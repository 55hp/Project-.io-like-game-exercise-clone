using System.Collections.Generic;
using UnityEngine;

public static class AreaManager
{
    // -------------------------------------------------------------------------
    // Ray-casting point-in-polygon test on the XZ plane.
    // -------------------------------------------------------------------------
    public static bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
    {
        if (polygon == null || polygon.Length < 3) return false;

        bool inside = false;
        int n = polygon.Length;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector2 pi = polygon[i];
            Vector2 pj = polygon[j];

            bool edgeCrossesRay = (pi.y > point.y) != (pj.y > point.y);
            if (!edgeCrossesRay) continue;

            float xIntersect = (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y) + pi.x;
            if (point.x < xIntersect)
                inside = !inside;
        }

        return inside;
    }

    // -------------------------------------------------------------------------
    // Merges newTrailVertices into the existing areaVertices polygon.
    //
    // Connects trail[0] → closest existing vertex (startIdx)
    //           trail[last] → closest existing vertex (endIdx)
    //
    // Tries two arcs:
    //   CW  – polygon arc from endIdx → startIdx going forward  (index +1)
    //   CCW – polygon arc from endIdx → startIdx going backward (index -1)
    // Each arc is prepended to the trail to close the polygon.
    //
    // Returns the candidate with the larger absolute shoelace area.
    // -------------------------------------------------------------------------
    public static List<Vector3> DeformArea(List<Vector3> areaVertices, List<Vector3> newTrailVertices)
    {
        if (areaVertices == null || areaVertices.Count < 3)   return areaVertices;
        if (newTrailVertices == null || newTrailVertices.Count < 2) return areaVertices;

        int n = areaVertices.Count;

        int startIdx = FindClosestIndex(areaVertices, newTrailVertices[0]);
        int endIdx   = FindClosestIndex(areaVertices, newTrailVertices[newTrailVertices.Count - 1]);

        if (startIdx == endIdx) return areaVertices;

        // -- CW candidate: arc endIdx → startIdx going forward, then trail --
        List<Vector3> cwCandidate = BuildArc(areaVertices, endIdx, startIdx, forward: true);
        cwCandidate.AddRange(newTrailVertices);

        // -- CCW candidate: arc endIdx → startIdx going backward, then trail --
        List<Vector3> ccwCandidate = BuildArc(areaVertices, endIdx, startIdx, forward: false);
        ccwCandidate.AddRange(newTrailVertices);

        float areaCW  = Mathf.Abs(ShoelaceArea(cwCandidate));
        float areaCCW = Mathf.Abs(ShoelaceArea(ccwCandidate));

        List<Vector3> best     = areaCW >= areaCCW ? cwCandidate : ccwCandidate;
        float         bestArea = Mathf.Max(areaCW, areaCCW);

        // Never shrink the zone: if neither candidate improves on the original,
        // discard the deformation and keep the existing polygon.
        float areaOriginal = Mathf.Abs(ShoelaceArea(areaVertices));
        if (bestArea < areaOriginal) return areaVertices;

        return best;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the index of the vertex in <paramref name="vertices"/> closest to <paramref name="target"/>.
    /// </summary>
    private static int FindClosestIndex(List<Vector3> vertices, Vector3 target)
    {
        int   closest    = 0;
        float minSqrDist = float.MaxValue;

        for (int i = 0; i < vertices.Count; i++)
        {
            float sqrDist = (vertices[i] - target).sqrMagnitude;
            if (sqrDist < minSqrDist)
            {
                minSqrDist = sqrDist;
                closest    = i;
            }
        }

        return closest;
    }

    /// <summary>
    /// Collects polygon vertices from <paramref name="fromIdx"/> to <paramref name="toIdx"/>
    /// (inclusive) stepping forward (+1 % n) or backward (-1 % n).
    /// </summary>
    private static List<Vector3> BuildArc(List<Vector3> vertices, int fromIdx, int toIdx, bool forward)
    {
        List<Vector3> arc = new List<Vector3>();
        int n       = vertices.Count;
        int i       = fromIdx;
        int safety  = 0;

        while (safety <= n)
        {
            arc.Add(vertices[i]);
            if (i == toIdx) break;
            i = forward ? (i + 1) % n : (i - 1 + n) % n;
            safety++;
        }

        return arc;
    }

    /// <summary>
    /// Signed shoelace area using the X and Z components (XZ plane).
    /// Positive = CCW winding; negative = CW winding.
    /// </summary>
    private static float ShoelaceArea(List<Vector3> poly)
    {
        float area = 0f;
        int   n    = poly.Count;

        for (int i = 0, j = n - 1; i < n; j = i++)
            area += poly[j].x * poly[i].z - poly[i].x * poly[j].z;

        return area * 0.5f;
    }
}
