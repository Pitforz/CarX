using System;
using System.IO;
using System.Threading.Tasks;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CarX.EditorTools
{
    /// <summary>Procedural tileable textures, materials, post effects and darkness for the tunnels.</summary>
    [InitializeOnLoad]
    public static partial class TunnelAssets
    {
        public const string TexDir = "Assets/_Project/Textures/Tunnel";
        public const string MatDir = "Assets/_Project/Materials/Tunnel";
        public const string SetPath = "Assets/_Project/Settings/TunnelMaterials.asset";
        public const string PostPath = "Assets/_Project/Settings/TunnelPostFX.asset";
        public const string CookiePath = TexDir + "/T_FlashlightCookie.png";

        static TunnelAssets()
        {
            // the library's set wins (that is where the current look lives), the old set is the fallback
            TunnelMaterials.DefaultProvider = () =>
            {
                var lib = AssetDatabase.LoadAssetAtPath<TunnelTypeLibrary>(TunnelGraphAssets.LibraryPath);
                if (lib != null && lib.materials != null) return lib.materials;
                return AssetDatabase.LoadAssetAtPath<TunnelMaterials>(SetPath);
            };
        }

        [MenuItem("CarX/Tunnels/Regenerate Textures and Materials", priority = 40)]
        static void MenuRegenerate()
        {
            Get(true);
            Debug.Log("CarX: tunnel textures and materials regenerated");
        }

        // ------------------------------------------------------------------ public API

        public static TunnelMaterials Get(bool regenerate)
        {
            EnsureFolder(TexDir);
            EnsureFolder(MatDir);
            EnsureFolder("Assets/_Project/Settings");
            bool need = regenerate || !File.Exists(TexDir + "/T_Concrete_A.png") || !File.Exists(CookiePath);
            if (need)
            {
                try
                {
                    EditorUtility.DisplayProgressBar("CarX", "Генерирую текстуры тоннелей…", 0.1f);
                    GenerateTextures();
                }
                finally { EditorUtility.ClearProgressBar(); }
            }

            var set = AssetDatabase.LoadAssetAtPath<TunnelMaterials>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<TunnelMaterials>();
                AssetDatabase.CreateAsset(set, SetPath);
            }
            if (need || set.concrete == null || set.sky == null || set.concreteDirty == null)
            {
                set.concrete = Lit("M_Concrete", "T_Concrete", 1.0f, 1f, 0f, Color.white);
                set.concreteDirty = Lit("M_ConcreteDirty", "T_ConcreteDirty", 1.0f, 1f, 0f, Color.white);
                set.floor = Lit("M_Mud", "T_Mud", 1.0f, 1f, 0f, Color.white);
                set.soil = Lit("M_Soil", "T_Soil", 1.2f, 1f, 0f, Color.white);
                set.sand = Lit("M_Sand", "T_Sand", 0.8f, 1f, 0f, Color.white);
                set.iron = Lit("M_Iron", "T_Iron", 1.0f, 1f, 1f, Color.white);
                set.lid = Lit("M_ManholeCover", "T_Lid", 1.2f, 1f, 1f, Color.white);
                set.lid.SetFloat("_AlphaClip", 1f);
                set.lid.SetFloat("_Cutoff", 0.5f);
                set.lid.SetFloat("_Cull", 0f);
                Validate(set.lid);
                set.water = Water();
                set.shaft = Custom("M_LightShaft", "CarX/LightShaft", null);
                set.ray = Custom("M_LightRay", "CarX/LightRay", m => { m.SetFloat("_Intensity", 1.4f); });
                set.dust = Custom("M_DustMote", "CarX/DustMote", null);
                set.sky = Custom("M_Daylight", "CarX/Emissive", m => { m.SetColor("_Color", new Color(5.5f, 5.8f, 6.4f, 1f)); });
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssets();
            }
            ConfigurePipeline();
            return set;
        }

        public static Texture2D Cookie() { return AssetDatabase.LoadAssetAtPath<Texture2D>(CookiePath); }

        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ materials

        static Texture2D Tex(string name) { return AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + name + ".png"); }

        static Material Lit(string name, string tex, float bump, float smoothness, float metallic, Color tint)
        {
            string path = MatDir + "/" + name + ".mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            var albedo = Tex(tex + "_A");
            var normal = Tex(tex + "_N");
            var mask = Tex(tex + "_M");
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_MainTex", albedo);
            m.SetColor("_BaseColor", tint);
            m.SetColor("_Color", tint);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", bump);
            m.SetTexture("_MetallicGlossMap", mask);
            m.SetFloat("_Smoothness", mask != null ? smoothness : 0.2f);
            m.SetFloat("_Metallic", mask != null ? 1f : metallic);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.SetFloat("_SpecularHighlights", 1f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_Cull", 2f);
            m.SetTextureScale("_BaseMap", Vector2.one);
            if (normal != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            if (mask != null) m.EnableKeyword("_METALLICSPECGLOSSMAP"); else m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            Validate(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Water()
        {
            string path = MatDir + "/M_Water.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            m.SetTexture("_BaseMap", null);
            m.SetColor("_BaseColor", new Color(0.035f, 0.04f, 0.035f, 0.82f));
            m.SetTexture("_BumpMap", Tex("T_Water_N"));
            m.SetFloat("_BumpScale", 0.35f);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTexture("_MetallicGlossMap", null);
            m.SetFloat("_Smoothness", 0.96f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.SetFloat("_SpecularHighlights", 1f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_Cull", 2f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
            Validate(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Custom(string name, string shaderName, Action<Material> setup)
        {
            string path = MatDir + "/" + name + ".mat";
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError("CarX: shader not found " + shaderName);
                return null;
            }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            if (setup != null) setup(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Lets URP's own material inspector fix keywords, queues and blend states.</summary>
        static void Validate(Material m)
        {
            try
            {
                var t = Type.GetType("UnityEditor.Rendering.Universal.ShaderGUI.LitShader, Unity.RenderPipelines.Universal.Editor");
                if (t == null) return;
                var gui = Activator.CreateInstance(t) as ShaderGUI;
                if (gui != null) gui.ValidateMaterial(m);
            }
            catch (Exception e) { Debug.LogWarning("CarX: material validation skipped: " + e.Message); }
        }

        static void ConfigurePipeline()
        {
            var assets = new System.Collections.Generic.List<UniversalRenderPipelineAsset>();
            var def = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (def != null) assets.Add(def);
            var q = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            if (q != null && !assets.Contains(q)) assets.Add(q);
            foreach (var a in assets)
            {
                bool changed = false;
                if (!a.supportsCameraDepthTexture) { a.supportsCameraDepthTexture = true; changed = true; }
                if (a.additionalLightsShadowmapResolution < 4096) { a.additionalLightsShadowmapResolution = 4096; changed = true; }
                if (a.shadowDistance < 60f) { a.shadowDistance = 60f; changed = true; }
                if (changed) EditorUtility.SetDirty(a);
            }
        }

        // ------------------------------------------------------------------ post effects & darkness

        public static VolumeProfile PostProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, PostPath);
            }
            var tm = Add<Tonemapping>(profile);
            tm.mode.Override(TonemappingMode.ACES);
            // reference photos: daylight at the far end burns out to white with a wide soft halo, the rest is dim, cool and grey
            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.8f);
            // the daylight is HDR ~20: unclamped, a big exit right in front of the camera blooms the whole frame to flat grey
            bloom.clamp.Override(16f);
            bloom.highQualityFiltering.Override(true);
            var vig = Add<Vignette>(profile);
            vig.intensity.Override(0.48f);
            vig.smoothness.Override(0.45f);
            var grain = Add<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.06f);
            grain.response.Override(0.75f);
            var ca = Add<ColorAdjustments>(profile);
            ca.postExposure.Override(0.35f);
            ca.contrast.Override(18f);
            ca.saturation.Override(-28f);
            var wb = Add<WhiteBalance>(profile);
            wb.temperature.Override(-10f);
            var chroma = Add<ChromaticAberration>(profile);
            chroma.intensity.Override(0.04f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T c;
            if (profile.TryGet(out c)) return c;
            c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        /// <summary>Near-black ambient, thick dark fog, no sky reflections, global post-processing volume.</summary>
        public static void ApplyAtmosphere()
        {
            RenderSettings.skybox = null;
            RenderSettings.sun = null;
            var lib = AssetDatabase.LoadAssetAtPath<TunnelTypeLibrary>(TunnelGraphAssets.LibraryPath);
            if (lib != null && lib.applyAtmosphere) lib.ApplyAtmosphere();   // the library holds the current look
            else
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                var amb = new Color(0.010f, 0.011f, 0.013f);
                RenderSettings.ambientLight = amb;
                var sh = new SphericalHarmonicsL2();
                sh.AddAmbientLight(amb);
                RenderSettings.ambientProbe = sh;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = 0.045f;
                RenderSettings.fogColor = new Color(0.005f, 0.0055f, 0.006f);
            }
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0f;

            var volGo = GameObject.Find("PostFX");
            if (volGo == null) volGo = new GameObject("PostFX");
            var vol = volGo.GetComponent<Volume>();
            if (vol == null) vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = PostProfile();
            foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) SetupCamera(cam);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        public static void SetupCamera(Camera cam)
        {
            var d = cam.GetUniversalAdditionalCameraData();
            d.renderPostProcessing = true;
            d.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            d.antialiasingQuality = AntialiasingQuality.High;
            d.requiresDepthTexture = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.allowHDR = true;
            EditorUtility.SetDirty(d);
            EditorUtility.SetDirty(cam);
        }

        // ------------------------------------------------------------------ texture generation

        enum Kind { Color, Linear, Normal }

        struct S
        {
            public Color c;      // albedo (linear-ish 0..1 values written as sRGB)
            public float h;      // height
            public float sm;     // smoothness
            public float mt;     // metallic
            public float a;      // alpha
        }

        static void GenerateTextures()
        {
            Build("T_Concrete", 1024, Concrete, 5f, true);
            Build("T_ConcreteDirty", 1024, ConcreteDirty, 5f, true);
            EditorUtility.DisplayProgressBar("CarX", "Генерирую текстуры тоннелей…", 0.4f);
            Build("T_Mud", 1024, Mud, 4f, true);
            Build("T_Sand", 512, Sand, 3f, true);
            Build("T_Soil", 512, Soil, 4f, true);
            EditorUtility.DisplayProgressBar("CarX", "Генерирую текстуры тоннелей…", 0.7f);
            Build("T_Iron", 512, Iron, 3f, true);
            Build("T_Lid", 512, Lid, 6f, true);
            WaterNormal(512);
            FlashlightCookie(256);
            Import(TexDir + "/T_ConcreteDirty_A.png", Kind.Color, true);
            Import(TexDir + "/T_ConcreteDirty_N.png", Kind.Normal, true);
            Import(TexDir + "/T_ConcreteDirty_M.png", Kind.Linear, true);
        }

        static void Build(string name, int n, Func<float, float, S> fn, float normalStrength, bool withMask)
        {
            var albedo = new Color32[n * n];
            var mask = new Color32[n * n];
            var height = new float[n * n];
            Parallel.For(0, n, y =>
            {
                float v = (y + 0.5f) / n;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n;
                    var s = fn(u, v);
                    int i = y * n + x;
                    albedo[i] = new Color32(B(s.c.r), B(s.c.g), B(s.c.b), B(s.a));
                    mask[i] = new Color32(B(s.mt), 255, 0, B(s.sm));
                    height[i] = s.h;
                }
            });
            Save(name + "_A", albedo, n, Kind.Color);
            Save(name + "_N", NormalFromHeight(height, n, normalStrength), n, Kind.Normal);
            if (withMask) Save(name + "_M", mask, n, Kind.Linear);
        }

        static byte B(float f) { return (byte)Mathf.Clamp(Mathf.RoundToInt(f * 255f), 0, 255); }

        static Color32[] NormalFromHeight(float[] h, int n, float strength)
        {
            var res = new Color32[n * n];
            float k = strength * n / 512f;
            Parallel.For(0, n, y =>
            {
                int yu = (y + 1) % n, yd = (y - 1 + n) % n;
                for (int x = 0; x < n; x++)
                {
                    int xr = (x + 1) % n, xl = (x - 1 + n) % n;
                    float dx = (h[y * n + xr] - h[y * n + xl]) * k;
                    float dy = (h[yu * n + x] - h[yd * n + x]) * k;
                    var nrm = new Vector3(-dx, -dy, 1f).normalized;
                    res[y * n + x] = new Color32(B(nrm.x * 0.5f + 0.5f), B(nrm.y * 0.5f + 0.5f), B(nrm.z * 0.5f + 0.5f), 255);
                }
            });
            return res;
        }

        static void Save(string name, Color32[] px, int n, Kind kind)
        {
            string path = TexDir + "/" + name + ".png";
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false, kind != Kind.Color);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Import(path, kind, false);
        }

        static void Import(string path, Kind kind, bool clampV)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            imp.textureType = kind == Kind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = kind == Kind.Color;
            imp.alphaSource = kind == Kind.Normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = false;
            imp.mipmapEnabled = true;
            imp.wrapModeU = TextureWrapMode.Repeat;
            imp.wrapModeV = clampV ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 4;
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            if (path.Contains("T_Lid_A"))
            {
                imp.mipMapsPreserveCoverage = true;
                imp.alphaTestReferenceValue = 0.5f;
            }
            imp.SaveAndReimport();
        }

        // ---------- noise (all periodic over the 0..1 tile) ----------

        static readonly Vector2[] Grad = MakeGrad();

        static Vector2[] MakeGrad()
        {
            var g = new Vector2[64];
            for (int i = 0; i < 64; i++) g[i] = new Vector2(Mathf.Cos(i * Mathf.PI * 2f / 64f), Mathf.Sin(i * Mathf.PI * 2f / 64f));
            return g;
        }

        static int Hash(int x, int y, int s)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + s * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return h ^ (h >> 16);
            }
        }

        static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

        static float Perlin(float x, float y, int px, int py, int s)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            int ax = Mod(x0, px), bx = Mod(x0 + 1, px), ay = Mod(y0, py), by = Mod(y0 + 1, py);
            Vector2 g00 = Grad[Hash(ax, ay, s) & 63], g10 = Grad[Hash(bx, ay, s) & 63], g01 = Grad[Hash(ax, by, s) & 63], g11 = Grad[Hash(bx, by, s) & 63];
            float n00 = g00.x * fx + g00.y * fy;
            float n10 = g10.x * (fx - 1f) + g10.y * fy;
            float n01 = g01.x * fx + g01.y * (fy - 1f);
            float n11 = g11.x * (fx - 1f) + g11.y * (fy - 1f);
            float u = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            float v = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v) * 1.45f;
        }

        /// <summary>Fractal noise, roughly -0.6..0.6. fx, fy = base frequency (tiles per texture) per axis.</summary>
        static float Fbm(float u, float v, int fx, int fy, int oct, int s)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < oct; o++)
            {
                sum += amp * Perlin(u * fx, v * fy, fx, fy, s + o * 131);
                norm += amp;
                amp *= 0.5f;
                fx *= 2; fy *= 2;
            }
            return sum / norm;
        }

        static float Fbm(float u, float v, int f, int oct, int s) { return Fbm(u, v, f, f, oct, s); }

        /// <summary>Periodic cellular noise. Distances in cell units. id = hash of the nearest cell.</summary>
        static void Worley(float u, float v, int f, int s, out float f1, out float f2, out int id)
        {
            float x = u * f, y = v * f;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            f1 = f2 = 9f;
            id = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = xi + dx, cy = yi + dy;
                    int h = Hash(Mod(cx, f), Mod(cy, f), s);
                    float px = cx + (h & 1023) / 1023f, py = cy + ((h >> 10) & 1023) / 1023f;
                    float ddx = px - x, ddy = py - y;
                    float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                    if (d < f1) { f2 = f1; f1 = d; id = h; }
                    else if (d < f2) f2 = d;
                }
        }

        static float R01(int id, int shift) { return ((id >> shift) & 255) / 255f; }
        static float Sat(float x) { return Mathf.Clamp01(x); }
        static float Smooth(float e0, float e1, float x) { float t = Sat((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        // ---------- materials ----------

        static S Concrete(float u, float v)
        {
            var s = new S { a = 1f };
            float big = Fbm(u, v, 3, 4, 101);
            float mid = Fbm(u, v, 11, 4, 102);
            float fine = Fbm(u, v, 48, 3, 103);
            float L = 0.52f + big * 0.11f + mid * 0.06f + fine * 0.04f;
            Color c = new Color(L, L * 0.985f, L * 0.95f);
            float h = 0.5f + big * 0.25f + mid * 0.18f + fine * 0.14f;
            float sm = 0.18f + mid * 0.05f;

            // formwork bug holes
            float f1, f2; int id;
            Worley(u, v, 64, 104, out f1, out f2, out id);
            float pore = R01(id, 8) > 0.5f ? Sat((0.17f - f1) / 0.07f) : 0f;
            c *= 1f - 0.55f * pore;
            h -= pore * 0.7f;

            // light seepage right at the slab joints (u = 0 and u = 1): a narrow, faint band, so joints don't stand out
            float dj = Mathf.Min(u, 1f - u) * 2f;
            float edgeN = Fbm(u, v, 6, 8, 4, 105);
            float seep = Sat(1f - dj / (0.10f + edgeN * 0.08f));
            seep *= seep;
            float streak = Sat((Fbm(u, v, 28, 2, 4, 106) - 0.04f) * 3.4f);
            streak *= Sat(0.3f + Fbm(u, v, 3, 2, 3, 107) * 1.6f);
            float wet = Sat(seep * 0.35f + streak * 0.6f);
            c = Color.Lerp(c, c * new Color(0.40f, 0.40f, 0.36f), wet);
            sm = Mathf.Lerp(sm, 0.62f, wet);

            // efflorescence: whitish mineral crust along the joints
            float eff = Sat((Fbm(u, v, 9, 4, 108) + seep * 0.25f - 0.34f) * 4f) * Sat(seep * 2.5f) * 0.5f;
            c = Color.Lerp(c, new Color(0.80f, 0.79f, 0.74f), eff * 0.55f);
            sm = Mathf.Lerp(sm, 0.08f, eff);
            h += eff * 0.18f;

            // black mould growing out of the joints and streaks
            float moldN = Fbm(u, v, 5, 5, 109) + seep * 0.12f + streak * 0.22f;
            Worley(u, v, 110, 110, out f1, out f2, out id);
            float speck = Sat((0.38f - f1) / 0.16f) * (R01(id, 12) > 0.35f ? 1f : 0f);
            float mold = Sat((moldN - 0.30f) * 6f);
            mold = Sat(mold + speck * Sat((moldN - 0.10f) * 4f) * 0.85f);
            c = Color.Lerp(c, new Color(0.04f, 0.045f, 0.035f), mold * 0.93f);
            sm = Mathf.Lerp(sm, 0.42f, mold);
            h += mold * 0.12f;

            // rare rust drips
            float rust = Sat((Fbm(u, v, 20, 3, 3, 111) - 0.18f) * 4f) * Sat((Fbm(u, v, 2, 2, 2, 112) - 0.22f) * 5f);
            c = Color.Lerp(c, new Color(0.40f, 0.23f, 0.11f), rust * 0.45f);

            // hairline cracks
            Worley(u, v, 7, 113, out f1, out f2, out id);
            float crack = Sat(1f - (f2 - f1) / 0.03f) * Sat((Fbm(u, v, 4, 3, 114) - 0.12f) * 4f);
            c = Color.Lerp(c, c * 0.3f, crack * 0.85f);
            h -= crack * 0.5f;

            s.c = c;
            s.h = h;
            s.sm = Sat(sm);
            return s;
        }

        static S ConcreteDirty(float u, float v)
        {
            // v maps to height: y metres = v * 2. Mud line at ~0.3 m with splashes above; clean concrete from ~0.8 m.
            var s = Concrete(u, v);
            float y = v * 2f;
            float line = 0.30f + Fbm(u, 0.37f, 12, 1, 3, 120) * 0.12f;
            float below = Sat((line - y) / 0.025f);
            float grad = Sat(y / Mathf.Max(0.05f, line));
            Color mud = Color.Lerp(new Color(0.13f, 0.115f, 0.09f), new Color(0.31f, 0.27f, 0.21f), grad * grad);
            mud *= 0.85f + (Fbm(u, v, 16, 4, 121) + 0.5f) * 0.3f;
            float drip = Sat((Fbm(u, v, 40, 3, 3, 122) - 0.08f) * 3f) * Sat((line + 0.05f - y) / 0.25f);
            mud = Color.Lerp(mud, mud * 0.6f, drip);
            s.c = Color.Lerp(s.c, mud, below * 0.95f);
            float tide = Mathf.Exp(-Mathf.Pow((y - line) / 0.018f, 2f));
            s.c = Color.Lerp(s.c, new Color(0.10f, 0.085f, 0.07f), tide * 0.65f);
            // an older, fainter flood line
            float line2 = 0.62f + Fbm(u, 0.81f, 9, 1, 3, 123) * 0.08f;
            float tide2 = Mathf.Exp(-Mathf.Pow((y - line2) / 0.03f, 2f));
            s.c = Color.Lerp(s.c, s.c * new Color(0.62f, 0.58f, 0.52f), tide2 * 0.55f);
            // splashes above the line
            float f1, f2; int id;
            Worley(u, v, 70, 124, out f1, out f2, out id);
            float above = Sat(1f - (y - line) / 0.4f) * (1f - below);
            float splash = R01(id, 8) > 0.55f ? Sat((0.33f - f1) / 0.12f) : 0f;
            s.c = Color.Lerp(s.c, new Color(0.30f, 0.26f, 0.2f), splash * above * 0.65f);
            // wet stain darkening towards the floor
            float damp = Sat(1f - y / 0.85f) * (1f - below);
            s.c *= 1f - damp * 0.18f;
            s.sm = Mathf.Lerp(s.sm, 0.32f, below * 0.8f + tide * 0.3f);
            s.h += below * (Fbm(u, v, 24, 3, 125) * 0.2f);
            return s;
        }

        static S Mud(float u, float v)
        {
            var s = new S { a = 1f };
            float big = Fbm(u, v, 3, 4, 201);
            float mid = Fbm(u, v, 10, 4, 202);
            float fine = Fbm(u, v, 40, 3, 203);
            float wet = Sat((Fbm(u, v, 4, 5, 204) + 0.04f) * 3.6f);
            Color dry = Color.Lerp(new Color(0.33f, 0.29f, 0.24f), new Color(0.42f, 0.385f, 0.32f), Sat(mid * 1.5f + 0.5f));
            Color wetC = new Color(0.14f, 0.125f, 0.105f);
            Color c = Color.Lerp(dry, wetC, wet) * (0.9f + fine * 0.22f + big * 0.16f);
            float h = 0.5f + big * 0.35f + mid * 0.25f + fine * 0.1f;

            float f1, f2; int id;
            Worley(u, v, 9, 205, out f1, out f2, out id);
            float crack = Sat(1f - (f2 - f1) / 0.05f) * (1f - wet) * Sat((Fbm(u, v, 3, 3, 206) + 0.2f) * 3f);
            c *= 1f - 0.5f * crack;
            h -= crack * 0.45f;

            Worley(u, v, 34, 207, out f1, out f2, out id);
            float peb = R01(id, 8) > 0.6f ? Sat((0.34f - f1) / 0.09f) : 0f;
            Color pc = new Color(0.47f, 0.45f, 0.41f) * (0.65f + R01(id, 16) * 0.55f);
            c = Color.Lerp(c, pc, peb * (1f - wet * 0.5f));
            h += peb * 0.6f * Sat((0.34f - f1) / 0.34f);

            Worley(u, v, 22, 208, out f1, out f2, out id);
            float debris = R01(id, 8) > 0.86f ? Sat((0.28f - f1) / 0.06f) : 0f;
            c = Color.Lerp(c, new Color(0.10f, 0.075f, 0.05f), debris * 0.8f);

            float sm = Mathf.Lerp(0.12f, 0.84f, wet);
            h = Mathf.Lerp(h, 0.45f + big * 0.05f, wet * 0.85f);
            s.c = c;
            s.h = h;
            s.sm = sm;
            return s;
        }

        static S Sand(float u, float v)
        {
            var s = new S { a = 1f };
            float g = Fbm(u, v, 64, 3, 301);
            float m = Fbm(u, v, 6, 4, 302);
            float warp = Fbm(u, v, 3, 3, 303);
            float ripple = Mathf.Sin(((u * 5f + v * 2f) + warp * 0.9f) * Mathf.PI * 2f);
            Color c = Color.Lerp(new Color(0.47f, 0.45f, 0.40f), new Color(0.58f, 0.555f, 0.50f), Sat(m * 1.4f + 0.5f));
            c *= 0.9f + g * 0.2f;
            c *= 1f - 0.08f * (ripple * 0.5f + 0.5f);
            float f1, f2; int id;
            Worley(u, v, 90, 304, out f1, out f2, out id);
            float grain = R01(id, 8) > 0.8f ? Sat((0.3f - f1) / 0.1f) : 0f;
            c = Color.Lerp(c, R01(id, 16) > 0.5f ? new Color(0.2f, 0.19f, 0.17f) : new Color(0.75f, 0.73f, 0.68f), grain * 0.6f);
            s.c = c;
            s.h = 0.5f + ripple * 0.12f + m * 0.25f + g * 0.08f;
            s.sm = 0.12f + Sat(-m) * 0.2f;
            return s;
        }

        static S Soil(float u, float v)
        {
            var s = new S { a = 1f };
            float m = Fbm(u, v, 5, 5, 401);
            float fine = Fbm(u, v, 40, 3, 402);
            // grey, close to the concrete, so soil showing in the seams barely stands out
            Color c = Color.Lerp(new Color(0.17f, 0.163f, 0.15f), new Color(0.30f, 0.288f, 0.265f), Sat(m * 1.5f + 0.5f));
            c *= 0.85f + fine * 0.3f;
            float f1, f2; int id;
            Worley(u, v, 14, 403, out f1, out f2, out id);
            float clump = Sat((f2 - f1) / 0.25f);
            c *= 0.75f + clump * 0.35f;
            Worley(u, v, 30, 404, out f1, out f2, out id);
            float peb = R01(id, 8) > 0.75f ? Sat((0.3f - f1) / 0.08f) : 0f;
            c = Color.Lerp(c, new Color(0.38f, 0.35f, 0.3f) * (0.7f + R01(id, 16) * 0.5f), peb);
            float root = Sat(1f - Mathf.Abs(Fbm(u, v, 12, 3, 3, 405)) / 0.02f) * Sat((Fbm(u, v, 3, 2, 406) - 0.1f) * 4f);
            c = Color.Lerp(c, new Color(0.30f, 0.285f, 0.26f), root * 0.4f);
            s.c = c;
            s.h = 0.5f + m * 0.3f + clump * 0.3f + peb * 0.5f + fine * 0.1f;
            s.sm = 0.3f + Sat(-m) * 0.25f;
            return s;
        }

        static S Iron(float u, float v)
        {
            var s = new S { a = 1f };
            float m = Fbm(u, v, 4, 5, 501);
            float fine = Fbm(u, v, 32, 3, 502);
            float rust = Sat((m + 0.12f) * 3.5f);
            float heavy = Sat((Fbm(u, v, 9, 4, 503) - 0.05f) * 4f) * rust;
            Color steel = new Color(0.30f, 0.29f, 0.28f) * (0.9f + fine * 0.2f);
            Color r1 = new Color(0.36f, 0.18f, 0.075f), r2 = new Color(0.18f, 0.09f, 0.04f);
            Color c = Color.Lerp(steel, Color.Lerp(r1, r2, heavy), rust);
            s.c = c * (0.9f + fine * 0.2f);
            s.mt = Mathf.Lerp(0.85f, 0.05f, rust);
            s.sm = Mathf.Lerp(0.45f, 0.12f, rust);
            s.h = 0.5f + heavy * 0.3f + fine * 0.15f;
            return s;
        }

        static S Lid(float u, float v)
        {
            var s = new S { a = 1f, mt = 0.6f };
            float dx = u - 0.5f, dy = v - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            float ang = Mathf.Atan2(dy, dx);
            float raised = 0f;
            raised = Mathf.Max(raised, Smooth(0.93f, 0.95f, r));                                  // rim
            raised = Mathf.Max(raised, Sat(1f - Mathf.Abs(r - 0.52f) / 0.02f));                   // ring
            raised = Mathf.Max(raised, Sat(1f - Mathf.Abs(r - 0.82f) / 0.02f));                   // ring
            // diamond grid pattern
            float gx = (dx + dy) * 26f, gy = (dx - dy) * 26f;
            float cell = Mathf.Min(Mathf.Abs(gx - Mathf.Round(gx)), Mathf.Abs(gy - Mathf.Round(gy)));
            float grid = Sat((cell - 0.18f) / 0.06f);
            if (r > 0.56f && r < 0.8f) raised = Mathf.Max(raised, grid);
            // radial ribs in the centre field
            float rib = Sat(1f - Mathf.Abs(Mathf.Sin(ang * 8f)) / 0.12f);
            if (r < 0.48f && r > 0.1f) raised = Mathf.Max(raised, rib * 0.8f);
            // bosses around the vent holes
            float hole = 0f;
            foreach (var hp in LidPattern.Holes)
            {
                float d = Mathf.Sqrt((u - hp.x) * (u - hp.x) + (v - hp.y) * (v - hp.y));
                if (d < hp.z) hole = 1f;
                raised = Mathf.Max(raised, Sat(1f - Mathf.Abs(d - hp.z * 1.6f) / (hp.z * 0.5f)));
            }
            float fine = Fbm(u, v, 40, 3, 601);
            float rustN = Fbm(u, v, 6, 4, 602);
            float rust = Sat((rustN + 0.1f) * 3f) * (1f - raised * 0.7f);
            Color iron = new Color(0.20f, 0.195f, 0.19f) * (0.9f + fine * 0.2f);
            Color worn = new Color(0.34f, 0.33f, 0.32f);
            Color c = Color.Lerp(iron, worn, raised * 0.8f);
            c = Color.Lerp(c, new Color(0.30f, 0.15f, 0.06f), rust * 0.75f);
            s.c = c;
            s.h = raised * 0.8f + fine * 0.1f - hole;
            s.sm = Mathf.Lerp(0.2f, 0.5f, raised) * (1f - rust * 0.6f);
            s.mt = Mathf.Lerp(0.55f, 0.85f, raised) * (1f - rust);
            s.a = (r > 0.995f || hole > 0f) ? 0f : 1f;
            return s;
        }

        static void WaterNormal(int n)
        {
            var h = new float[n * n];
            Parallel.For(0, n, y =>
            {
                float v = (y + 0.5f) / n;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n;
                    float w = Mathf.Sin((u * 3f + v * 1f) * Mathf.PI * 2f + 0.3f) * 0.3f
                            + Mathf.Sin((u * -2f + v * 4f) * Mathf.PI * 2f + 1.7f) * 0.25f
                            + Mathf.Sin((u * 7f + v * -5f) * Mathf.PI * 2f + 4.1f) * 0.12f
                            + Fbm(u, v, 6, 4, 701) * 0.6f;
                    h[y * n + x] = w;
                }
            });
            Save("T_Water_N", NormalFromHeight(h, n, 1.2f), n, Kind.Normal);
        }

        static void FlashlightCookie(int n)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float body = Smooth(0.97f, 0.62f, r);
                    float hot = Smooth(0.42f, 0.0f, r);
                    float ring = Mathf.Exp(-Mathf.Pow((r - 0.58f) / 0.05f, 2f)) * 0.22f;
                    float dirt = 0.92f + Fbm(u, v, 9, 3, 801) * 0.16f;
                    float I = Sat((body * (0.5f + 0.5f * hot) + ring * body) * dirt);
                    if (x == 0 || y == 0 || x == n - 1 || y == n - 1) I = 0f;
                    byte b = B(I);
                    px[y * n + x] = new Color32(b, b, b, b);
                }
            string path = CookiePath;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = false;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
        }
    }
}
