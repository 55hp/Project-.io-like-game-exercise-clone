using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace hp55games.Editor.Tools
{
    // ─────────────────────────────────────────────────────────────────────────
    //  PathCreator
    //  EditorWindow per generare waypoint (con WaypointLabel) come figli di un
    //  parent GO, seguendo pattern circolari, zigzag o fiore.
    // ─────────────────────────────────────────────────────────────────────────
    public class PathCreatorWindow : EditorWindow
    {
        // ── Parametri ────────────────────────────────────────────────────────
        private GameObject parentGO;
        private Vector3    sp      = Vector3.zero;   // Starting Point (centro)
        private float      ri      = 0f;             // Raggio Iniziale
        private float      of_     = 0f;             // Offset Raggio Finale (stop)
        private float      prcS    = 0f;             // Progressione raggio/s
        private float      dp      = 0f;             // Distanza tra punti
        private float      df      = 0f;             // Distanza Forme (ampiezza pattern)
        private Color      labelCol = Color.white;   // Label Color

        // ── Shape selection ──────────────────────────────────────────────────
        private enum Shape { Circle, Zigzag, Flower }
        private int selectedShape = 0;

        private static readonly string[] ShapeLabels =
        {
            "Cerchio",
            "Cerchio Zigzag",
            "Cerchio Fiore",
        };

        // ── UI state ─────────────────────────────────────────────────────────
        private bool   clearBeforeGenerate;
        private Vector2 scroll;

        // Stili lazy-init
        private GUIStyle _headerStyle;
        private GUIStyle _btnActiveStyle;
        private GUIStyle _btnNormalStyle;

        private const int   MAX_PTS    = 8000;
        private const float PETAL_FREQ = 6f;    // petali del pattern fiore

        // ── Menu ─────────────────────────────────────────────────────────────
        [MenuItem("hp55games Tools/Path Creator")]
        public static void Open() =>
            GetWindow<PathCreatorWindow>(false, "Path Creator", true);

        // ── GUI ──────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            InitStyles();
            scroll = EditorGUILayout.BeginScrollView(scroll);

            // ── PARENT ───────────────────────────────────────────────────────
            SectionHeader("PARENT");
            parentGO = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Parent GO", "Transform padre dei waypoint generati"),
                parentGO, typeof(GameObject), true);

            EditorGUILayout.Space(10);

            // ── PARAMETRI ────────────────────────────────────────────────────
            SectionHeader("PARAMETRI");

            sp   = EditorGUILayout.Vector3Field(
                new GUIContent("SP  (Starting Point)", "Centro del path / punto di partenza"), sp);

            EditorGUILayout.Space(2);

            ri   = EditorGUILayout.FloatField(
                new GUIContent("RI  (Raggio Iniziale)", "Raggio di partenza della spirale/cerchio"), ri);
            of_  = EditorGUILayout.FloatField(
                new GUIContent("OF  (Offset Raggio Finale)",
                    "La generazione si ferma quando il raggio raggiunge questo valore. " +
                    "Se > 0 il path non arriva al centro."), of_);
            prcS = EditorGUILayout.FloatField(
                new GUIContent("PRC/S  (Progressione Raggio/s)",
                    "Riduzione del raggio per unità di arco percorsa. " +
                    "0 = cerchio fisso (un solo giro)."), prcS);
            dp   = EditorGUILayout.FloatField(
                new GUIContent("DP  (Distanza tra Punti)",
                    "Distanza in unità mondo tra waypoint consecutivi."), dp);
            df   = EditorGUILayout.FloatField(
                new GUIContent("DF  (Distanza Forme)",
                    "Ampiezza del pattern: escursione radiale dello zigzag/fiore."), df);

            EditorGUILayout.Space(2);

            labelCol = EditorGUILayout.ColorField(
                new GUIContent("C  (Color)", "Colore usato come LabelColor nei WaypointLabel generati"),
                labelCol);

            EditorGUILayout.Space(10);

            // ── FORMA ────────────────────────────────────────────────────────
            SectionHeader("FORMA");
            DrawShapeGrid();

            EditorGUILayout.Space(6);
            clearBeforeGenerate = EditorGUILayout.ToggleLeft(
                "Svuota parent prima di generare", clearBeforeGenerate);

            EditorGUILayout.Space(14);

            // ── GENERA ───────────────────────────────────────────────────────
            bool canGenerate = parentGO != null && dp > 0f && ri > 0f;
            EditorGUI.BeginDisabledGroup(!canGenerate);
            if (GUILayout.Button("  Genera", GUILayout.Height(42)))
                Generate();
            EditorGUI.EndDisabledGroup();

            if (!canGenerate)
            {
                if (parentGO == null)
                    EditorGUILayout.HelpBox("Assegna un Parent GO per abilitare la generazione.", MessageType.Warning);
                else if (dp <= 0f)
                    EditorGUILayout.HelpBox("DP deve essere > 0.", MessageType.Warning);
                else if (ri <= 0f)
                    EditorGUILayout.HelpBox("RI deve essere > 0.", MessageType.Warning);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        // ── Shape grid ───────────────────────────────────────────────────────
        private void DrawShapeGrid()
        {
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < ShapeLabels.Length; i++)
            {
                bool isActive = selectedShape == i;
                GUIStyle style = isActive ? _btnActiveStyle : _btnNormalStyle;
                if (GUILayout.Button(ShapeLabels[i], style, GUILayout.Height(32)))
                    selectedShape = i;
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── Section header helpers ────────────────────────────────────────────
        private void SectionHeader(string title)
        {
            EditorGUILayout.LabelField(title, _headerStyle);
            var r = GUILayoutUtility.GetLastRect();
            r.y += r.height; r.height = 1;
            EditorGUI.DrawRect(r, new Color(0.55f, 0.55f, 0.55f, 0.6f));
            GUILayout.Space(5);
        }

        // ── Lazy style init ───────────────────────────────────────────────────
        private void InitStyles()
        {
            if (_headerStyle != null) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };

            _btnNormalStyle = new GUIStyle(GUI.skin.button);

            _btnActiveStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
            };
            _btnActiveStyle.normal.textColor    = Color.white;
            _btnActiveStyle.hover.textColor     = Color.white;
            _btnActiveStyle.active.textColor    = Color.white;
            // Background tint via a colored texture
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(0.2f, 0.5f, 0.9f, 1f));
            tex.Apply();
            _btnActiveStyle.normal.background   = tex;
            _btnActiveStyle.hover.background    = tex;
            _btnActiveStyle.active.background   = tex;
        }

        // ════════════════════════════════════════════════════════════════════
        //  GENERAZIONE
        // ════════════════════════════════════════════════════════════════════
        private void Generate()
        {
            List<Vector3> pts = (Shape)selectedShape switch
            {
                Shape.Circle => GenCircle(),
                Shape.Zigzag => GenZigzag(),
                Shape.Flower => GenFlower(),
                _            => new List<Vector3>()
            };

            if (pts.Count == 0)
            {
                EditorUtility.DisplayDialog("Path Creator",
                    "Nessun punto generato. Controlla i parametri (RI, DP, PRC/S, OF).", "OK");
                return;
            }

            Undo.SetCurrentGroupName("PathCreator: Genera Waypoints");
            int undoGroup = Undo.GetCurrentGroup();

            // Svuota figli esistenti se richiesto
            if (clearBeforeGenerate)
            {
                var children = new List<GameObject>();
                foreach (Transform child in parentGO.transform)
                    children.Add(child.gameObject);
                foreach (var go in children)
                    Undo.DestroyObjectImmediate(go);
            }

            int startIdx = parentGO.transform.childCount;

            for (int i = 0; i < pts.Count; i++)
            {
                var go = new GameObject($"WP_{startIdx + i}");
                Undo.RegisterCreatedObjectUndo(go, "Create WP");

                go.transform.SetParent(parentGO.transform, false);
                go.transform.position = pts[i];

                // Aggiungo WaypointLabel e setto i campi serializzati
                var lbl = go.AddComponent<WaypointLabel>();
                lbl.labelColor = labelCol;

                var so = new SerializedObject(lbl);
                so.FindProperty("verticalOffset").floatValue = 0.4f;
                so.FindProperty("fontSize").intValue         = 11;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(parentGO);

            Debug.Log($"[PathCreator] {pts.Count} waypoint generati in '{parentGO.name}'.");
            EditorUtility.DisplayDialog("Path Creator", $"{pts.Count} waypoint generati.", "OK");
        }

        // ════════════════════════════════════════════════════════════════════
        //  ALGORITMI DI GENERAZIONE
        //
        //  Per tutti e tre:
        //    • radius = RI (raggio corrente, decresce di prcS * dp per step)
        //    • angle  = angolo corrente (in radianti)
        //    • dAngle = dp / max(radius, ε)  → arc-step corretto
        //    • stop   SE prcS>0 && radius <= OF
        //             SE prcS=0 && angle   >= 2π  (un giro completo)
        // ════════════════════════════════════════════════════════════════════

        private List<Vector3> GenCircle()
        {
            var   pts    = new List<Vector3>();
            float radius = ri;
            float angle  = 0f;
            float stopR  = Mathf.Max(0f, of_);
            bool  spiral = prcS > 0f;

            while (pts.Count < MAX_PTS)
            {
                if (spiral && radius <= stopR) break;

                pts.Add(Pt(angle, radius));

                float dA = dp / Mathf.Max(radius, 0.001f);
                angle += dA;
                if (spiral)     radius -= prcS * dp;
                else if (angle >= Mathf.PI * 2f) break;
            }
            return pts;
        }

        // Zigzag: punti alternati tra (radius + DF) e (radius - DF)
        private List<Vector3> GenZigzag()
        {
            var   pts    = new List<Vector3>();
            float radius = ri;
            float angle  = 0f;
            float stopR  = Mathf.Max(0f, of_);
            bool  spiral = prcS > 0f;
            int   idx    = 0;

            while (pts.Count < MAX_PTS)
            {
                if (spiral && radius <= stopR) break;

                float r = Mathf.Max(radius + (idx % 2 == 0 ? df : -df), 0f);
                pts.Add(Pt(angle, r));

                float dA = dp / Mathf.Max(radius, 0.001f);
                angle += dA;
                if (spiral)     radius -= prcS * dp;
                else if (angle >= Mathf.PI * 2f) break;
                idx++;
            }
            return pts;
        }

        // Fiore: modulazione sinusoidale del raggio — r = radius + DF * sin(PETAL_FREQ * angle)
        private List<Vector3> GenFlower()
        {
            var   pts    = new List<Vector3>();
            float radius = ri;
            float angle  = 0f;
            float stopR  = Mathf.Max(0f, of_);
            bool  spiral = prcS > 0f;

            while (pts.Count < MAX_PTS)
            {
                if (spiral && radius <= stopR) break;

                float r = Mathf.Max(radius + df * Mathf.Sin(PETAL_FREQ * angle), 0f);
                pts.Add(Pt(angle, r));

                float dA = dp / Mathf.Max(radius, 0.001f);
                angle += dA;
                if (spiral)     radius -= prcS * dp;
                else if (angle >= Mathf.PI * 2f) break;
            }
            return pts;
        }

        // Helper: posizione 3D dal centro SP a raggio/angolo dati
        private Vector3 Pt(float angle, float radius) =>
            new Vector3(
                sp.x + Mathf.Cos(angle) * radius,
                sp.y,
                sp.z + Mathf.Sin(angle) * radius);
    }
}
