using System;
using System.Collections.Generic;
using Clipper2Lib;
using HP55.Audio;
using UnityEngine;

public class ZoneInteractionManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Singleton
    // -------------------------------------------------------------------------

    public static ZoneInteractionManager Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Reveal settings
    // -------------------------------------------------------------------------

    [Header("Participants (assign manually)")]
    [Tooltip("The player character.")]
    [SerializeField] private Character player;
    [Tooltip("Only the ENEMIES that are active in this scene. Leave disabled NPCs out.")]
    [SerializeField] private List<Character> enemies = new List<Character>();

    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private List<Character> allCharacters = new List<Character>();

    // -------------------------------------------------------------------------
    // Unity messages
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Deterministic population: only the manually assigned participants.
        // No FindObjectsOfType, so execution order no longer affects the list.
        allCharacters = new List<Character>();

        if (player != null && !allCharacters.Contains(player))
            allCharacters.Add(player);

        if (enemies != null)
        {
            foreach (Character e in enemies)
            {
                if (e != null && !allCharacters.Contains(e))
                    allCharacters.Add(e);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Optional safety add. The list is normally populated from the manually
    /// assigned player + enemies fields in Awake; this only adds c if missing,
    /// so a stray call (e.g. from WaypointMover.Start) can never create duplicates.
    /// </summary>
    public void RegisterCharacter(Character c)
    {
        if (c != null && !allCharacters.Contains(c))
            allCharacters.Add(c);
    }

    /// <summary>Removes c from allCharacters.</summary>
    public void UnregisterCharacter(Character c)
    {
        allCharacters.Remove(c);
    }

    /// <summary>
    /// Called every frame by WaypointMover while paintZone is true.
    /// If mover's position touches an enemy's active trail:
    ///   1. The trail OWNER (enemy) is killed.
    ///   2. The enemy's zone area is absorbed into the mover's zone.
    /// Only enemies in Trailing state can be killed via trail collision.
    /// </summary>
    public void CheckTrailKill(Character mover)
    {
        Vector2 moverPos = new Vector2(
            mover.transform.position.x,
            mover.transform.position.z);

        List<Character> snapshot = new List<Character>(allCharacters);

        foreach (Character enemy in snapshot)
        {
            if (enemy == mover) continue;
            if (enemy == null) continue;
            if (enemy.State != CharacterState.Trailing) continue;

            List<Vector3> enemyTrail = enemy.GetTrailPoints();

            if (enemyTrail == null || enemyTrail.Count < 2) continue;

            for (int i = 0; i < enemyTrail.Count - 1; i++)
            {
                Vector2 a = new Vector2(enemyTrail[i].x, enemyTrail[i].z);
                Vector2 b = new Vector2(enemyTrail[i + 1].x, enemyTrail[i + 1].z);

                float dist = DistancePointToSegment(moverPos, a, b);

                float enemyHalf = enemy.trail != null ? enemy.trail.startWidth * 0.5f : 0.25f;
                float moverHalf = mover.trail != null ? mover.trail.startWidth * 0.5f : 0.25f;
                float killThreshold = enemyHalf + moverHalf;

                if (dist < killThreshold)
                {
                    // Compute exact kill point on the segment.
                    Vector2 killPoint2D = ClosestPointOnSegment(moverPos, a, b);
                    Vector3 killPoint = new Vector3(killPoint2D.x, 0f, killPoint2D.y);

                    enemy.SetState(CharacterState.Dead);
                    AudioManager.Instance.PlaySFX("kill");
                    AbsorbAreaAndKill(mover, enemy, i, killPoint);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Called by WaypointMover just before ReturnToZone().
    /// When the attacker closes a trail that overlaps an enemy zone,
    /// the overlapping portion is removed from the enemy and added to the attacker.
    /// If the enemy zone collapses to less than 3 vertices, the enemy is killed
    /// and its entire remaining area is absorbed.
    /// </summary>
    public void CheckZoneConquest(Character attacker, List<Vector3> trailVertices)
    {
        if (trailVertices == null || trailVertices.Count < 2) return;

        // Build the corridor polygon (ribbon) that accounts for trail width.
        float halfWidth = attacker.trail != null ? attacker.trail.startWidth * 0.5f : 0f;
        Vector2[] corridorPoly = halfWidth > 0f
            ? Character.BuildCorridorPolygon(trailVertices, halfWidth)
            : null;

        // Fallback: if no valid corridor, use the raw center-line as a thin polygon.
        Vector2[] trailPoly = corridorPoly ?? ToVector2Array(trailVertices);

        // Snapshot to safely handle list mutation during iteration.
        List<Character> snapshot = new List<Character>(allCharacters);

        foreach (Character enemy in snapshot)
        {
            if (enemy == attacker) continue;
            if (enemy == null) continue;
            if (enemy.areaVertices == null || enemy.areaVertices.Count < 3) continue;

            // Check if any enemy vertex falls inside the attacker's trail corridor.
            bool hasOverlap = false;
            foreach (Vector3 v in enemy.areaVertices)
            {
                if (AreaManager.IsPointInPolygon(new Vector2(v.x, v.z), trailPoly))
                {
                    hasOverlap = true;
                    break;
                }
            }

            if (!hasOverlap) continue;

            // Separate enemy vertices: those inside the corridor go to attacker,
            // those outside remain with the enemy.
            List<Vector3> conquered = new List<Vector3>();
            List<Vector3> surviving = new List<Vector3>();

            foreach (Vector3 v in enemy.areaVertices)
            {
                if (AreaManager.IsPointInPolygon(new Vector2(v.x, v.z), trailPoly))
                    conquered.Add(v);
                else
                    surviving.Add(v);
            }

            // If the enemy zone is entirely consumed → full absorb and kill.
            if (surviving.Count < 3)
            {
                // Reveal centre for an enclosure kill: the centroid of the victim area,
                // since there is no single collision point. killSegmentIndex = -1 means
                // "use the full victim trail" inside AbsorbAreaAndKill.
                enemy.SetState(CharacterState.Dead);
                AudioManager.Instance.PlaySFX("kill");
                Vector3 enclosureCentre = CentroidXZ(enemy.GetAreaPolygon());
                AbsorbAreaAndKill(attacker, enemy, -1, enclosureCentre);
                continue;
            }

            // Shrink enemy zone to surviving vertices.
            enemy.SetAreaVertices(surviving);
            enemy.UpdateArea();

            // Note: conquered vertices are NOT explicitly added to the attacker here
            // because the attacker's trail-based DeformArea (called right after in
            // WaypointMover) will already expand its zone to cover the trail corridor.
            // Adding them would cause duplicate/overlapping geometry.
        }
    }

    // -------------------------------------------------------------------------
    // Absorb & kill
    // -------------------------------------------------------------------------

    /// <summary>
    /// Single deterministic path for both trail-kill and full-enclosure conquest.
    ///
    /// Two areas are computed:
    ///   - FULL logical area  = attacker area + attacker trail + victim trimmed trail + victim area.
    ///     Assigned to areaVertices so the collider / IsInsideZone include the conquered land
    ///     immediately and the character never re-draws trail over it.
    ///   - RENDER area (during the reveal) = the same union WITHOUT the victim area, so the main
    ///     zone mesh grows only up to the kill point and does NOT yet show the conquered region.
    ///
    /// The conquered region (victim area) is then revealed by a SEPARATE temporary mesh that
    /// animates its own _RevealRadius from 0 outward over ~1s. Because that mesh contains only the
    /// conquered region, the radius it needs is small and the sweep is clearly visible. On complete,
    /// the main mesh is committed to the full logical area and the temporary mesh is destroyed.
    /// No single-radius limitation, no coplanar double-draw.
    /// </summary>
    private void AbsorbAreaAndKill(Character absorber, Character victim, int killSegmentIndex, Vector3 revealCentre)
    {
        // --- Capture everything we need BEFORE mutating/destroying ---
        List<Vector3> preConquestArea   = absorber.GetAreaPolygon();
        List<Vector3> absorberTrailRaw  = new List<Vector3>(absorber.GetTrailPoints());
        List<Vector3> victimAreaPolygon = victim.GetAreaPolygon();
        Material      zoneMat           = absorber.GetZoneMaterial();

        // --- Shared subjects: attacker area + attacker corridor + victim TRIMMED corridor ---
        // These three define the attacker's reach UP TO the kill point (no conquered region yet).
        PathsD baseSubjects = new PathsD();

        if (preConquestArea != null && preConquestArea.Count >= 3)
            baseSubjects.Add(Vector3ListToPathD(preConquestArea));

        PathsD absorberCorridor = ThickenTrail(absorberTrailRaw, absorber.trail);
        if (absorberCorridor != null)
            baseSubjects.AddRange(absorberCorridor);

        List<Vector3> victimTrailFull    = victim.GetTrailPoints();
        List<Vector3> victimTrailTrimmed = TrimTrailAtKillPoint(victimTrailFull, killSegmentIndex, revealCentre);
        PathsD victimCorridor = ThickenTrail(victimTrailTrimmed, victim.trail);
        if (victimCorridor != null)
            baseSubjects.AddRange(victimCorridor);

        // RENDER area = union of base subjects only (no victim area).
        List<Vector3> renderArea = UnionToPolygon(baseSubjects);

        // FULL area = base subjects + victim area.
        PathsD fullSubjects = new PathsD(baseSubjects);
        if (victimAreaPolygon != null && victimAreaPolygon.Count >= 3)
            fullSubjects.Add(Vector3ListToPathD(victimAreaPolygon));
        List<Vector3> fullArea = UnionToPolygon(fullSubjects);

        // --- Apply: logical area = full, rendered mesh = render area (conquered region hidden) ---
        List<Vector3> backup = absorber.areaVertices;
        bool applied = false;

        if (fullArea != null && fullArea.Count >= 3)
        {
            absorber.SetAreaVertices(fullArea);                 // logical (collision) = full
            List<Vector3> meshNow = (renderArea != null && renderArea.Count >= 3) ? renderArea : fullArea;
            if (absorber.UpdateRenderedArea(meshNow))           // mesh = render area (no conquered region)
                applied = true;
            else
            {
                absorber.SetAreaVertices(backup);
                absorber.UpdateArea();
                Debug.LogWarning("[ZoneInteractionManager] Absorb render mesh failed, rolled back.", absorber);
            }
        }
        else
        {
            Debug.LogWarning("[ZoneInteractionManager] Absorb full union empty, skipping.", absorber);
        }

        // Clear A's trail so it restarts fresh from here.
        absorber.ResetTrail();

        // Destroy the victim and its old zone visual immediately.
        CleanUpVictim(victim);

        // --- Reveal: animate the conquered region on a SEPARATE temporary mesh from radius 0 ---
        if (applied && victimAreaPolygon != null && victimAreaPolygon.Count >= 3 && zoneMat != null)
        {
            ConquestRevealAnimator animator = FindOrCreateRevealAnimator();
            animator.PlayReveal(
                absorber,
                victimAreaPolygon,
                revealCentre,
                zoneMat,
                onComplete: () =>
                {
                    if (absorber != null)
                        absorber.CommitRenderedArea(); // main mesh now shows the full logical area
                });
        }
        else if (applied && absorber != null)
        {
            // No reveal possible — just commit the full area to the visible mesh.
            absorber.CommitRenderedArea();
        }
    }

    /// <summary>Unions the given subjects and returns the largest contour as a Vector3 polygon, or null.</summary>
    private List<Vector3> UnionToPolygon(PathsD subjects)
    {
        if (subjects == null || subjects.Count == 0) return null;

        PathsD solution = Clipper.Union(subjects, FillRule.NonZero);
        if (solution == null || solution.Count == 0) return null;

        return PathDToVector3List(PickLargestContour(solution));
    }

    /// <summary>Finds or creates the single ConquestRevealAnimator used to play reveal animations.</summary>
    private ConquestRevealAnimator FindOrCreateRevealAnimator()
    {
        ConquestRevealAnimator animator = FindObjectOfType<ConquestRevealAnimator>();
        if (animator == null)
        {
            GameObject go = new GameObject("ConquestRevealAnimator");
            animator = go.AddComponent<ConquestRevealAnimator>();
        }
        return animator;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Returns the contour with the largest absolute area from a PathsD result.</summary>
    private static PathD PickLargestContour(PathsD paths)
    {
        PathD largest = paths[0];
        double largestArea = Math.Abs(Clipper.Area(largest));

        for (int i = 1; i < paths.Count; i++)
        {
            double a = Math.Abs(Clipper.Area(paths[i]));
            if (a > largestArea)
            {
                largestArea = a;
                largest = paths[i];
            }
        }

        return largest;
    }

    /// <summary>
    /// Trims the victim trail: keeps points from index 0 up to and including killSegmentIndex,
    /// then appends the exact kill point. Everything after is discarded.
    /// If killSegmentIndex is negative, returns the full trail untrimmed (enclosure case).
    /// </summary>
    private List<Vector3> TrimTrailAtKillPoint(List<Vector3> fullTrail, int killSegmentIndex, Vector3 killPoint)
    {
        if (fullTrail == null || fullTrail.Count < 2)
            return fullTrail ?? new List<Vector3>();

        if (killSegmentIndex < 0)
            return fullTrail;

        int lastIndex = Mathf.Min(killSegmentIndex, fullTrail.Count - 2);

        List<Vector3> trimmed = new List<Vector3>(lastIndex + 2);
        for (int i = 0; i <= lastIndex; i++)
            trimmed.Add(fullTrail[i]);

        trimmed.Add(killPoint);
        return trimmed;
    }

    private void CleanUpVictim(Character victim)
    {
        victim.DestroyZone();
        UnregisterCharacter(victim);
        Destroy(victim.gameObject);
    }

    private static Vector3 CentroidXZ(List<Vector3> poly)
    {
        if (poly == null || poly.Count == 0) return Vector3.zero;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < poly.Count; i++) sum += poly[i];
        sum /= poly.Count;
        return new Vector3(sum.x, 0f, sum.z);
    }

    // -------------------------------------------------------------------------
    // Clipper2 helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Thickens a polyline trail into a closed corridor polygon using Clipper InflatePaths.
    /// Returns null if the trail is too short or width is zero.
    /// </summary>
    private PathsD ThickenTrail(List<Vector3> trailPoints, TrailRenderer trail)
    {
        if (trailPoints == null || trailPoints.Count < 2) return null;

        float width = trail != null ? trail.startWidth : 0f;
        if (width <= 0f) return null;

        PathD openPath = Vector3ListToPathD(trailPoints);
        PathsD input = new PathsD { openPath };

        double delta = width * 0.5;
        PathsD corridors = Clipper.InflatePaths(input, delta, JoinType.Round, EndType.Round);

        return corridors != null && corridors.Count > 0 ? corridors : null;
    }

    private static PathD Vector3ListToPathD(List<Vector3> verts)
    {
        PathD path = new PathD(verts.Count);
        for (int i = 0; i < verts.Count; i++)
            path.Add(new PointD(verts[i].x, verts[i].z));
        return path;
    }

    private static List<Vector3> PathDToVector3List(PathD path)
    {
        List<Vector3> result = new List<Vector3>(path.Count);
        for (int i = 0; i < path.Count; i++)
            result.Add(new Vector3((float)path[i].x, 0f, (float)path[i].y));
        return result;
    }

    private static Vector2 ClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return a + t * ab;
    }

    private float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        Vector2 ap = p - a;
        float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude);
        Vector2 closest = a + t * ab;
        return Vector2.Distance(p, closest);
    }

    private static Vector2[] ToVector2Array(List<Vector3> verts)
    {
        Vector2[] result = new Vector2[verts.Count];
        for (int i = 0; i < verts.Count; i++)
            result[i] = new Vector2(verts[i].x, verts[i].z);
        return result;
    }
}