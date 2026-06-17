using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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

    // Zone child object refs.
    private GameObject   zoneGO;
    private MeshFilter   zoneFilter;
    private MeshRenderer zoneRenderer;
    private MeshCollider zoneCollider;

    [Header("Trail Deformation")]
    [SerializeField] private int minTrailPointsForDeformation = 5;

    [Header("Trail")]
    [SerializeField] private float minPointDistance = 0.1f;
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

    /// <summary>Returns true when worldPos is inside the current zone polygon.</summary>
    public bool IsInsideZone(Vector3 worldPos)
    {
        if (areaVertices == null || areaVertices.Count < 3) return false;
        return AreaManager.IsPointInPolygon(new Vector2(worldPos.x, worldPos.z), Vertices2D(areaVertices));
    }

    /// <summary>
    /// Returns true when at least <paramref name="threshold"/> fraction of the player's
    /// footprint sample points (5×5 grid within the square of side 2*radius) are outside
    /// the zone. Use this instead of IsInsideZone for overlap-based trail activation.
    /// </summary>
    public bool IsLargelyOutsideZone(Vector3 worldPos, float radius, float threshold = 0.95f)
    {
        if (areaVertices == null || areaVertices.Count < 3) return true;

        Vector2[] polygon  = Vertices2D(areaVertices);
        const int steps    = 4;                        // (steps+1)² = 25 sample points
        float     stepSize = (radius * 2f) / steps;
        int       outside  = 0;
        int       total    = 0;

        for (int xi = 0; xi <= steps; xi++)
        {
            for (int zi = 0; zi <= steps; zi++)
            {
                float x = worldPos.x - radius + xi * stepSize;
                float z = worldPos.z - radius + zi * stepSize;
                total++;
                if (!AreaManager.IsPointInPolygon(new Vector2(x, z), polygon))
                    outside++;
            }
        }

        return total > 0 && (float)outside / total >= threshold;
    }

    public void AddTrailPoint(Vector3 worldPos)
    {
        bool tooClose = newAreaVertices.Count > 0 &&
                        Vector3.Distance(worldPos, newAreaVertices[newAreaVertices.Count - 1]) <= minPointDistance;
        if (tooClose) return;

        newAreaVertices.Add(worldPos);

        if (trail != null)
        {
            trail.time    = Mathf.Infinity;
            trail.emitting = true;
        }
    }

    public void ReturnToZone()
    {
        if (newAreaVertices.Count >= minTrailPointsForDeformation)
        {
            List<Vector3> candidate = AreaManager.DeformArea(areaVertices, newAreaVertices);

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
