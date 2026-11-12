using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarX.EditorTools
{
    // Builds a throw-away demo scene with Mixamo NPCs inside the Tunnel Kit demo layout.
    // Menu: CarX > NPC Demo > 1. Setup Imports, then 2. Build Scene.
    public static class NpcDemoBuilder
    {
        const string CharDir = "Assets/ThirdParty/Mixamo/Characters";
        const string AnimDir = "Assets/ThirdParty/Mixamo/Animations";
        const string GenDir = "Assets/_Project/Prefabs/NpcDemo";
        const string SourceScene = "Assets/_Project/Scenes/TunnelKit_Demo.unity";
        const string ScenePath = "Assets/_Project/Scenes/NPC_Demo.unity";
        const string SitClip = "Sit Ground";

        [MenuItem("CarX/NPC Demo/1. Setup Imports", priority = 20)]
        public static void SetupImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { CharDir, AnimDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) continue;
                bool isChar = path.StartsWith(CharDir);
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                if (isChar)
                {
                    imp.importAnimation = false;
                    imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                }
                else
                {
                    imp.importAnimation = true;
                    imp.materialImportMode = ModelImporterMaterialImportMode.None;
                    var defaults = imp.defaultClipAnimations;
                    var clips = new List<ModelImporterClipAnimation>();
                    string baseName = Path.GetFileNameWithoutExtension(path);
                    foreach (var c in defaults)
                    {
                        c.name = baseName;
                        c.loopTime = true;
                        c.lockRootRotation = true;
                        c.keepOriginalOrientation = true;
                        c.lockRootHeightY = true;
                        c.keepOriginalPositionY = true;
                        c.lockRootPositionXZ = true;
                        c.keepOriginalPositionXZ = true;
                        clips.Add(c);
                    }
                    if (baseName == "Standing Up" && defaults.Length > 0)
                    {
                        // The first frames of "Standing Up" are a person sitting on the ground: use them as a sit loop.
                        var d = defaults[0];
                        var sit = new ModelImporterClipAnimation
                        {
                            name = SitClip,
                            takeName = d.takeName,
                            firstFrame = d.firstFrame,
                            lastFrame = d.firstFrame + 3,
                            loopTime = true,
                            lockRootRotation = true, keepOriginalOrientation = true,
                            lockRootHeightY = true, keepOriginalPositionY = true,
                            lockRootPositionXZ = true, keepOriginalPositionXZ = true,
                        };
                        clips.Add(sit);
                    }
                    imp.clipAnimations = clips.ToArray();
                }
                imp.SaveAndReimport();
                Debug.Log("CarX NPC: import set up for " + path);
            }
        }

        // Mixamo FBX keep textures embedded: pull them out so the imported materials can find them.
        public static void ExtractTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { CharDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                string dir = CharDir + "/Textures/" + Path.GetFileNameWithoutExtension(path);
                Directory.CreateDirectory(dir);
                imp.ExtractTextures(dir);
                imp.materialSearch = ModelImporterMaterialSearch.Everywhere;
                imp.materialName = ModelImporterMaterialName.BasedOnMaterialName;
                AssetDatabase.Refresh();
                imp.SaveAndReimport();
                Debug.Log("CarX NPC: textures extracted for " + path);
            }
        }

        [MenuItem("CarX/NPC Demo/2. Build Scene", priority = 21)]
        public static void BuildScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(GenDir + "/Materials");
            Directory.CreateDirectory(GenDir + "/Textures");
            Directory.CreateDirectory(GenDir + "/Animators");
            AssetDatabase.Refresh();
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { GenDir + "/Materials" }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(g));
            dirtyCache.Clear();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.CopyAsset(SourceScene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);

            EnsureDecalFeature();
            var scarMat = ScarMaterial();

            var root = new GameObject("NPCs").transform;
            // Zone 1 corridor (player starts at z = -0.5 looking along +Z).
            Spawn(root, "Ch17", SitClip, new Vector3(-1.05f, 0f, 3.2f), 90f, false);
            Spawn(root, "Ch08", "Nervously Look Around", new Vector3(0.85f, 0f, 6.2f), 215f, true, scarMat);
            // East branch of the T: a "deeper" one.
            Spawn(root, "Ch02", "Zombie Idle", new Vector3(5.6f, 0f, 8.0f), 270f, true, scarMat);
            // Two people standing together at the crossroads.
            Spawn(root, "Ch02", "Idle", new Vector3(-0.55f, 0f, 15.6f), 55f, false);
            Spawn(root, "Ch17", "Idle", new Vector3(0.55f, 0f, 16.35f), 235f, false);

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("CarX NPC: demo scene saved to " + ScenePath);
        }

        static AnimationClip FindClip(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { AnimDir }))
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                    if (a is AnimationClip c && c.name == name) return c;
            Debug.LogError("CarX NPC: clip not found " + name);
            return null;
        }

        static AnimatorController Controller(string clipName)
        {
            string path = GenDir + "/Animators/" + clipName + ".controller";
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ctrl != null) return ctrl;
            return AnimatorController.CreateAnimatorControllerAtPathWithClip(path, FindClip(clipName));
        }

        static void Spawn(Transform parent, string character, string clipName, Vector3 pos, float yaw, bool scar, Material scarMat = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharDir + "/" + character + ".fbx");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = character + " - " + clipName;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));

            var anim = go.GetComponent<Animator>();
            if (anim == null) anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = Controller(clipName);
            anim.applyRootMotion = false;
            go.AddComponent<CarX.NPC.NpcLoopVariation>();

            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(m => DirtyMaterial(character, m)).ToArray();

            if (scar && scarMat != null)
            {
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    // Still in bind pose here, so the face looks along the character's forward.
                    var t = go.transform;
                    var d = new GameObject("Scar Decal");
                    d.transform.SetParent(head, false);
                    d.transform.position = head.position + t.up * 0.075f + t.right * 0.035f + t.forward * 0.16f;
                    d.transform.rotation = Quaternion.LookRotation(-t.forward, t.up) * Quaternion.Euler(0, 0, 25f);
                    var p = d.AddComponent<DecalProjector>();
                    p.material = scarMat;
                    p.size = new Vector3(0.07f, 0.11f, 0.22f);
                    p.pivot = new Vector3(0, 0, 0.11f);
                }
            }

            var clip = FindClip(clipName);
            if (clip != null) clip.SampleAnimation(go, clip.length * 0.3f); // pose for the editor view
        }

        // ---------------- grime ----------------

        static readonly Dictionary<string, Material> dirtyCache = new Dictionary<string, Material>();

        static Material DirtyMaterial(string character, Material src)
        {
            if (src == null) return null;
            string lower = src.name.ToLowerInvariant();
            if (lower.Contains("eye") || lower.Contains("lash") || lower.Contains("teeth") || lower.Contains("tongue")) return src;
            string key = character + "_" + src.name;
            if (dirtyCache.TryGetValue(key, out var cached) && cached != null) return cached;

            string matPath = GenDir + "/Materials/" + Safe(key) + "_Dirty.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existing != null) { dirtyCache[key] = existing; return existing; }

            var mat = new Material(src) { name = Safe(key) + "_Dirty" };
            var baseTex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") as Texture2D : null;
            bool hair = lower.Contains("hair");
            if (baseTex == null)
            {
                // URP importer does not bind Mixamo textures: map materials to UDIM tiles (body, body1, ..., hair = last).
                string dir = CharDir + "/Textures/" + character;
                var tiles = Directory.GetFiles(dir, "*_Diffuse.png").Select(f => Path.GetFileName(f)).OrderBy(f => f).ToList();
                string suffix = lower.Substring(lower.LastIndexOf('_') + 1);
                int idx = hair ? tiles.Count - 1 : (suffix == "body" ? 0 : int.TryParse(suffix.Replace("body", ""), out int k) ? k : 0);
                if (tiles.Count > 0)
                {
                    string diffuse = dir + "/" + tiles[Mathf.Clamp(idx, 0, tiles.Count - 1)];
                    baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(diffuse);
                    string normal = diffuse.Replace("_Diffuse", "_Normal");
                    if (File.Exists(normal))
                    {
                        var ni = (TextureImporter)AssetImporter.GetAtPath(normal);
                        if (ni.textureType != TextureImporterType.NormalMap) { ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport(); }
                        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                        mat.EnableKeyword("_NORMALMAP");
                    }
                }
            }
            if (hair)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.4f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", 0f);
            }
            if (baseTex != null)
            {
                bool skin = lower.Contains("body") || lower.Contains("skin") || lower.Contains("head") || lower.Contains("face");
                mat.SetTexture("_BaseMap", Grime(baseTex, Safe(key), skin ? 0.55f : 1f));
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.82f, 0.78f, 0.72f, 1f));
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.12f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(mat, matPath);
            dirtyCache[key] = mat;
            return mat;
        }

        static Texture2D Grime(Texture2D src, string name, float strength)
        {
            int w = Mathf.Min(src.width, 2048), h = Mathf.Min(src.height, 2048);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var px = tex.GetPixels32();
            var dirt = new Color(0.23f, 0.18f, 0.12f);
            float seed = name.GetHashCode() % 1000;
            for (int y = 0; y < h; y++)
            {
                float v = (float)y / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / w;
                    float n = Mathf.PerlinNoise(seed + u * 6f, seed + v * 6f) * 0.6f
                            + Mathf.PerlinNoise(seed * 2 + u * 23f, v * 23f) * 0.3f
                            + Mathf.PerlinNoise(u * 90f, seed + v * 90f) * 0.1f;
                    float stain = Mathf.Clamp01((n - 0.42f) * 2.2f) * strength;
                    float grain = (Mathf.PerlinNoise(u * 400f, v * 400f) - 0.5f) * 0.12f * strength;
                    int i = y * w + x;
                    Color c = px[i];
                    // desaturate (sun-faded, worn) and darken toward mud
                    float g = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                    c = Color.Lerp(c, new Color(g, g, g), 0.45f * strength);
                    c = Color.Lerp(c, dirt, stain * 0.75f);
                    c *= 0.88f + grain;
                    c.a = px[i].a / 255f;
                    px[i] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            string path = GenDir + "/Textures/" + name + "_Dirty.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static string Safe(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_').Replace(':', '_');
        }

        // ---------------- scar ----------------

        static Material ScarMaterial()
        {
            string texPath = GenDir + "/Textures/Scar.png";
            int s = 256;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color[s * s];
            var scar = new Color(0.42f, 0.13f, 0.12f);
            var stitch = new Color(0.08f, 0.05f, 0.05f);
            // gently curved line from top to bottom
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float v = (float)y / s, u = (float)x / s;
                    float cx = 0.5f + Mathf.Sin(v * 3.1f) * 0.12f + (Mathf.PerlinNoise(v * 12f, 3f) - 0.5f) * 0.05f;
                    float dist = Mathf.Abs(u - cx);
                    float ends = Mathf.SmoothStep(0f, 1f, Mathf.Min(v, 1f - v) * 6f);
                    float core = Mathf.Clamp01(1f - dist / 0.035f) * ends;
                    float halo = Mathf.Clamp01(1f - dist / 0.14f) * ends * 0.35f;
                    // stitches: short dark bars across the scar every ~1/9 of its length
                    float st = Mathf.Abs(Mathf.Repeat(v * 9f, 1f) - 0.5f) < 0.06f && dist < 0.09f ? ends : 0f;
                    Color c = Color.Lerp(new Color(0.55f, 0.25f, 0.22f), scar, core);
                    float a = Mathf.Max(core * 0.95f, halo);
                    if (st > 0f) { c = stitch; a = Mathf.Max(a, 0.9f * st); }
                    c.a = a;
                    px[y * s + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();

            string matPath = GenDir + "/Materials/Scar_Decal.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Shader Graphs/Decal"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("Base_Map", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Decals need the Decal renderer feature on every URP renderer the project uses.
        static void EnsureDecalFeature()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Settings" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.rendererFeatures.Any(f => f is DecalRendererFeature)) continue;
                var feature = ScriptableObject.CreateInstance<DecalRendererFeature>();
                feature.name = "Decal";
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
                var so = new SerializedObject(data);
                var list = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
                Debug.Log("CarX NPC: added Decal feature to " + data.name);
            }
            AssetDatabase.SaveAssets();
        }

        // ---------------- screenshots ----------------

        // Like the bridge's "shot", but with a flashlight at the camera.
        public static void FlashShot(string name, float x, float y, float z, float yaw, float pitch)
        {
            var go = new GameObject("_ShotFlashlight") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetPositionAndRotation(new Vector3(x + 0.15f, y - 0.1f, z), Quaternion.Euler(pitch, yaw, 0));
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = 50f;
            l.innerSpotAngle = 25f;
            l.range = 18f;
            l.intensity = 6f;
            l.color = new Color(1f, 0.93f, 0.8f);
            l.shadows = LightShadows.Soft;
            try
            {
                var bridge = typeof(NpcDemoBuilder).Assembly.GetType("CarX.EditorTools.CarXRemoteBridge");
                var shot = bridge.GetMethod("Shot", BindingFlags.Static | BindingFlags.NonPublic);
                string F(float f) => f.ToString(System.Globalization.CultureInfo.InvariantCulture);
                shot.Invoke(null, new object[] { new[] { "shot", name, F(x), F(y), F(z), F(yaw), F(pitch), "60" } });
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
