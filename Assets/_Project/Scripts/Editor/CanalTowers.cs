using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.EditorTools
{
    /// <summary>
    /// Far towers that read as glass in the noon sun: the windows of the placeholder towers reflect the sky
    /// (CanalEntrance → Отражение неба) and a light haze makes them paler and bluer with distance (CanalEntrance → Outside Fog).
    /// Menu: CarX → Canal → Небоскрёбы: стекло и дымка.
    /// Your own tower models: give their glass Smoothness 0.85–0.9 and Metallic 0.3–0.5 and it reflects the sky as well.
    /// </summary>
    public static partial class CanalEntranceBuilder
    {
        [MenuItem("CarX/Canal/Небоскрёбы: стекло и дымка")]
        static void GlassTowersMenu() { Debug.Log("CarX небоскрёбы: " + GlassTowers()); }

        public static string GlassTowers()
        {
            int mats = 0, renderers = 0;
            mats += Glass("City_Facade_Carnival", 0.86f, 0.35f);
            mats += Glass("City_Facade_Gold", 0.86f, 0.55f);
            mats += Glass("City_Facade_White", 0.8f, 0.25f);
            mats += Glass("City_Facade_Dark", 0.88f, 0.4f);
            mats += Glass("City_NeedleGlass", 0.9f, 0.5f);
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                bool city = false;
                foreach (var m in mr.sharedMaterials) if (m != null && m.name.StartsWith("City_")) { city = true; break; }
                if (!city || mr.reflectionProbeUsage == ReflectionProbeUsage.BlendProbes) continue;
                Undo.RecordObject(mr, "Glass towers");
                mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                renderers++;
            }
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c != null)
            {
                Undo.RecordObject(c, "Glass towers");
                c.skyReflections = true;
                if (c.outsideFogDensity <= 0f) { c.outsideFogDensity = 0.001f; c.outsideFogColor = new Color(15f, 16.5f, 19f); }
                EditorUtility.SetDirty(c);
                EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
            }
            AssetDatabase.SaveAssets();
            return "materials " + mats + ", towers " + renderers + ", haze " + (c != null ? c.outsideFogDensity.ToString("0.0000") : "no canal");
        }

        static int Glass(string name, float smooth, float metal)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + name + ".mat");
            if (m == null) return 0;
            Undo.RecordObject(m, "Glass towers");
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            return 1;
        }
    }
}
