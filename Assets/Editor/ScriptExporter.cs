using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace hp55games.Editor.Tools
{
    // ─────────────────────────────────────────────────────────────────────────
    //  ScriptExporter
    //  Scans all .cs files under Assets/ and writes their content into a single
    //  .txt file placed at the project root (next to the Assets folder).
    //  Each script is separated by 3 blank lines and preceded by its path.
    // ─────────────────────────────────────────────────────────────────────────
    public class ScriptExporter : EditorWindow
    {
        private const string MenuPath = "hp55games Tools/Export All Scripts to TXT";

        [MenuItem(MenuPath)]
        private static void Execute()
        {
            // Project root = one level above Application.dataPath (which is .../Assets)
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string timestamp   = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string outputPath  = Path.Combine(projectRoot, $"AllScripts_Export_{timestamp}.txt");

            string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });

            if (scriptGuids.Length == 0)
            {
                EditorUtility.DisplayDialog("Script Exporter", "Nessun script trovato in Assets/.", "OK");
                return;
            }

            // Filter out scripts inside any "Plugins" folder
            var filteredGuids = System.Array.FindAll(scriptGuids, guid =>
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                return !p.Replace('\\', '/').Contains("/Plugins/");
            });

            if (filteredGuids.Length == 0)
            {
                EditorUtility.DisplayDialog("Script Exporter", "Nessun script trovato al di fuori della cartella Plugins.", "OK");
                return;
            }

            var sb = new StringBuilder();

            sb.AppendLine($"// Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"// Total scripts: {filteredGuids.Length} (Plugins esclusi)");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();

            for (int i = 0; i < filteredGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(filteredGuids[i]);
                string fullPath  = Path.GetFullPath(assetPath);

                EditorUtility.DisplayProgressBar(
                    "Script Exporter",
                    $"Processando {assetPath}",
                    (float)i / filteredGuids.Length);

                if (!File.Exists(fullPath))
                    continue;

                string content = File.ReadAllText(fullPath);

                // Header with relative asset path
                sb.AppendLine($"// ===== {assetPath} =====");
                sb.AppendLine();
                sb.AppendLine(content);

                // 3 blank lines separator (skip after last file)
                if (i < filteredGuids.Length - 1)
                {
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.AppendLine();
                }
            }

            EditorUtility.ClearProgressBar();

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);

            Debug.Log($"[ScriptExporter] {filteredGuids.Length} script esportati in: {outputPath}");

            EditorUtility.DisplayDialog(
                "Script Exporter",
                $"{filteredGuids.Length} script esportati in:\n{outputPath}",
                "OK");
        }
    }
}
