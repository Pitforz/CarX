using System.Collections.Generic;
using System.IO;
using System.Text;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.EditorTools
{
    /// <summary>Trash colliders and wind sway for the props the user placed around the canal.</summary>
    public static partial class CanalEntranceBuilder
    {
        /// <summary>Writes Logs/props.txt: every prefab instance root in the scene with its source, position, size, shaders.</summary>
        public static string ListProps()
        {
            var sb = new StringBuilder();
            var seen = new HashSet<GameObject>();
            int n = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
                if (root == null || !seen.Add(root)) continue;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (!path.Contains("WasteOvergrowth") && !path.Contains("Tree") && !path.Contains("Canal")) continue;
                var b = new Bounds(root.transform.position, Vector3.zero);
                var shaders = new HashSet<string>();
                int cols = root.GetComponentsInChildren<Collider>(true).Length;
                foreach (var rr in root.GetComponentsInChildren<Renderer>())
                {
                    b.Encapsulate(rr.bounds);
                    foreach (var m in rr.sharedMaterials) if (m != null && m.shader != null) shaders.Add(m.shader.name);
                }
                string parent = root.transform.parent != null ? root.transform.parent.name : "-";
                sb.AppendLine(string.Format("{0} | {1} | parent {2} | pos {3} | size {4} | colliders {5} | {6}",
                    root.name, path, parent, root.transform.position.ToString("F1"), b.size.ToString("F1"), cols, string.Join(",", shaders)));
                n++;
            }
            File.WriteAllText("Logs/props.txt", sb.ToString());
            return n + " prefab roots -> Logs/props.txt";
        }
        // ------------------------------------------------------------------ trash: invisible round walls

        const string TrashWallsName = "Мусор: невидимые стены (авто)";
        static readonly string[] TrashSkip = { "TrashGroup_7", "TrashGroup_8", "TrashGroup_9", "TrashGroup_10", "TrashGroup_11", "Flies", "Stain", "Pile1", "Pile2", "Pile3", "Pile" };

        
        [MenuItem("CarX/Canal/Ветер: применить к деревьям и траве")]
        static void SetupWindMenu() { Debug.Log("Ветер: " + SetupWind()); }

        /// <summary>A simple round collider around each piece of trash (overlapping ones merged), so the player keeps a little distance.
        /// Small piles TrashGroup 7-11 stay walkable. Run again after moving trash: the old walls are replaced.</summary>
        public static string TrashWallsApply() { return TrashColliders(); }
        public static string TrashColliders() { return TrashColliders(TrashWallsWindow.Margin, TrashWallsWindow.MaxMerged); }

        public static string TrashColliders(float margin, float maxMerged)
        {
            var old = GameObject.Find(TrashWallsName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            var circles = new List<Vector4>();   // x, z, radius, ground y
            var skippedBig = new List<string>();
            Transform parent = null;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
                if (root == null) continue;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (!path.Contains("WasteOvergrowth")) continue;
                string src = Path.GetFileNameWithoutExtension(path);
                bool skip = false;
                foreach (var k in TrashSkip) if (src.EndsWith(k)) skip = true;
                if (src.Contains("TrashPile")) skip = true;
                if (skip) continue;
                if (parent == null) parent = root.transform.parent;
                var b = r.bounds;
                float rad = Mathf.Max(b.extents.x, b.extents.z);
                if (rad < 0.08f && b.size.y < 0.15f) continue;   // bottle caps, flat paper
                if (rad > 3f) { skippedBig.Add(r.name); continue; }
                circles.Add(new Vector4(b.center.x, b.center.z, rad + margin, b.min.y));
            }
            // merge: a circle inside a bigger one disappears, strongly overlapping ones become one (if not too big)
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < circles.Count && !changed; i++)
                    for (int j = i + 1; j < circles.Count && !changed; j++)
                    {
                        Vector4 a = circles[i], b = circles[j];
                        var ab = new Vector2(b.x - a.x, b.y - a.y);
                        float d = ab.magnitude;
                        float gy = Mathf.Min(a.w, b.w);
                        if (d + b.z <= a.z + 0.15f) { a.w = gy; circles[i] = a; circles.RemoveAt(j); changed = true; }
                        else if (d + a.z <= b.z + 0.15f) { b.w = gy; circles[j] = b; circles.RemoveAt(i); changed = true; }
                        else if (d < 0.6f * (a.z + b.z))
                        {
                            float R = (d + a.z + b.z) * 0.5f;
                            if (R > maxMerged) continue;
                            var cxy = new Vector2(a.x, a.y) + ab / Mathf.Max(d, 1e-4f) * (R - a.z);
                            circles[i] = new Vector4(cxy.x, cxy.y, R, gy);
                            circles.RemoveAt(j);
                            changed = true;
                        }
                    }
            }
            var walls = new GameObject(TrashWallsName);
            Undo.RegisterCreatedObjectUndo(walls, "Мусор: стены");
            if (parent != null) walls.transform.SetParent(parent, true);
            int n = 0;
            foreach (var c in circles)
            {
                var go = new GameObject("Стена " + (n++));
                go.transform.SetParent(walls.transform, false);
                go.transform.position = new Vector3(c.x, c.w - 0.05f, c.y);
                var col = go.AddComponent<CapsuleCollider>();
                col.radius = c.z;
                col.height = 2f * c.z + 2f;
                col.center = new Vector3(0f, col.height * 0.5f - c.z, 0f);   // the round bottom is under the ground, a straight wall above it
            }
            EditorSceneManager.MarkSceneDirty(walls.scene);
            EditorSceneManager.SaveScene(walls.scene);
            return n + " walls" + (skippedBig.Count > 0 ? ", skipped too big: " + string.Join(",", skippedBig) : "");
        }

        // ------------------------------------------------------------------ wind

        /// <summary>Pack trees, bushes and the grass tufts get the CarX/WindFoliage shader (shared gusts, own swaying, calm in the channel).</summary>
        public static string SetupWind()
        {
            var sh = Shader.Find("CarX/WindFoliage");
            if (sh == null) return "no shader CarX/WindFoliage";
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            float calmY = c != null ? c.transform.TransformPoint(new Vector3(0f, c.groundHeight - 1.2f, 0f)).y : -1000f;
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PackTreeDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                float worldH = PrefabHeight(prefab), meshH = 0.5f;
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) meshH = Mathf.Max(meshH, mf.sharedMesh.bounds.max.y);
                bool bush = name.StartsWith("1_") || name.StartsWith("2_");
                float strength = worldH * (bush ? 0.06f : name.StartsWith("Palm") ? 0.03f : 0.025f);
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || !AssetDatabase.GetAssetPath(m).StartsWith(PackTreeDir)) continue;
                        ToWind(m, sh, 0f, meshH, strength, m.name.EndsWith("_Leaves") ? 1f : 0f, calmY);
                        n++;
                    }
            }
            if (c != null && c.grassTuft != null) { ToWind(c.grassTuft, sh, 1f, 1f, 0.07f, 0f, calmY); n++; }
            // moving vertices need the real object position: no static batching for the trees
            int unstatic = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
                if (root == null || !PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root).StartsWith(PackTreeDir)) continue;
                var f = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                if ((f & StaticEditorFlags.BatchingStatic) != 0)
                {
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, f & ~StaticEditorFlags.BatchingStatic);
                    unstatic++;
                }
            }
            AssetDatabase.SaveAssets();
            if (c != null && c.GetComponent<WindControl>() == null) Undo.AddComponent<WindControl>(c.gameObject);
            if (c != null) EditorSceneManager.SaveScene(c.gameObject.scene);
            return n + " materials on wind, calm below y " + calmY.ToString("F1") + ", unstatic " + unstatic;
        }

        static void ToWind(Material m, Shader sh, float mode, float height, float strength, float flutter, float calmY)
        {
            var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            var col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
            float cut = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.45f;
            float clip = m.IsKeywordEnabled("_ALPHATEST_ON") || (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f) ? 1f : 0f;
            float cull = m.HasProperty("_Cull") ? m.GetFloat("_Cull") : 2f;
            float smooth = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0.2f;
            Undo.RecordObject(m, "Ветер");
            m.shader = sh;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", col);
            m.SetFloat("_Cutoff", cut);
            m.SetFloat("_AlphaClip", clip);
            m.SetFloat("_Cull", cull);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_WindMode", mode);
            m.SetFloat("_TreeHeight", height);
            m.SetFloat("_Strength", strength);
            m.SetFloat("_Flutter", flutter);
            m.SetFloat("_CalmBelowY", calmY);
            m.renderQueue = clip > 0.5f ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            m.SetOverrideTag("RenderType", clip > 0.5f ? "TransparentCutout" : "Opaque");
            EditorUtility.SetDirty(m);
        }
    }
}

namespace CarX.EditorTools
{
    /// <summary>Окно: невидимые круглые стены вокруг мусора с настраиваемым радиусом.</summary>
    public class TrashWallsWindow : EditorWindow
    {
        public static float Margin { get { return EditorPrefs.GetFloat("CarX.TrashMargin", 0.35f); } set { EditorPrefs.SetFloat("CarX.TrashMargin", value); } }
        public static float MaxMerged { get { return EditorPrefs.GetFloat("CarX.TrashMaxMerged", 2.6f); } set { EditorPrefs.SetFloat("CarX.TrashMaxMerged", value); } }
        string _result = "";

        [MenuItem("CarX/Canal/Мусор: невидимые стены...")]
        static void Open() { GetWindow<TrashWallsWindow>("Стены вокруг мусора"); }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Круглая невидимая стена вокруг каждого мусорного объекта и Trash Group. Не трогает Trash Group 7-11 и кучи (Pile). Пересекающиеся стены объединяются. Повторное применение заменяет старые стены.", MessageType.Info);
            Margin = EditorGUILayout.Slider("Отступ от мусора, м", Margin, 0f, 2f);
            MaxMerged = EditorGUILayout.Slider("Макс. радиус объединённой стены, м", MaxMerged, 0.5f, 6f);
            if (GUILayout.Button("Применить ко всему мусору в открытой сцене", GUILayout.Height(32)))
                _result = CanalEntranceBuilder.TrashColliders(Margin, MaxMerged);
            if (_result.Length > 0) EditorGUILayout.LabelField("Готово: " + _result);
        }
    }
}
