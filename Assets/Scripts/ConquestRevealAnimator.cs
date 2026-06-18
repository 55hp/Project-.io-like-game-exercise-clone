using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class ConquestRevealAnimator : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Settings
    // -------------------------------------------------------------------------

    [Header("Animation")]
    [SerializeField] private float revealDuration = 1f;
    [SerializeField] private Ease revealEase = Ease.OutQuad;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Plays a radial reveal animation over the conquered polygon, then invokes onComplete.
    /// </summary>
    public void PlayReveal(
        Character attacker,
        List<Vector3> conqueredPolygon,
        Vector3 collisionPoint,
        Material skinMaterial,
        Action onComplete)
    {
        if (conqueredPolygon == null || conqueredPolygon.Count < 3)
        {
            onComplete?.Invoke();
            return;
        }

        // 1. Create temporary GameObject.
        GameObject revealGO = new GameObject("ConquestReveal");
        // Slight Y lift so the reveal mesh never z-fights the main zone mesh where edges meet.
        revealGO.transform.position = new Vector3(0f, 0.02f, 0f);

        MeshFilter meshFilter = revealGO.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = revealGO.AddComponent<MeshRenderer>();

        // 2. Build mesh from conqueredPolygon.
        Mesh mesh = BuildMesh(conqueredPolygon);
        if (mesh == null)
        {
            Destroy(revealGO);
            onComplete?.Invoke();
            return;
        }
        meshFilter.sharedMesh = mesh;

        // 3. Assign material instance.
        Material matInstance = new Material(skinMaterial);
        meshRenderer.sharedMaterial = matInstance;

        // 4. Set reveal mechanic properties only. All aesthetics (colour, texture, edge
        //    glow, animation) are baked into the assigned zone material asset — code never
        //    touches them. We only drive the radial reveal here.
        Vector4 center = new Vector4(collisionPoint.x, 0f, collisionPoint.z, 0f);
        matInstance.SetVector("_RevealCenter", center);
        matInstance.SetFloat("_RevealRadius", 0f);

        // 5. Compute max radius on XZ plane.
        float maxRadius = 0f;
        Vector2 collisionXZ = new Vector2(collisionPoint.x, collisionPoint.z);
        for (int i = 0; i < conqueredPolygon.Count; i++)
        {
            float dist = Vector2.Distance(
                collisionXZ,
                new Vector2(conqueredPolygon[i].x, conqueredPolygon[i].z));
            if (dist > maxRadius) maxRadius = dist;
        }

        // Add softness margin so the entire polygon is fully revealed at the end.
        float softness = matInstance.HasProperty("_RevealSoftness")
            ? matInstance.GetFloat("_RevealSoftness")
            : 0.5f;
        maxRadius += softness;

        // 6. Animate _RevealRadius with DOTween.
        float currentRadius = 0f;
        DOTween.To(
            () => currentRadius,
            value =>
            {
                currentRadius = value;
                if (matInstance != null)
                    matInstance.SetFloat("_RevealRadius", value);
            },
            maxRadius,
            revealDuration)
            .SetEase(revealEase)
            .OnComplete(() =>
            {
                // 7. Cleanup and callback.
                Destroy(revealGO);
                Destroy(matInstance);
                onComplete?.Invoke();
            });
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    // Must match Character.uvWorldScale so the conquered region's texture lines up
    // seamlessly with the rest of the zone during the reveal.
    private const float UV_WORLD_SCALE = 4f;

    private static Mesh BuildMesh(List<Vector3> verts)
    {
        Vector2[] verts2D = new Vector2[verts.Count];
        for (int i = 0; i < verts.Count; i++)
            verts2D[i] = new Vector2(verts[i].x, verts[i].z);

        int[] tris = new Triangulator(verts2D).Triangulate();
        if (tris == null || tris.Length < 3) return null;

        Mesh mesh = new Mesh { name = "ConquestRevealMesh" };
        mesh.vertices = verts.ToArray();
        mesh.triangles = tris;

        // Same planar world-space UV mapping as Character.BuildMesh so the texture is continuous.
        Vector2[] uvs = new Vector2[verts.Count];
        for (int i = 0; i < verts.Count; i++)
            uvs[i] = new Vector2(verts[i].x / UV_WORLD_SCALE, verts[i].z / UV_WORLD_SCALE);
        mesh.uv = uvs;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}