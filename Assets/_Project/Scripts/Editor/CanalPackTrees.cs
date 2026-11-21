using System.Collections.Generic;
using System.IO;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.EditorTools
{
    /// <summary>
    /// Trees from the imported "Tree Collection" pack (Assets/TreePackVol.1). Its prefabs are Tree Creator trees with built-in
    /// shaders (pink in URP): we make URP copies (Lit, leaves alpha-clipped and two-sided) in Prefabs/Canal/PackTrees and plant
    /// them only where a ray from above hits the ground on top of the banks, never the channel.
    /// </summary>
    public static partial class CanalEntranceBuilder
    {
        const string PackDir = "Assets/TreePackVol.1/Prefabs";
        const string PackTreeDir = "Assets/_Project/Prefabs/Canal/PackTrees";

        static bool PackAvailable() { return AssetDatabase.IsValidFolder(PackDir + "/Palm"); }

        // pack folders: Palm = palms, 0/3/5 = broadleaf trees (10-20 m), 1/2 = bushes (about 1 m), 4 = 25 m conifers (not used)
        static List<GameObject> PackPrefabs(string kind)
        {
            var list = new List<GameObject>();
            var folders = kind == "palm" ? new[] { PackDir + "/Palm" } : kind == "bush" ? new[] { PackDir + "/1", PackDir + "/2" } : new[] { PackDir + "/0", PackDir + "/3", PackDir + "/5" };
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", folders))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var p = UrpTree(path);
                if (p != null) list.Add(p);
            }
            return list;
        }

        /// <summary>URP copy of a pack tree prefab (made once; edit or delete it freely, it is made again if missing).</summary>
        static GameObject UrpTree(string srcPath)
        {
            string name = srcPath.Substring(PackDir.Length + 1).Replace(".prefab", "").Replace("/", "_").Replace(" ", "");
            string dst = PackTreeDir + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(dst);
            if (existing != null) return existing;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
            if (src == null) return null;
            var go = (GameObject)Object.Instantiate(src);
            go.name = name;
            foreach (var t in go.GetComponentsInChildren<Tree>(true)) Object.DestroyImmediate(t);
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    bool leaves = m.shader != null && m.shader.name.Contains("Leaves") || m.name.ToLowerInvariant().Contains("lea");
                    var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                    var col = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                    mats[i] = TreeMat(name + (leaves ? "_Leaves" : "_Bark"), tex, col, leaves);
                    any = true;
                }
                r.sharedMaterials = mats;
                r.renderingLayerMask = 1u << 3;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            if (!any) { Object.DestroyImmediate(go); return null; }
            Directory.CreateDirectory(PackTreeDir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, dst);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static Material TreeMat(string name, Texture tex, Color col, bool leaves)
        {
            string path = PackTreeDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Directory.CreateDirectory(PackTreeDir);
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", col);
            m.SetFloat("_Smoothness", leaves ? 0.25f : 0.1f);
            if (leaves)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.45f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f);
                m.doubleSidedGI = true;
                m.renderQueue = (int)RenderQueue.AlphaTest;
                m.SetOverrideTag("RenderType", "TransparentCutout");
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static float PrefabHeight(GameObject prefab)
        {
            float h = 0f;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) h = Mathf.Max(h, mf.sharedMesh.bounds.max.y * mf.transform.lossyScale.y);
            return Mathf.Max(0.5f, h);
        }

        /// <summary>Ground on top of the banks under (x, z) in the canal's local space, or null over the channel or over nothing.</summary>
        static Vector3? GroundAt(CanalEntrance c, Vector2 p)
        {
            float G = c.groundHeight;
            var origin = c.transform.TransformPoint(new Vector3(p.x, G + 40f, p.y));
            foreach (var hit in Physics.RaycastAll(origin, -c.transform.up, 80f, ~(1 << 2), QueryTriggerInteraction.Ignore))
            {
                var local = c.transform.InverseTransformPoint(hit.point);
                if (local.y < G - 0.6f) continue;               // channel floor or slope
                if (Vector3.Dot(hit.normal, c.transform.up) < 0.85f) continue;
                if (hit.collider.GetComponentInParent<OutdoorObjects>() != null) continue; // props, not ground
                return local;
            }
            return null;
        }

        /// <summary>Plants pack palms and trees (and the old shrubs) on the bank tops; more trees near the tunnel on the soil bank.</summary>
        public static int PlantPackTrees(CanalEntrance c)
        {
            Physics.SyncTransforms();
            var palms = PackPrefabs("palm");
            var trees = PackPrefabs("tree");
            var bushes = PackPrefabs("bush");
            if (palms.Count == 0 || trees.Count == 0) return 0;
            var props = GameObject.Find(PropsRootName);
            if (props == null) props = PlaceProps(c);
            var old = props.transform.Find(TreesRootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject(TreesRootName);
            Undo.RegisterCreatedObjectUndo(root, "Деревья");
            root.transform.SetParent(props.transform, false);
            if (c.shrubPrefab == null) AssignAssets(c);

            var rnd = new System.Random(c.seed * 977 + 5);
            System.Func<float> R = () => (float)rnd.NextDouble();
            float from = Mathf.Max(c.treesFrom, c.areaDepth + 5f), L = c.channelLength - 4f, cs = c.ConcreteSign;
            int placed = 0;
            System.Func<GameObject, float, float, float, bool> put = (prefab, s, u, height) =>
            {
                var g = GroundAt(c, c.PointAt(s, u));
                if (g == null) return false;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                go.transform.localPosition = g.Value - Vector3.up * 0.1f;
                go.transform.localRotation = Quaternion.Euler(0f, R() * 360f, 0f);
                go.transform.localScale = Vector3.one * (height / PrefabHeight(prefab));
                placed++;
                return true;
            };
            System.Func<List<GameObject>, GameObject> Pick = l => l[rnd.Next(l.Count)];

            // near the tunnel, on the soil bank (left when walking into the tunnel), several tries per tree
            for (int i = 0, tries = 0; i < 9 && tries < 80; tries++)
            {
                float s = Mathf.Lerp(1f, from, R());
                bool palm = R() < 0.45f;
                if (put(palm ? Pick(palms) : Pick(trees), s, -cs * (c.CrestAt(s) + 1.5f + R() * 5f), palm ? 8f + R() * 5f : 6f + R() * 3f)) i++;
            }
            // along the channel, both banks
            for (int i = 0, tries = 0; i < c.trees + c.palms + c.tallPalms && tries < 200; tries++)
            {
                float s = Mathf.Lerp(from, L, R()), side = R() < 0.6f ? -cs : cs;
                bool palm = R() < 0.55f;
                if (put(palm ? Pick(palms) : Pick(trees), s, side * (c.CrestAt(s) + 2f + R() * 5f), palm ? 8f + R() * 7f : 6f + R() * 4f)) i++;
            }
            // bushes from the pack at their own size (a bit bigger at most)
            for (int i = 0, tries = 0; i < c.shrubs && tries < 150; tries++)
            {
                float s = Mathf.Lerp(2f, L, R()), side = R() < 0.65f ? -cs : cs;
                var b = bushes.Count > 0 ? Pick(bushes) : c.shrubPrefab;
                if (put(b, s, side * (c.CrestAt(s) + 0.8f + R() * 4f), PrefabHeight(b) * (1f + 0.6f * R()))) i++;
            }
            var oo = props.GetComponent<OutdoorObjects>();
            if (oo != null) oo.Apply();
            EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
            return placed;
        }

        /// <summary>Bridge: pack trees + no fences + long camera range, then save.</summary>
        public static string FixTreesFencesRange()
        {
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c == null) return "no canal";
            var sb = new System.Text.StringBuilder();
            sb.Append(RemoveFences(c)).Append(" | ").Append(FarClip(3000f)).Append(" | trees ").Append(PackAvailable() ? PlantPackTrees(c).ToString() : "pack missing");
            EditorSceneManager.SaveScene(c.gameObject.scene);
            return sb.ToString();
        }

        /// <summary>Bridge: fences back along the banks and over the mouths, none on the far culvert, no torn spans.</summary>
        /// <summary>Floor wider under the slabs, short slabs in the bend, ground behind the clean bank without overhangs; grass regrown.</summary>
        public static string FixInnerBend()
        {
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c == null) return "no canal";
            // the channel course changed (smooth bend without loops): the whole channel is rebuilt
            c.ForceRebuild();
            RebuildChannel(c);
            var root = c.transform.Find(CanalEntrance.ChannelRootName);
            if (root == null) return "no channel";
            EditorUtility.SetDirty(c);
            EditorSceneManager.SaveScene(c.gameObject.scene);
            int slabs = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) if (mf.name.StartsWith("Плиты")) slabs += mf.sharedMesh.triangles.Length / 3;
            return "inner bend fixed, slab tris " + slabs;
        }

        public static string RestoreFences()
        {
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c == null) return "no canal";
            Undo.RecordObject(c, "Заборы");
            c.fences = true; c.culvertFence = false; c.fenceDamage = 0f;
            EditorUtility.SetDirty(c);
            if (c.chainLink == null || c.fenceSteel == null) AssignAssets(c);
            // keep the thin wires visible far away instead of fading to a grey sheet
            var imp = AssetImporter.GetAtPath(CanalTextures.TexDir + "/Canal_ChainLink.png") as TextureImporter;
            if (imp != null && !imp.mipMapsPreserveCoverage)
            {
                imp.mipMapsPreserveCoverage = true;
                imp.alphaTestReferenceValue = 0.35f;
                imp.SaveAndReimport();
            }
            var root = c.RebuildFences();
            if (root == null) return "no channel";
            string path = ChannelAssetPath(c);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Mesh && (o.name == "CanalFenceNet" || o.name == "CanalFencePosts") && !AssetDatabase.IsMainAsset(o))
                    { AssetDatabase.RemoveObjectFromAsset(o); Object.DestroyImmediate(o, true); }
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && mf.gameObject.name.StartsWith("Заборы") && !AssetDatabase.Contains(mf.sharedMesh))
                    { mf.sharedMesh.hideFlags = HideFlags.None; AssetDatabase.AddObjectToAsset(mf.sharedMesh, path); }
                AssetDatabase.SaveAssets();
            }
            EditorSceneManager.SaveScene(c.gameObject.scene);
            int n = 0;
            foreach (Transform t in root) if (t.name.StartsWith("Заборы")) n++;
            return "fences restored: " + n + " pieces";
        }

        /// <summary>No chain-link fences: switch them off for future rebuilds and delete the built ones.</summary>
        public static string RemoveFences(CanalEntrance c)
        {
            Undo.RecordObject(c, "Без заборов");
            c.fences = false;
            EditorUtility.SetDirty(c);
            int n = 0;
            var ch = c.transform.Find(CanalEntrance.ChannelRootName);
            if (ch != null)
                foreach (var t in ch.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name.StartsWith("Заборы")) { Undo.DestroyObjectImmediate(t.gameObject); n++; }
            return "fences removed " + n;
        }

        /// <summary>Far clip for every camera and Cinemachine lens in the open scenes (the Starter Assets follow camera has 500 m).</summary>
        public static string FarClip(float far)
        {
            int cams = 0, lenses = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if ((cam.hideFlags & HideFlags.DontSave) != 0) continue;
                Undo.RecordObject(cam, "Far clip");
                cam.farClipPlane = far; EditorUtility.SetDirty(cam); cams++;
            }
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (mb == null || !mb.GetType().FullName.Contains("Cinemachine")) continue;
                var so = new SerializedObject(mb);
                foreach (var path in new[] { "m_Lens.FarClipPlane", "Lens.FarClipPlane" })
                {
                    var p = so.FindProperty(path);
                    if (p == null) continue;
                    p.floatValue = far; lenses++;
                }
                so.ApplyModifiedProperties();
            }
            var sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                var s = sv.cameraSettings;
                s.dynamicClip = false; s.farClip = far * 2f;
                sv.cameraSettings = s;
            }
            return "far " + far + ": cameras " + cams + ", cinemachine lenses " + lenses;
        }
    }
}
