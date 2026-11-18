using System.Collections.Generic;
using System.IO;
using System.Text;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarX.EditorTools
{
    /// <summary>
    /// Opens every scene of the project that has a tunnel graph, rebuilds the graph by the current types and look,
    /// saves it and reports the problems. Use after pulling new tunnel rules: nobody has to redo a graph by hand.
    /// </summary>
    public static class TunnelRebuildAll
    {
        [MenuItem("CarX/Tunnels/Rebuild All Graphs in Project", priority = 42)]
        static void Menu()
        {
            var scenes = ScenesWithGraphs();
            if (scenes.Count == 0) { EditorUtility.DisplayDialog("CarX", "В проекте нет сцен с графом тоннелей.", "OK"); return; }
            if (!EditorUtility.DisplayDialog("Пересобрать все графы",
                    "Сцены с графом тоннелей:\n\n" + string.Join("\n", scenes) + "\n\nКаждая откроется, граф пересоберётся по текущим типам и сцена сохранится. Сцены с несохранёнными правками будут пропущены.",
                    "Пересобрать и сохранить", "Отмена")) return;
            var report = RebuildAll(true);
            Debug.Log(report);
            EditorUtility.DisplayDialog("CarX", report, "OK");
        }

        public static List<string> ScenesWithGraphs()
        {
            var res = new List<string>();
            string guid = ScriptGuid();
            foreach (var g in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (path.StartsWith("Assets/_Recovery")) continue;
                try { if (guid != null && File.ReadAllText(path).Contains(guid)) res.Add(path); }
                catch { }
            }
            return res;
        }

        static string ScriptGuid()
        {
            foreach (var g in AssetDatabase.FindAssets("TunnelNetwork t:MonoScript"))
            {
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(g));
                if (ms != null && ms.GetClass() == typeof(TunnelNetwork)) return g;
            }
            return null;
        }

        public static string RebuildAll(bool save)
        {
            var sb = new StringBuilder("CarX rebuild all graphs:");
            var prevActive = SceneManager.GetActiveScene();
            foreach (var path in ScenesWithGraphs())
            {
                var scene = SceneManager.GetSceneByPath(path);
                bool wasOpen = scene.IsValid() && scene.isLoaded;
                if (wasOpen && scene.isDirty && save) { sb.Append("\n  " + path + ": пропущена (есть несохранённые правки)"); continue; }
                if (!wasOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                int nets = 0, tunnels = 0, issues = 0;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var net in root.GetComponentsInChildren<TunnelNetwork>(true))
                    {
                        nets++;
                        net.ForceRebuild();
                        net.EnsureLayout();
                        net.Build();
                        tunnels += net.chains.Count;
                        issues += net.issues.Count;
                    }
                if (save) EditorSceneManager.SaveScene(scene);
                sb.Append("\n  " + path + ": графов " + nets + ", тоннелей " + tunnels + ", проблем " + issues + (save ? ", сохранена" : ""));
                if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            }
            if (prevActive.IsValid() && prevActive.isLoaded) SceneManager.SetActiveScene(prevActive);
            return sb.ToString();
        }
    }
}
