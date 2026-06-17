using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class WaypointMover : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    [Header("Path")]
    public List<Transform> waypoints;
    [Range(0.1f, 20f)] public float speed         = 4f;
    [Range(0f,  1f)]   public float curveTension  = 0.5f;
    public bool loop = false;

    [Header("Zone Painting")]
    public bool paintZone = true;
    [SerializeField] private float footprintRadius  = 0.4f;   // metà della larghezza XZ del mesh
    [SerializeField] [Range(0f, 1f)] private float outsideThreshold = 0.95f;

    [Header("Playback")]
    [SerializeField] private bool autoStart = true;

    [Header("Visuals")]
    [SerializeField] private Transform meshTransform;
    [SerializeField] [Range(1f, 2000f)] private float rotationSpeed = 720f;

    private Character character;
    private bool      isMoving      = false;
    private int       currentIndex  = 0;
    private float     progress      = 0f;
    private Vector3   previousPosition;

    // -------------------------------------------------------------------------
    // Property
    // -------------------------------------------------------------------------

    public bool IsMoving => isMoving;

    // -------------------------------------------------------------------------
    // Unity messages
    // -------------------------------------------------------------------------

    private void Awake()
    {
        character = GetComponent<Character>();

        // Snap prima che qualsiasi Start() giri, così InitializeZone() vede la posizione corretta.
        if (autoStart && waypoints != null && waypoints.Count >= 2)
            transform.position = waypoints[0].position;
    }

    private void Start()
    {
        transform.position = waypoints[0].position;
        character.InitializeZone();
        previousPosition = transform.position;
        if (autoStart) StartMoving();
    }

    private void Update()
    {
        if (!isMoving || waypoints == null || waypoints.Count < 2) return;

        int count = waypoints.Count;

        // Safety: already past the last segment
        if (currentIndex >= count - 1)
        {
            HandleEnd();
            return;
        }

        // --- Catmull-Rom control points for the current segment ---------------
        Vector3 p0 = waypoints[Mathf.Max(currentIndex - 1, 0)].position;
        Vector3 p1 = waypoints[currentIndex].position;
        Vector3 p2 = waypoints[currentIndex + 1].position;
        Vector3 p3 = waypoints[Mathf.Min(currentIndex + 2, count - 1)].position;

        float segmentLength = Vector3.Distance(p1, p2);
        if (segmentLength < 0.0001f) segmentLength = 0.0001f;

        progress += (speed * Time.deltaTime) / segmentLength;

        // --- Crossed a waypoint -----------------------------------------------
        if (progress >= 1f)
        {
            progress      = 0f;
            currentIndex += 1;

            if (currentIndex >= count - 1)
            {
                HandleEnd();
                return;
            }
        }

        // --- Evaluate spline position for current (index, progress) -----------
        Vector3 q0 = waypoints[Mathf.Max(currentIndex - 1, 0)].position;
        Vector3 q1 = waypoints[currentIndex].position;
        Vector3 q2 = waypoints[currentIndex + 1].position;
        Vector3 q3 = waypoints[Mathf.Min(currentIndex + 2, count - 1)].position;

        transform.position = EvalCatmullRom(q0, q1, q2, q3, progress);

        // --- Zone painting – valutato ogni frame dopo l'aggiornamento posizione --
        if (paintZone)
        {
            if (character.IsLargelyOutsideZone(transform.position, footprintRadius, outsideThreshold))
            {
                // 95%+ del footprint è fuori dalla zona → traccia la trail
                character.AddTrailPoint(transform.position);
            }
            else if (character.IsDrawingTrail && character.IsInsideZone(transform.position))
            {
                // Il centro del Character è chiaramente dentro la zona con trail attiva → chiudi l'area.
                // Usare IsInsideZone (singolo punto) invece di !IsLargelyOutsideZone aggiunge isteresi:
                // la trail inizia con 95%+ fuori, si chiude solo quando il centro è dentro.
                // Questo evita l'oscillazione rapida al bordo che corrompeva il poligono.
                character.ReturnToZone();
            }
        }

        // --- Rotazione mesh verso la direzione di movimento -------------------
        Vector3 moveDir = transform.position - previousPosition;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot  = Quaternion.LookRotation(moveDir.normalized);
            Transform  rotTarget  = meshTransform != null ? meshTransform : transform;
            rotTarget.rotation    = Quaternion.RotateTowards(rotTarget.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
        previousPosition = transform.position;
    }

    // -------------------------------------------------------------------------
    // Public API  –  callable from Timeline Signal Receiver
    // -------------------------------------------------------------------------

    public void StartMoving()
    {
        if (waypoints == null || waypoints.Count < 2)
        {
            Debug.LogWarning("[WaypointMover] Need at least 2 waypoints to start.", this);
            return;
        }

        currentIndex = 0;
        progress     = 0f;
        transform.position = waypoints[0].position;   // evita lo snap al primo frame
        isMoving     = true;
    }

    public void StopMoving()
    {
        isMoving = false;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called when the mover reaches or passes the last waypoint.
    /// Loops if requested, otherwise stops and triggers zone deformation.
    /// </summary>
    private void HandleEnd()
    {
        if (loop)
        {
            if (paintZone && character.IsDrawingTrail)
                character.ReturnToZone();

            currentIndex = 0;
            progress     = 0f;
        }
        else
        {
            // Snap to final waypoint so position is exact
            transform.position = waypoints[waypoints.Count - 1].position;

            StopMoving();

            if (paintZone)
                character.ReturnToZone();
        }
    }

    /// <summary>
    /// Hermite-form Catmull-Rom spline with adjustable tension.
    /// curveTension = 0.5 reproduces the standard Catmull-Rom curve.
    /// </summary>
    private Vector3 EvalCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        Vector3 tangent1 = curveTension * (p2 - p0);
        Vector3 tangent2 = curveTension * (p3 - p1);

        float t2 = t  * t;
        float t3 = t2 * t;

        // Cubic Hermite basis functions
        float h1 =  2f * t3 - 3f * t2 + 1f;
        float h2 =       t3 - 2f * t2 + t;
        float h3 = -2f * t3 + 3f * t2;
        float h4 =       t3 -      t2;

        return h1 * p1 + h2 * tangent1 + h3 * p2 + h4 * tangent2;
    }
}
