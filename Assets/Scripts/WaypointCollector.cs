using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Componente da aggiungere al GameObject padre dei waypoints.
/// Il pulsante in Inspector raccoglie tutti i figli con WaypointLabel
/// e assegna la lista risultante al WaypointMover indicato.
/// </summary>
public class WaypointCollector : MonoBehaviour
{
    [Header("Target")]
    public WaypointMover targetMover;

    [Header("Options")]
    public bool makeItLoop = false;

    [Header("Result (read-only)")]
    [SerializeField] private List<Transform> collectedWaypoints = new List<Transform>();
}

#if UNITY_EDITOR
[CustomEditor(typeof(WaypointCollector))]
public class WaypointCollectorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(6);

        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("Collect & Assign Waypoints", GUILayout.Height(30)))
            Collect((WaypointCollector)target);
        GUI.backgroundColor = Color.white;
    }

    private static void Collect(WaypointCollector collector)
    {
        // Cerca ricorsivamente tra tutti i figli (inclusi inattivi).
        WaypointLabel[] labels = collector.GetComponentsInChildren<WaypointLabel>(includeInactive: true);

        List<Transform> found = new List<Transform>(labels.Length);
        foreach (WaypointLabel label in labels)
            found.Add(label.transform);

        if (collector.makeItLoop && found.Count > 0)
            found.Add(found[0]);

        // --- Aggiorna collectedWaypoints sul collector ---
        SerializedObject collectorSO  = new SerializedObject(collector);
        SerializedProperty listProp   = collectorSO.FindProperty("collectedWaypoints");

        listProp.arraySize = found.Count;
        for (int i = 0; i < found.Count; i++)
            listProp.GetArrayElementAtIndex(i).objectReferenceValue = found[i];

        collectorSO.ApplyModifiedProperties();

        // --- Assegna la lista al WaypointMover ---
        if (collector.targetMover == null)
        {
            Debug.LogWarning("[WaypointCollector] Nessun WaypointMover assegnato.", collector);
            return;
        }

        SerializedObject moverSO       = new SerializedObject(collector.targetMover);
        SerializedProperty waypointsProp = moverSO.FindProperty("waypoints");

        waypointsProp.arraySize = found.Count;
        for (int i = 0; i < found.Count; i++)
            waypointsProp.GetArrayElementAtIndex(i).objectReferenceValue = found[i];

        moverSO.ApplyModifiedProperties();

        Debug.Log($"[WaypointCollector] {found.Count} waypoints assegnati a '{collector.targetMover.name}'.", collector.targetMover);
    }
}
#endif
