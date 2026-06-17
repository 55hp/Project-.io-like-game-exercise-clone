using System;
using System.Collections.Generic;
using Clipper2Lib;
using UnityEngine;

public class ZoneInteractionManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Singleton
    // -------------------------------------------------------------------------

    public static ZoneInteractionManager Instance { get; private set; }

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

        allCharacters = new List<Character>(FindObjectsOfType<Character>());
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Adds c to allCharacters if not already present.</summary>
    public void RegisterCharacter(Character c)
    {
        if (!allCharacters.Contains(c))
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
                enemy.SetState(CharacterState.Dead);
                AbsorbAreaAndKill(attacker, enemy, -1, Vector3.zero);
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
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the absorber's zone as the geometric UNION of:
    ///   A's area, A's trail (thickened), B's trail trimmed at kill point (thickened), B's area.
    /// The victim's trail is cut at the kill segment — only the portion from zone edge
    /// up to and including the kill point is used. The "dead tail" beyond is discarded.
    /// Uses Clipper2Lib for a clean boolean union.
    /// Rolls back to A's previous area if triangulation fails.
    /// </summary>
    private void AbsorbAreaAndKill(Character absorber, Character victim, int killSegmentIndex, Vector3 killPoint)
    {
        List<Vector3> backup = absorber.areaVertices;

        // --- Build the four subject polygons ---
        PathsD subjects = new PathsD();

        // 1. A's area polygon.
        List<Vector3> absorberArea = absorber.GetAreaPolygon();
        if (absorberArea != null && absorberArea.Count >= 3)
            subjects.Add(Vector3ListToPathD(absorberArea));

        // 2. A's trail thickened into a corridor.
        List<Vector3> absorberTrail = absorber.GetTrailPoints();
        PathsD absorberCorridor = ThickenTrail(absorberTrail, absorber.trail);
        if (absorberCorridor != null)
            subjects.AddRange(absorberCorridor);

        // 3. B's trail TRIMMED at kill point, then thickened.
        List<Vector3> victimTrailFull = victim.GetTrailPoints();
        List<Vector3> victimTrailTrimmed = TrimTrailAtKillPoint(victimTrailFull, killSegmentIndex, killPoint);
        PathsD victimCorridor = ThickenTrail(victimTrailTrimmed, victim.trail);
        if (victimCorridor != null)
            subjects.AddRange(victimCorridor);

        // 4. B's area polygon.
        List<Vector3> victimArea = victim.GetAreaPolygon();
        if (victimArea != null && victimArea.Count >= 3)
            subjects.Add(Vector3ListToPathD(victimArea));

        // --- Execute Clipper Union ---
        PathsD solution = Clipper.Union(subjects, FillRule.NonZero);

        if (solution == null || solution.Count == 0)
        {
            Debug.LogWarning("[ZoneInteractionManager] Clipper Union returned empty result, rolled back.", absorber);
            CleanUpVictim(victim);
            return;
        }

        // Pick the largest resulting contour by absolute area.
        PathD largest = solution[0];
        double largestArea = Math.Abs(Clipper.Area(largest));

        for (int i = 1; i < solution.Count; i++)
        {
            double a = Math.Abs(Clipper.Area(solution[i]));
            if (a > largestArea)
            {
                largestArea = a;
                largest = solution[i];
            }
        }

        // Convert back to List<Vector3>.
        List<Vector3> newArea = PathDToVector3List(largest);

        // Assign and try to rebuild mesh.
        absorber.SetAreaVertices(newArea);

        if (!absorber.UpdateArea())
        {
            absorber.SetAreaVertices(backup);
            absorber.UpdateArea();
            Debug.LogWarning("[ZoneInteractionManager] AbsorbAreaAndKill triangulation failed after union, rolled back.", absorber);
        }

        // Clear A's trail.
        absorber.ResetTrail();

        // Destroy victim.
        CleanUpVictim(victim);
    }

    /// <summary>
    /// Trims the victim trail: keeps points from index 0 up to and including killSegmentIndex,
    /// then appends the exact killPoint. Everything after is discarded.
    /// If killSegmentIndex is negative, returns the full trail untrimmed (zone conquest case).
    /// </summary>
    private List<Vector3> TrimTrailAtKillPoint(List<Vector3> fullTrail, int killSegmentIndex, Vector3 killPoint)
    {
        if (fullTrail == null || fullTrail.Count < 2)
            return fullTrail ?? new List<Vector3>();

        // No trimming requested — use the entire trail.
        if (killSegmentIndex < 0)
            return fullTrail;

        // Clamp index to valid range.
        int lastIndex = Mathf.Min(killSegmentIndex, fullTrail.Count - 2);

        // Keep all points from the start up to the start of the kill segment (inclusive).
        List<Vector3> trimmed = new List<Vector3>(lastIndex + 2);
        for (int i = 0; i <= lastIndex; i++)
            trimmed.Add(fullTrail[i]);

        // Append the kill point as the terminal point of the trimmed trail.
        trimmed.Add(killPoint);

        return trimmed;
    }

    private void CleanUpVictim(Character victim)
    {
        victim.DestroyZone();
        UnregisterCharacter(victim);
        Destroy(victim.gameObject);
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

        // InflatePaths with EndType.Round creates a capsule-like corridor around the open path.
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
