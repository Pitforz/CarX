using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>
    /// Trees from the pack, lit like real leaves: the pack's normal_specular and translucency_gloss maps go into the CarX/WindFoliage
    /// materials (glints, relief, sunlight shining through the leaves) and the Tree Creator wind data in the meshes makes branches
    /// bob and leaves flutter on their own. Menu: CarX → Canal → Листва: блики, просвет, листья на ветру.
    /// </summary>
    public static partial class CanalEntranceBuilder
    {
        [MenuItem("CarX/Canal/Листва: блики, просвет, листья на ветру")]
        static void FoliageLightMenu() { Debug.Log("CarX листва: " + FoliageLight()); }

        public static string FoliageLight()
        {
            var sh = Shader.Find("CarX/WindFoliage");
            if (sh == null) return "no shader CarX/WindFoliage";
            if (!AssetDatabase.IsValidFolder(PackTreeDir)) return "no pack trees in " + PackTreeDir;
            // the pack keeps one atlas per tree (diffuse, normal_specular, translucency_gloss); several prefabs share one atlas,
            // so the maps are looked up next to the colour map of each material, not by the prefab's name
            var byName = new System.Collections.Generic.Dictionary<string, string>();
            if (AssetDatabase.IsValidFolder(PackDir))
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PackDir }))
                {
                    string src = AssetDatabase.GUIDToAssetPath(guid);
                    string name = src.Substring(PackDir.Length + 1).Replace(".prefab", "").Replace("/", "_").Replace(" ", "");
                    byName[name] = Path.GetDirectoryName(src).Replace('\\', '/') + "/" + Path.GetFileNameWithoutExtension(src) + "_Textures/";
                }
            int leaves = 0, bark = 0, missing = 0, noMaps = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { PackTreeDir }))
            {
                string mp = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(mp);
                bool leaf = file.EndsWith("_Leaves");
                if (!leaf && !file.EndsWith("_Bark")) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (m == null) continue;
                if (m.shader != sh) { missing++; continue; }   // run «Ветер: применить к деревьям и траве» first
                string texDir = null;
                var albedo = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                if (albedo != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(albedo)))
                    texDir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(albedo)).Replace('\\', '/') + "/";
                else byName.TryGetValue(file.Substring(0, file.LastIndexOf('_')), out texDir);
                var bump = texDir != null ? AssetDatabase.LoadAssetAtPath<Texture>(texDir + "normal_specular.png") : null;
                var trans = texDir != null ? AssetDatabase.LoadAssetAtPath<Texture>(texDir + "translucency_gloss.png") : null;
                if (bump == null) noMaps++;
                m.SetTexture("_BumpSpecMap", bump);
                m.SetTexture("_TranslucencyMap", trans);
                m.SetFloat("_UsePackMaps", bump != null ? 1f : 0f);
                m.SetFloat("_TreeCreatorData", 1f);
                // bark: a stronger relief, so the furrows of the trunk read in the sun
                m.SetFloat("_NormalStrength", leaf ? 1f : 1.6f);
                m.SetFloat("_Translucency", leaf ? 1.2f : 0f);
                m.SetFloat("_Smoothness", leaf ? 0.45f : 0.15f);
                m.SetFloat("_Occlusion", 0.5f);
                EditorUtility.SetDirty(m);
                if (leaf) leaves++; else bark++;
            }
            AssetDatabase.SaveAssets();
            return "листва " + leaves + ", кора " + bark + (noMaps > 0 ? ", без карт " + noMaps : "") + (missing > 0 ? ", без шейдера ветра " + missing : "");
        }
    }
}
