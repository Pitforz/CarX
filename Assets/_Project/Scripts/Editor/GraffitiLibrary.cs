using System.Collections.Generic;
using System.IO;
using CarX.Level;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>The graffiti folder: every picture dropped into Assets/_Project/Graffiti gets a decal material
    /// (and worn variants on demand) in Graffiti/_Generated. Replacing a picture with the same name updates every graffiti using it.</summary>
    [InitializeOnLoad]
    public static class GraffitiLibrary
    {
        public const string Root = "Assets/_Project/Graffiti";
        public const string Gen = Root + "/_Generated";
        const string DecalShader = "Shader Graphs/Decal";

        static GraffitiLibrary()
        {
            GraffitiDecal.MaterialFor = MaterialFor;
            EditorApplication.delayCall += () => { EnsureDecalLayers(); RefreshScenes(); };
        }

        /// <summary>Angle fade of decals (no paint smeared over walls at 90° to the graffiti) only works with "Decal Layers"
        /// on the URP Decal renderer feature: turns it on in every renderer of the project, once.</summary>
        public static void EnsureDecalLayers()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                foreach (var f in data.rendererFeatures)
                {
                    if (!(f is UnityEngine.Rendering.Universal.DecalRendererFeature)) continue;
                    var so = new SerializedObject(f);
                    var prop = so.FindProperty("m_Settings.decalLayers");
                    if (prop == null || prop.boolValue) continue;
                    prop.boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(f);
                    EditorUtility.SetDirty(data);
                    AssetDatabase.SaveAssetIfDirty(data);
                    Debug.Log("CarX Graffiti: включены Decal Layers в " + data.name + " (краска не тянется по перпендикулярным стенам)");
                }
            }
        }

        public static bool IsSource(string path)
        {
            if (!path.StartsWith(Root + "/") || path.StartsWith(Gen) || path.StartsWith(GraffitiSheet.SheetsDir + "/")) return false;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".psd";
        }

        /// <summary>All pictures in the folder (sub-folders included), sorted by name.</summary>
        public static List<Texture2D> Images()
        {
            var list = new List<Texture2D>();
            if (!AssetDatabase.IsValidFolder(Root)) return list;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsSource(path)) continue;
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (t != null) list.Add(t);
            }
            list.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets/_Project", "Graffiti");
            if (!AssetDatabase.IsValidFolder(Gen)) AssetDatabase.CreateFolder(Root, "_Generated");
        }

        // ------------------------------------------------------------------ materials

        static string Key(Texture2D img, int pct, out string srcPath, out string guid)
        {
            srcPath = AssetDatabase.GetAssetPath(img);
            guid = AssetDatabase.AssetPathToGUID(srcPath);
            return Gen + "/" + guid + "_w" + pct.ToString("00");
        }

        /// <summary>Decal material for a picture at a wear level (0..1, step 0.1); created on first use.</summary>
        public static Material MaterialFor(Texture2D img, float wear)
        {
            if (img == null) return null;
            int pct = Mathf.RoundToInt(GraffitiDecal.WearStep(wear) * 100f);
            string src, guid;
            string key = Key(img, pct, out src, out guid);
            if (string.IsNullOrEmpty(guid) || !IsSource(src)) return null;   // not from the graffiti folder
            var mat = AssetDatabase.LoadAssetAtPath<Material>(key + ".mat");
            if (mat != null) return mat;
            var shader = Shader.Find(DecalShader);
            if (shader == null) { Debug.LogError("CarX Graffiti: нет шейдера " + DecalShader); return null; }
            EnsureFolders();
            Texture2D tex = pct == 0 ? img : Worn(src, key + ".png", guid, pct);
            mat = new Material(shader) { name = img.name + (pct > 0 ? " w" + pct : ""), enableInstancing = true };
            Setup(mat, tex);
            AssetDatabase.CreateAsset(mat, key + ".mat");
            return mat;
        }

        static void Setup(Material mat, Texture tex)
        {
            mat.SetTexture("Base_Map", tex);
            if (mat.HasProperty("Normal_Blend")) mat.SetFloat("Normal_Blend", 0f);   // keep the bumps of the concrete under the paint
            EditorUtility.SetDirty(mat);
        }

        // ------------------------------------------------------------------ wear: eaten-away alpha + faded colour

        static Texture2D Worn(string srcPath, string outPath, string guid, int pct)
        {
            WriteWorn(srcPath, outPath, guid, pct);
            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        static void WriteWorn(string srcPath, string outPath, string guid, int pct)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            bool ok = false;
            try { ok = src.LoadImage(File.ReadAllBytes(srcPath)); } catch { }
            if (!ok)
            {
                Debug.LogWarning("CarX Graffiti: потёртость работает только для PNG/JPG: " + srcPath);
                src = new Texture2D(4, 4);
            }
            int w = src.width, h = src.height;
            var px = src.GetPixels32();
            float wear = pct / 100f;
            var rnd = new System.Random(guid.GetHashCode());
            float ox = (float)rnd.NextDouble() * 500f, oy = (float)rnd.NextDouble() * 500f;
            float big = 5f / Mathf.Max(w, h), mid = 18f / Mathf.Max(w, h), fine = 90f / Mathf.Max(w, h);
            var n = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = 0.55f * Mathf.PerlinNoise(x * big + ox, y * big + oy)
                            + 0.30f * Mathf.PerlinNoise(x * mid + oy, y * mid + ox)
                            + 0.15f * Mathf.PerlinNoise(x * fine + 31f, y * fine + 17f);
                    n[y * w + x] = v;
                }
            // threshold = the noise value below which "wear" of the area is gone (only counting painted pixels)
            var sample = new List<float>();
            for (int i = 0; i < n.Length; i += Mathf.Max(1, n.Length / 8192)) if (px[i].a > 20) sample.Add(n[i]);
            sample.Sort();
            float t = sample.Count > 0 ? sample[Mathf.Clamp((int)(sample.Count * wear * 0.9f), 0, sample.Count - 1)] : 0f;
            const float soft = 0.035f;
            float fade = 1f - 0.35f * wear, grey = 0.35f * wear;
            for (int i = 0; i < px.Length; i++)
            {
                float keep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(t - soft, t + soft, n[i]));
                if (wear <= 0f) keep = 1f;
                var c = px[i];
                float l = (c.r * 0.3f + c.g * 0.59f + c.b * 0.11f);
                px[i] = new Color32(
                    (byte)Mathf.Lerp(c.r, l, grey), (byte)Mathf.Lerp(c.g, l, grey), (byte)Mathf.Lerp(c.b, l, grey),
                    (byte)(c.a * keep * fade));
            }
            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.SetPixels32(px);
            File.WriteAllBytes(outPath, outTex.EncodeToPNG());
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(outTex);
        }

        // ------------------------------------------------------------------ keeping things in sync

        /// <summary>Re-reads the folder: materials for new pictures, worn copies of replaced pictures, removes leftovers of deleted ones,
        /// and refreshes every graffiti in the open scenes.</summary>
        public static void Sync(ICollection<string> changedSources = null)
        {
            EnsureFolders();
            var guids = new HashSet<string>();
            foreach (var img in Images())
            {
                guids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(img)));
                MaterialFor(img, 0f);
            }
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var f in Directory.GetFiles(Gen))
                {
                    string name = Path.GetFileName(f);
                    if (name.EndsWith(".meta")) continue;
                    int us = name.IndexOf("_w");
                    if (us <= 0) continue;
                    string guid = name.Substring(0, us);
                    string path = Gen + "/" + name;
                    if (!guids.Contains(guid)) { AssetDatabase.DeleteAsset(path); continue; }   // picture was deleted
                    if (changedSources != null && changedSources.Contains(guid) && name.EndsWith(".png"))
                    {
                        int pct;
                        if (int.TryParse(Path.GetFileNameWithoutExtension(name).Substring(us + 2), out pct))
                            WriteWorn(AssetDatabase.GUIDToAssetPath(guid), path, guid, pct);   // picture replaced: wear again
                    }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
            RefreshScenes();
        }

        public static void RefreshScenes()
        {
            foreach (var g in Object.FindObjectsByType<GraffitiDecal>(FindObjectsInactive.Include)) g.Refresh();
            SceneView.RepaintAll();
        }

        /// <summary>Graffiti in the open scenes whose picture was deleted.</summary>
        public static List<GraffitiDecal> Missing()
        {
            var list = new List<GraffitiDecal>();
            foreach (var g in Object.FindObjectsByType<GraffitiDecal>(FindObjectsInactive.Include)) if (g.image == null) list.Add(g);
            return list;
        }

        // ------------------------------------------------------------------ test pictures

        /// <summary>Three simple spray-paint pictures to try the tool with (prefixed "_тест", safe to delete).</summary>
        public static void CreateTestImages()
        {
            EnsureFolders();
            Write("_тест_стрелка", 512, 256, new Color32(240, 240, 235, 255), (u, v) =>
            {
                float shaft = Box(u - 0.38f, v - 0.5f, 0.3f, 0.11f);
                float head = Mathf.Max(u - 0.94f, Mathf.Abs(v - 0.5f) * 1.2f - (0.94f - u) * 0.9f + 0.0f, 0.62f - u);
                return Mathf.Min(shaft, head * 0.8f);
            });
            Write("_тест_крест", 384, 384, new Color32(235, 200, 30, 255), (u, v) =>
            {
                float a = Mathf.Abs((u - 0.5f) - (v - 0.5f)) * 0.707f - 0.06f;
                float b = Mathf.Abs((u - 0.5f) + (v - 0.5f)) * 0.707f - 0.06f;
                float r = Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f)) - 0.4f;
                return Mathf.Max(Mathf.Min(a, b), r);
            });
            Write("_тест_круг", 512, 512, new Color32(200, 30, 35, 255), (u, v) =>
            {
                float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.55f) * (v - 0.55f));
                return Mathf.Abs(d - 0.3f) - 0.045f;   // ring
            });
            AssetDatabase.Refresh();
            Sync();
        }

        static float Box(float x, float y, float hx, float hy) { return Mathf.Max(Mathf.Abs(x) - hx, Mathf.Abs(y) - hy); }

        /// <summary>Draws a signed-distance shape as spray paint: soft edge, overspray dots, a few drips.</summary>
        static void Write(string name, int w, int h, Color32 col, System.Func<float, float, float> sdf)
        {
            string path = Root + "/" + name + ".png";
            if (File.Exists(path)) return;
            var rnd = new System.Random(name.GetHashCode());
            var px = new Color32[w * h];
            float s = 1f / Mathf.Max(w, h);
            var drips = new List<Vector3>();   // x, start y, length (uv)
            for (int i = 0; i < 9; i++) drips.Add(new Vector3((float)rnd.NextDouble(), 0f, 0.05f + (float)rnd.NextDouble() * 0.15f));
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / w, v = (float)y / h;
                    float d = sdf(u, v);
                    float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.004f, 0.012f, d));
                    // overspray: sparse dots just outside the edge
                    if (d > 0f && d < 0.05f)
                    {
                        uint hsh = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                        hsh ^= hsh >> 13; hsh *= 0x5bd1e995; hsh ^= hsh >> 15;
                        if ((hsh & 1023) < (int)(60 * (1f - d / 0.05f))) a = Mathf.Max(a, 0.6f);
                    }
                    a *= 0.85f + 0.15f * Mathf.PerlinNoise(x * s * 40f, y * s * 40f);
                    px[y * w + x] = new Color32(col.r, col.g, col.b, (byte)(255 * Mathf.Clamp01(a)));
                }
            // drips run down (texture y goes up) from painted pixels on the lower edge of the shape
            foreach (var dr in drips)
            {
                int x0 = Mathf.Clamp((int)(dr.x * w), 2, w - 3);
                int yStart = -1;
                for (int y = 0; y < h; y++) if (px[y * w + x0].a > 200) { yStart = y; break; }
                if (yStart < 0) continue;
                int len = (int)(dr.z * h);
                for (int k = 0; k < len && yStart - k >= 0; k++)
                {
                    float wdt = Mathf.Lerp(2.5f, 1.2f, (float)k / len) + (k > len - 4 ? 1.2f : 0f);
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        float a = Mathf.Clamp01(wdt - Mathf.Abs(dx));
                        int i = (yStart - k) * w + x0 + dx;
                        if (a > px[i].a / 255f) px[i] = new Color32(col.r, col.g, col.b, (byte)(255 * a));
                    }
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }

    /// <summary>Import settings for graffiti pictures and the hook that keeps materials in sync with the folder.</summary>
    class GraffitiImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            string p = assetPath;
            if (!p.StartsWith(GraffitiLibrary.Root + "/")) return;
            var ti = (TextureImporter)assetImporter;
            if (ti.importSettingsMissing)
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.mipmapEnabled = true;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.Compressed;
            }
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;   // no dark halo around the paint
            ti.wrapMode = TextureWrapMode.Clamp;   // no repeats at the decal edges
        }

        static readonly HashSet<string> pending = new HashSet<string>();
        static readonly HashSet<string> sheets = new HashSet<string>();
        static bool queued;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool any = false;
            foreach (var p in imported)
            {
                if (GraffitiSheet.IsCandidate(p)) { sheets.Add(p); any = true; }
                if (GraffitiLibrary.IsSource(p)) { pending.Add(AssetDatabase.AssetPathToGUID(p)); any = true; }
            }
            foreach (var p in deleted) if (GraffitiLibrary.IsSource(p)) any = true;
            foreach (var p in moved) if (GraffitiLibrary.IsSource(p)) any = true;
            foreach (var p in movedFrom) if (GraffitiLibrary.IsSource(p)) any = true;
            if (!any || queued) return;
            queued = true;
            EditorApplication.delayCall += () =>
            {
                queued = false;
                var changed = new HashSet<string>(pending);
                var cut = new List<string>(sheets);
                pending.Clear();
                sheets.Clear();
                foreach (var sp in cut) GraffitiSheet.Process(sp, false);   // sheets first: their pieces get materials in the same pass
                GraffitiLibrary.Sync(changed);
                GraffitiWindow.RepaintAll();
            };
        }
    }
}
