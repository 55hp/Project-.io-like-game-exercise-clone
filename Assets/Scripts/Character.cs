using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum CharacterState { InZone, Trailing, Dead }

[DisallowMultipleComponent]
public class Character : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    [Header("Identity")]
    public string characterName = "Player";
    public Color  zoneColor     = Color.blue;

    [Header("Zone Settings")]
    [SerializeField] private float startAreaRadius = 3f;
    [SerializeField] private int   startAreaPoints = 36;

    // Public data – only this class can reassign the list reference.
    public List<Vector3> areaVertices { get; private set; }

    // State machine.
    public CharacterState State { get; private set; } = CharacterState.InZone;

    public void SetState(CharacterState newState) { State = newState; }

    // Zone child object refs.
    private GameObject   zoneGO;
    private MeshFilter   zoneFilter;
    private MeshRenderer zoneRenderer;
    private MeshCollider zoneCollider;

    [Header("Trail Deformation")]
    [SerializeField] private int minTrailPointsForDeformation = 5;

    [Header("Trail")]
    private const float MIN_POINT_DISTANCE = 0.05f;
    private List<Vector3> newAreaVertices = new List<Vector3>();
    public TrailRenderer trail;

    // -------------------------------------------------------------------------
    // Unity messages
    // -------------------------------------------------------------------------

    private void Awake()
    {
        areaVertices = new List<Vector3>();
    }

    // Start() rimosso: InitializeZone() è ora responsabilità di WaypointMover.Start()
    // oppure richiamabile manualmente via ContextMenu.

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    [ContextMenu("Initialize Zone")]
    public void InitializeZone()
    {
        // Remove any pre-existing zone so re-runs stay clean.
        // The zone is detached to scene root, so we use the stored reference —
        // transform.Find() would NOT find it there.
        string zoneName = characterName + "Zone";
        if (zoneGO != null)
        {
#if UNITY_EDITOR
            DestroyImmediate(zoneGO);
#else
            Destroy(zoneGO);
#endif
            zoneGO = null;
        }
        else
        {
            // Fallback: search by name in case the ref was lost (e.g. after domain reload).
            GameObject stale = GameObject.Find(zoneName);
            if (stale != null)
            {
#if UNITY_EDITOR
                DestroyImmediate(stale);
#else
                Destroy(stale);
#endif
            }
        }

        // --- child GameObject -------------------------------------------------
        zoneGO = new GameObject(zoneName);
        zoneGO.transform.SetParent(transform, worldPositionStays: false);
        zoneGO.transform.SetParent(null, false); // detach to scene root; local (0,0,0) → world (0,0,0)

        zoneFilter   = zoneGO.AddComponent<MeshFilter>();
        zoneRenderer = zoneGO.AddComponent<MeshRenderer>();
        zoneCollider = zoneGO.AddComponent<MeshCollider>();

        // --- URP Lit material (transparent) -----------------------------------
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null)
        {
            Debug.LogWarning("[Character] URP Lit shader not found. Is URP installed?");
            litShader = Shader.Find("Standard");
        }

        Material mat = new Material(litShader);

        Color baseColor = zoneColor;
        baseColor.a = 0.65f;
        mat.SetColor("_BaseColor", baseColor);

        // Surface type: Transparent
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend",   0f);   // Alpha blend
        mat.SetFloat("_ZWrite",  0f);
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;

        zoneRenderer.sharedMaterial   = mat;
        zoneRenderer.shadowCastingMode = ShadowCastingMode.Off;
        zoneRenderer.receiveShadows    = false;

        // --- Build initial circular polygon -----------------------------------
        if (areaVertices == null) areaVertices = new List<Vector3>();
        areaVertices.Clear();

        Vector3 center = transform.position;

        for (int i = 0; i < startAreaPoints; i++)
        {
            float angle = (float)i / startAreaPoints * Mathf.PI * 2f;
            areaVertices.Add(new Vector3(
                center.x + Mathf.Cos(angle) * startAreaRadius,
                center.y,
                center.z + Mathf.Sin(angle) * startAreaRadius
            ));
        }

        UpdateArea();
    }

    /// <summary>Rebuilds the zone mesh from areaVertices. Returns true if successful.</summary>
    public bool UpdateArea()
    {
        if (zoneFilter == null || areaVertices == null || areaVertices.Count < 3) return false;

        Mesh mesh = BuildMesh(areaVertices);
        if (mesh == null) return false;

        zoneFilter.sharedMesh   = mesh;
        zoneCollider.sharedMesh = null;   // force collider refresh
        zoneCollider.sharedMesh = mesh;
        return true;
    }

    /// <summary>True while the player has accumulated at least one trail point outside the zone.</summary>
    public bool IsDrawingTrail => newAreaVertices != null && newAreaVertices.Count > 1;

    /// <summary>
    /// Read-only access to the current trail points for external systems (e.g. ZoneInteractionManager).
    /// The list is cleared by ReturnToZone(); never hold a reference across frames.
    /// </summary>
    public List<Vector3> TrailVertices => newAreaVertices;

    /// <summary>Returns true when worldPos is inside the current zone polygon.</summary>
    public bool IsInsideZone(Vector3 worldPos)
    {
        if (areaVertices == null || areaVertices.Count < 3) return false;
        return AreaManager.IsPointInPolygon(new Vector2(worldPos.x, worldPos.z), Vertices2D(areaVertices));
    }

    /// <summary>
    /// Returns true when worldPos is outside the zone OR within <paramref name="margin"/>
    /// world-units of its boundary. Use this to start the trail slightly before the
    /// character visually crosses the edge.
    /// </summary>
    public bool IsNearOrOutsideZone(Vector3 worldPos, float margin)
    {
        if (areaVertices == null || areaVertices.Count < 3) return true;

        Vector2 p = new Vector2(worldPos.x, worldPos.z);
        Vector2[] poly = Vertices2D(areaVertices);

        if (!AreaManager.IsPointInPolygon(p, poly)) return true;

        // Inside zone: check distance to nearest boundary segment.
        for (int i = 0; i < areaVertices.Count; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % poly.Length];
            if (DistancePointToSegment2D(p, a, b) < margin) return true;
        }

        return false;
    }

    private static float DistancePointToSegment2D(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        Vector2 ap = p - a;
        float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + t * ab);
    }

    /// <summary>
    /// Offsets each point of <paramref name="trail"/> outward (away from the zone centroid)
    /// by <paramref name="offset"/> units, perpendicular to the local trail tangent on the XZ plane.
    /// Used to make the captured area include the full visual width of the TrailRenderer.
    /// </summary>
    private List<Vector3> OffsetTrailOutward(List<Vector3> trail, float offset)
    {
        if (trail.Count < 2) return trail;

        // Zone centroid as "inside" reference.
        Vector3 centroid = Vector3.zero;
        foreach (Vector3 v in areaVertices) centroid += v;
        centroid /= areaVertices.Count;

        List<Vector3> result = new List<Vector3>(trail.Count);

        for (int i = 0; i < trail.Count; i++)
        {
            // Non offsettare il primo e l'ultimo punto: devono rimanere ancorati
            // al confine della zona per DeformArea.FindClosestIndex.
            if (i == 0 || i == trail.Count - 1)
            {
                result.Add(trail[i]);
                continue;
            }

            // Tangent: central difference for interior points.
            Vector3 tangent = trail[i + 1] - trail[i - 1];
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 0.0001f)
            {
                result.Add(trail[i]);
                continue;
            }
            tangent.Normalize();

            // One of the two XZ perpendiculars.
            Vector3 perp = new Vector3(-tangent.z, 0f, tangent.x);

            // Flip if it points toward the centroid instead of away from it.
            Vector3 toCentroid = centroid - trail[i];
            toCentroid.y = 0f;
            if (Vector3.Dot(perp, toCentroid) > 0f) perp = -perp;

            result.Add(trail[i] + perp * offset);
        }

        return result;
    }

    public List<Vector3> GetTrailPoints()
    {
        return new List<Vector3>(newAreaVertices);
    }

    /// <summary>Returns a copy of the current area polygon vertices.</summary>
    public List<Vector3> GetAreaPolygon()
    {
        return new List<Vector3>(areaVertices);
    }

    /// <summary>
    /// Returns the active trail thickened into a closed corridor polygon using the
    /// TrailRenderer startWidth. Returns an empty list if the trail has fewer than 2 points.
    /// </summary>
    public List<Vector3> GetTrailPolygon()
    {
        if (newAreaVertices.Count < 2)
            return new List<Vector3>();

        float halfWidth = trail != null ? trail.startWidth * 0.5f : 0.25f;
        Vector2[] corridor = BuildCorridorPolygon(newAreaVertices, halfWidth);

        if (corridor == null || corridor.Length == 0)
            return new List<Vector3>();

        List<Vector3> result = new List<Vector3>(corridor.Length);
        for (int i = 0; i < corridor.Length; i++)
            result.Add(new Vector3(corridor[i].x, 0f, corridor[i].y));
        return result;
    }

    /// <summary>Returns true if there is a real active trail (at least 2 points).</summary>
    public bool HasActiveTrail()
    {
        return newAreaVertices.Count >= 2;
    }

    /// <summary>
    /// Static version for external use: builds a corridor polygon from any center line.
    /// </summary>
    public static Vector2[] BuildCorridorPolygon(List<Vector3> centerLine, float halfWidth)
    {
        int n = centerLine.Count;
        if (n < 2 || halfWidth <= 0f) return null;

        Vector2[] left  = new Vector2[n];
        Vector2[] right = new Vector2[n];

        for (int i = 0; i < n; i++)
        {
            Vector2 tangent;
            Vector2 pi = new Vector2(centerLine[i].x, centerLine[i].z);

            if      (i == 0)     tangent = new Vector2(centerLine[1].x, centerLine[1].z) - pi;
            else if (i == n - 1) tangent = pi - new Vector2(centerLine[n - 2].x, centerLine[n - 2].z);
            else                 tangent = new Vector2(centerLine[i + 1].x, centerLine[i + 1].z)
                                         - new Vector2(centerLine[i - 1].x, centerLine[i - 1].z);

            if (tangent.sqrMagnitude < 0.0001f) tangent = Vector2.right;
            tangent.Normalize();

            // Perpendicolare (rotate 90° CCW).
            Vector2 perp = new Vector2(-tangent.y, tangent.x);

            left[i]  = pi + perp * halfWidth;
            right[i] = pi - perp * halfWidth;
        }

        // Poligono chiuso: lato sinistro in avanti + lato destro in retromarcia.
        Vector2[] polygon = new Vector2[n * 2];
        for (int i = 0; i < n; i++) polygon[i]     = left[i];
        for (int i = 0; i < n; i++) polygon[n + i] = right[n - 1 - i];

        return polygon;
    }

    /// <summary>Replaces areaVertices with the supplied list. Called by ZoneInteractionManager on conquest.</summary>
    public void SetAreaVertices(List<Vector3> verts)
    {
        areaVertices = verts;
    }

    /// <summary>Destroys the zone GameObject immediately. Called before the character is killed.</summary>
    public void DestroyZone()
    {
        if (zoneGO != null)
        {
#if UNITY_EDITOR
            DestroyImmediate(zoneGO);
#else
            Destroy(zoneGO);
#endif
            zoneGO = null;
        }
    }

    /// <summary>
    /// Clears the active trail (newAreaVertices) and resets the TrailRenderer.
    /// Called after an immediate zone rebuild so the trail restarts fresh.
    /// </summary>
    public void ResetTrail()
    {
        newAreaVertices.Clear();

        if (trail != null)
        {
            trail.Clear();
            trail.emitting = false;
            trail.time = 0.5f;
        }
    }

    public void AddTrailPoint(Vector3 worldPos)
    {
        bool tooClose = newAreaVertices.Count > 0 &&
                        Vector3.Distance(worldPos, newAreaVertices[newAreaVertices.Count - 1]) <= MIN_POINT_DISTANCE;
        if (tooClose) return;

        // Distanza minima al boundary calcolata sui SEGMENTI (non sui vertici).
        float minDistToBoundary = float.MaxValue;
        Vector2 p2D = new Vector2(worldPos.x, worldPos.z);
        for (int i = 0; i < areaVertices.Count; i++)
        {
            Vector2 a = new Vector2(areaVertices[i].x, areaVertices[i].z);
            Vector2 b = new Vector2(areaVertices[(i + 1) % areaVertices.Count].x,
                                    areaVertices[(i + 1) % areaVertices.Count].z);
            float d = DistancePointToSegment2D(p2D, a, b);
            if (d < minDistToBoundary) minDistToBoundary = d;
        }

        bool insideZone = AreaManager.IsPointInPolygon(p2D, Vertices2D(areaVertices));

        // Accetta il punto se è fuori zona, oppure se è dentro ma vicino al confine.
        // La soglia tiene conto del mezzo-spessore della trail per non tagliare punti utili.
        float boundaryTolerance = trail != null ? trail.startWidth * 0.5f + 0.1f : 0.3f;
        if (insideZone && minDistToBoundary > boundaryTolerance) return;

        newAreaVertices.Add(worldPos);

        State = CharacterState.Trailing;

        if (trail != null)
        {
            trail.time     = Mathf.Infinity;
            trail.emitting = true;
        }
    }

    public void ReturnToZone()
    {
        if (newAreaVertices.Count >= minTrailPointsForDeformation)
        {
            List<Vector3> trailForDeform = newAreaVertices;
            if (trail != null && trail.startWidth > 0f)
                trailForDeform = OffsetTrailOutward(newAreaVertices, trail.startWidth * 0.5f);

            List<Vector3> candidate = AreaManager.DeformArea(areaVertices, trailForDeform);

            // Atomic commit: only accept the new polygon if it triangulates successfully.
            // This prevents areaVertices from diverging from the visible mesh when the
            // triangulator rejects a degenerate or self-intersecting candidate.
            if (!ReferenceEquals(candidate, areaVertices))
            {
                List<Vector3> backup = areaVertices;
                areaVertices = candidate;

                if (!UpdateArea())
                {
                    // Triangulation failed — roll back to keep data and mesh in sync.
                    areaVertices = backup;
                    Debug.LogWarning($"[Character] Zone deformation rejected for '{characterName}': triangulation failed. Keeping previous polygon.", this);
                }
            }
        }

        // Always clear so no stale trail points corrupt the next drawing session.
        newAreaVertices.Clear();

        if (trail != null)
        {
            trail.Clear();
            trail.emitting = false;
            trail.time     = 0.5f;
        }

        State = CharacterState.InZone;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private Mesh BuildMesh(List<Vector3> verts)
    {
        Vector2[] verts2D = Vertices2D(verts);
        int[]     tris    = new Triangulator(verts2D).Triangulate();

        if (tris == null || tris.Length < 3) return null;

        // Zone lives at world (0,0,0) with no parent — local space == world space.
        // Pass vertices directly without conversion.
        Mesh mesh = new Mesh { name = characterName + "ZoneMesh" };
        mesh.vertices  = verts.ToArray();
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector2[] Vertices2D(List<Vector3> verts)
    {
        Vector2[] result = new Vector2[verts.Count];
        for (int i = 0; i < verts.Count; i++)
            result[i] = new Vector2(verts[i].x, verts[i].z);
        return result;
    }
}
