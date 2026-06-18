using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Mostra il nome del GameObject come testo colorato in Scene View,
/// appena sopra il waypoint. Solo editor, nessun costo a runtime.
/// </summary>
public class WaypointLabel : MonoBehaviour
{
    [Header("Label")]
    public Color labelColor = Color.yellow;
    [SerializeField] private float verticalOffset = 0.4f;
    [SerializeField] private int   fontSize       = 11;

    [Header("Speed Override")]
    [Tooltip("Se attivo, quando WaypointMover raggiunge questo waypoint aggiorna il proprio speed.")]
    public bool  setSpeed = false;
    [Tooltip("Valore di speed applicato a WaypointMover se setSpeed è true.")]
    public float speed    = 0f;

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        GUIStyle style = new GUIStyle();
        style.normal.textColor = labelColor;
        style.fontSize         = fontSize;
        style.fontStyle        = FontStyle.Bold;

        Handles.Label(transform.position + Vector3.up * verticalOffset, gameObject.name, style);
    }
#endif
}
