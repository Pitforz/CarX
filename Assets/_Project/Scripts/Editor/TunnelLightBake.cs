using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CarX.EditorTools
{
    /// <summary>
    /// «Перезапечь свет» (only on demand): bakes the soft light of the tunnels into Adaptive Probe Volumes:
    /// the halo round every lit patch, the light reflected by floor and walls and the faint light far down dark tunnels.
    /// Probes live in world space, so the tunnel meshes may be rebuilt on load (lightmaps would not survive that).
    /// The direct sunlight of the grates stays live: NPCs keep their shadows.
    /// </summary>
    public static class TunnelLightBake
    {
        const string ProbesName = "_CarX_TunnelProbes";
        const string ProxyName = "_CarX_BakeProxy";
        const float ProbeSpacing = 0.5f;
        static bool waiting, completed, retried, reloadLocked;
        static double startTime, retryAt;
        static int pendingHash;
        static Scene pendingScene;

        [MenuItem("CarX/Перезапечь свет", priority = 0)]
        static void BakeMenu() { Bake(); }

        [MenuItem("CarX/Настройки света тоннелей", priority = 1)]
        static void OpenSettings()
        {
            var lib = TunnelTypeLibrary.Current;
            if (lib == null || !EditorUtility.IsPersistent(lib))
            {
                Dialog("Не найдена библиотека типов тоннелей (TunnelTypes).");
                return;
            }
            Selection.activeObject = lib;
            EditorGUIUtility.PingObject(lib);
        }

        public static void Bake()
        {
            if (Lightmapping.isRunning)
            {
                if (EditorUtility.DisplayDialog("Запекание света", "Свет уже запекается. Остановить?", "Остановить", "Пусть идёт")) Lightmapping.Cancel();
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Dialog("Выйди из Play: свет запекается только в редакторе."); return; }
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) { Dialog("Сначала сохрани сцену: данные света лежат рядом с файлом сцены."); return; }
            if (TunnelNetwork.deferBuild) { Dialog("Выключи режим «Схема» у графа тоннелей: в нём тоннели не построены."); return; }

            // the probe system has to be on before anything can be baked; the pipeline restarts, so try again a moment later
            if (EnsureProbeVolumesOn() && !retried)
            {
                retried = true;
                retryAt = EditorApplication.timeSinceStartup + 1.5;
                SceneView.RepaintAll();
                EditorApplication.update -= Retry;
                EditorApplication.update += Retry;
                Debug.Log("CarX: Включил пробы освещения (Adaptive Probe Volumes) в настройках графики. Запекание начнётся через секунду.");
                return;
            }
            retried = false;
            var lib = TunnelTypeLibrary.Current;

            RemoveProxies(scene);
            foreach (var net in UnityEngine.Object.FindObjectsByType<TunnelNetwork>(FindObjectsInactive.Exclude))
            {
                if (net.gameObject.scene != scene) continue;
                net.EnsureLayout();
                if (net.BuildPending) net.Build();   // bake the tunnels as they will be played
            }
            ShaftRuntime.RefreshAll();

            EnsureLightingSettings(scene, lib);
            Bounds bounds;
            int count = PrepareRenderers(scene, out bounds);
            if (count == 0) { Dialog("В сцене нет тоннелей: запекать нечего."); return; }
            EnsureProbeVolume(scene, bounds);
            if (!EnsureBakingSet(scene)) { Dialog("Не удалось подготовить набор запекания проб (Adaptive Probe Volumes). Подробности в Console."); return; }
            int made = CreateProxies(scene, lib);
            pendingHash = ComputeHash(scene);
            pendingScene = scene;
            completed = false;
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
            startTime = EditorApplication.timeSinceStartup;
            // a script reload in the middle would forget this bake (temporary lights stay, the state is not saved): hold it till the end
            if (!reloadLocked) { EditorApplication.LockReloadAssemblies(); reloadLocked = true; }
            if (!Lightmapping.BakeAsync())
            {
                UnlockReload();
                Lightmapping.bakeCompleted -= OnBakeCompleted;
                RemoveProxies(scene);
                Dialog("Unity не начал запекание. Подробности в Console.");
                return;
            }
            waiting = true;
            EditorApplication.update -= Watch;
            EditorApplication.update += Watch;
            Debug.Log("CarX: Запекание света началось: тоннельных мешей " + count + ", мягких источников " + made +
                      ". Идёт в фоне, прогресс внизу справа. Остановить: CarX → Перезапечь свет.");
        }

        static void Retry()
        {
            if (EditorApplication.timeSinceStartup < retryAt) return;
            EditorApplication.update -= Retry;
            Bake();
        }

        static void OnBakeCompleted() { completed = true; }

        static void UnlockReload()
        {
            if (!reloadLocked) return;
            reloadLocked = false;
            EditorApplication.UnlockReloadAssemblies();
        }

        static void Watch()
        {
            if (!waiting || Lightmapping.isRunning) return;
            waiting = false;
            UnlockReload();
            EditorApplication.update -= Watch;
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            if (pendingScene.IsValid()) RemoveProxies(pendingScene);
            ShaftRuntime.RefreshAll();
            if (!completed || !pendingScene.IsValid())
            {
                Debug.LogWarning("CarX: Запекание света остановлено, данные не обновлены.");
                return;
            }
            var state = State(pendingScene, true);
            state.bakedHash = pendingHash;
            state.bakedAt = DateTime.Now.ToString("dd.MM HH:mm");
            state.bakeSeconds = (float)(EditorApplication.timeSinceStartup - startTime);
            EditorUtility.SetDirty(state);
            EditorSceneManager.MarkSceneDirty(pendingScene);
            staleTime = 0;
            Debug.Log("CarX: Свет запечён за " + state.bakeSeconds.ToString("0") + " с.");
            Notify("Свет запечён за " + state.bakeSeconds.ToString("0") + " с. Сохрани сцену (Ctrl+S).");
        }

        /// <summary>A message over the scene views that fades by itself (no window to click away).</summary>
        static void Notify(string text)
        {
            var content = new GUIContent(text);
            foreach (SceneView sv in SceneView.sceneViews) sv.ShowNotification(content, 8.0);
        }

        // ------------------------------------------------------------------ inspector of the tunnel graph

        static double staleTime;
        static bool staleCached;

        public static void NetworkGUI(TunnelNetwork net)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Свет тоннелей", EditorStyles.boldLabel);
            if (Lightmapping.isRunning)
            {
                EditorGUILayout.HelpBox("Свет запекается… Прогресс внизу справа в окне Unity.", MessageType.Info);
                if (GUILayout.Button("Остановить запекание")) Lightmapping.Cancel();
                return;
            }
            var scene = net.gameObject.scene;
            var state = State(scene, false);
            if (state == null || state.bakedHash == 0)
                EditorGUILayout.HelpBox("Свет ещё не запекался: нет мягкого ореола и отсветов на стенах. Нажмите «Перезапечь свет».", MessageType.Warning);
            else if (IsStale(scene, state))
                EditorGUILayout.HelpBox("Свет устарел: тоннели, решётки или солнце менялись после запекания (" + state.bakedAt + "). Перезапеките перед коммитом.", MessageType.Warning);
            else
                EditorGUILayout.LabelField("Свет запечён " + state.bakedAt + ", актуален.", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Перезапечь свет")) EditorApplication.delayCall += Bake;
                if (GUILayout.Button("Настройки света", GUILayout.Width(130f))) OpenSettings();
            }
        }

        static bool IsStale(Scene scene, TunnelLightingState state)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - staleTime > 1.0)
            {
                staleTime = now;
                staleCached = ComputeHash(scene) != state.bakedHash;
            }
            return staleCached;
        }

        // ------------------------------------------------------------------ preparation

        static void Dialog(string text) { EditorUtility.DisplayDialog("Свет тоннелей", text, "OK"); }

        /// <summary>Turns on Adaptive Probe Volumes in every URP asset in use. Returns true if something changed.</summary>
        static bool EnsureProbeVolumesOn()
        {
            var assets = new HashSet<RenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline != null) assets.Add(GraphicsSettings.defaultRenderPipeline);
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                var a = QualitySettings.GetRenderPipelineAssetAt(i);
                if (a != null) assets.Add(a);
            }
            bool changed = false;
            foreach (var a in assets)
            {
                if (!(a is UniversalRenderPipelineAsset)) continue;
                var so = new SerializedObject(a);
                var p = so.FindProperty("m_LightProbeSystem");
                if (p == null || p.intValue == 1) continue;
                p.intValue = 1;   // LightProbeSystem.ProbeVolumes
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(a);
                changed = true;
            }
            if (changed) AssetDatabase.SaveAssets();
            return changed;
        }

        static void EnsureLightingSettings(Scene scene, TunnelTypeLibrary lib)
        {
            LightingSettings ls;
            if (!Lightmapping.TryGetLightingSettings(out ls) || ls == null)
            {
                string path = (Path.GetDirectoryName(scene.path) + "/" + scene.name + "_Lighting.lighting").Replace('\\', '/');
                ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
                if (ls == null)
                {
                    ls = new LightingSettings { name = scene.name + "_Lighting" };
                    AssetDatabase.CreateAsset(ls, path);
                }
                Lightmapping.lightingSettings = ls;
            }
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.autoGenerate = false;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;   // grate sunlight stays live, only its bounce is baked
            // CPU: the GPU baker crashed the editor's graphics device on a laptop GPU (2026-11-22); probes bake fast enough on the CPU
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            ls.maxBounces = Mathf.Clamp(lib.bounceCount, 1, 4);
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 256;
            ls.environmentSampleCount = 64;
            ls.lightProbeSampleCountMultiplier = 2f;
            ls.indirectScale = 1f;
            ls.albedoBoost = 1f;
            ls.ao = false;
            EditorUtility.SetDirty(ls);
        }

        /// <summary>Tunnel meshes that block and bounce baked light.</summary>
        static List<MeshRenderer> CollectGI(Scene scene, List<MeshRenderer> probesOnly)
        {
            var list = new List<MeshRenderer>();
            var seen = new HashSet<MeshRenderer>();
            var roots = new List<Component>();
            roots.AddRange(UnityEngine.Object.FindObjectsByType<TunnelPath>(FindObjectsInactive.Exclude));
            roots.AddRange(UnityEngine.Object.FindObjectsByType<TunnelJunction>(FindObjectsInactive.Exclude));
            roots.AddRange(UnityEngine.Object.FindObjectsByType<TunnelRoom>(FindObjectsInactive.Exclude));
            roots.AddRange(UnityEngine.Object.FindObjectsByType<TunnelColumn>(FindObjectsInactive.Exclude));
            foreach (var root in roots)
            {
                if (root == null || root.gameObject.scene != scene) continue;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (r == null || !seen.Add(r)) continue;
                    if (ContributesGI(r)) list.Add(r);
                    else if (probesOnly != null && Lit(r)) probesOnly.Add(r);
                }
            }
            return list;
        }

        static bool Lit(MeshRenderer r)
        {
            string n = r.gameObject.name;
            return n != "Beam" && n != "Sky" && n != "Dust" && r.GetComponentInParent<TunnelExit>() == null;
        }

        static bool ContributesGI(MeshRenderer r)
        {
            if (!r.enabled || r.shadowCastingMode == ShadowCastingMode.Off) return false;
            int layer = r.gameObject.layer;
            if (layer == 30 || layer == 31) return false;   // touch and collision shells
            if (!Lit(r) || r.gameObject.name == "Iron") return false;
            for (var t = r.transform; t != null; t = t.parent)
                if (t.name == TunnelPath.DressingName) return false;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0) return false;
            foreach (var m in r.sharedMaterials)
                if (m != null && m.renderQueue >= 2450) return false;   // water and other see-through things
            return true;
        }

        /// <summary>Tunnel meshes of the last bake preparation (for the shape of the probe box).</summary>
        static readonly List<MeshRenderer> giRenderers = new List<MeshRenderer>();

        static int PrepareRenderers(Scene scene, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            var probesOnly = new List<MeshRenderer>();
            var list = CollectGI(scene, probesOnly);
            giRenderers.Clear();
            giRenderers.AddRange(list);
            foreach (var r in list)
            {
                TunnelGI.Setup(r, true);
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            foreach (var r in probesOnly) TunnelGI.Setup(r, false);
            return list.Count;
        }

        static void EnsureProbeVolume(Scene scene, Bounds bounds)
        {
            var go = Holder(scene);
            if (go == null)
            {
                go = new GameObject(ProbesName);
                SceneManager.MoveGameObjectToScene(go, scene);
            }
            Vector3 min = bounds.min - Vector3.one * 2f, max = bounds.max + Vector3.one * 2f;
            var extra = new List<Bounds>();
            ClipAtCanals(scene, bounds, ref min, ref max, extra);
            go.transform.SetPositionAndRotation((min + max) * 0.5f, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            var pv = go.GetComponent<ProbeVolume>();
            if (pv == null) pv = go.AddComponent<ProbeVolume>();
            pv.mode = ProbeVolume.Mode.Local;
            pv.size = max - min;
            LimitBricks(pv);
            // tunnel parts that rise above the cut top get boxes of their own
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                var ch = go.transform.GetChild(i);
                if (ch.name.StartsWith(ProbesName + "_Extra")) UnityEngine.Object.DestroyImmediate(ch.gameObject);
            }
            for (int i = 0; i < extra.Count; i++)
            {
                var e = new GameObject(ProbesName + "_Extra" + i);
                e.transform.SetParent(go.transform, false);
                e.transform.SetPositionAndRotation(extra[i].center, Quaternion.identity);
                var epv = e.AddComponent<ProbeVolume>();
                epv.mode = ProbeVolume.Mode.Local;
                LimitBricks(epv);
                epv.size = extra[i].size;
            }
            if (go.GetComponent<TunnelLightingState>() == null) go.AddComponent<TunnelLightingState>();
        }

        /// <summary>
        /// Probe bricks no bigger than 4.5 m (probe spacing 0.5 m: levels 0 and 1). A probe box only says where bricks may start:
        /// a big brick (13.5 or 40.5 m) that touches the box near a tunnel reaches tens of metres up into the open air, and its
        /// dark probes beat the canal's sky light there (the casino sign behind the headwall stayed black). Inside the tunnels
        /// every point is within a few metres of a wall, so the small bricks cover it all the same.
        /// </summary>
        static void LimitBricks(ProbeVolume pv)
        {
            pv.overridesSubdivLevels = true;
            pv.lowestSubdivLevelOverride = 0;
            pv.highestSubdivLevelOverride = 1;
        }

        /// <summary>
        /// The probe box must not reach out into an open canal in front of the tunnel mouths: probes there are baked dark and would
        /// beat the canal's own sky light (CanalEntrance.Outdoor). The box stops 0.3 m inside the headwall, if the canal is turned
        /// along a world axis and no tunnel goes on past the headwall.
        /// </summary>
        static void ClipAtCanals(Scene scene, Bounds tunnels, ref Vector3 min, ref Vector3 max, List<Bounds> extra)
        {
            // above the ground: open air (canal side, road, signs, towers) lit by the canal's sky light, no dark probes there.
            // The box stops 2.5 m above the canal ground; the few tunnel parts that rise higher get their own boxes
            float groundTop = float.MaxValue;
            foreach (var c in UnityEngine.Object.FindObjectsByType<CarX.Level.CanalEntrance>(FindObjectsInactive.Exclude))
                if (c != null && c.gameObject.scene == scene)
                    groundTop = Mathf.Min(groundTop, c.transform.TransformPoint(new Vector3(0f, c.groundHeight, 0f)).y + 2.5f);
            if (groundTop < max.y)
            {
                max.y = Mathf.Max(groundTop, min.y + 1f);
                foreach (var r in giRenderers)
                {
                    if (r == null || r.bounds.max.y <= max.y - 0.5f) continue;
                    var b = r.bounds;
                    b.Expand(4f);
                    extra.Add(b);
                }
                Debug.Log("CarX bake: probe box top cut at " + max.y.ToString("F1") + " m (canal ground + 2.5), extra boxes for tall tunnel parts: " + extra.Count);
            }

            foreach (var c in UnityEngine.Object.FindObjectsByType<CarX.Level.CanalEntrance>(FindObjectsInactive.Exclude))
            {
                if (c == null || c.gameObject.scene != scene) continue;
                var f = c.transform.forward;
                int axis = Mathf.Abs(f.x) > 0.999f ? 0 : Mathf.Abs(f.z) > 0.999f ? 2 : -1;
                if (axis < 0) { Debug.Log("CarX bake: canal " + c.name + " is not along a world axis, the probe box is not cut at it"); continue; }
                float sign = Mathf.Sign(f[axis]), wall = c.transform.position[axis];
                if (sign > 0f)
                {
                    if (tunnels.max[axis] > wall + 1f) { Debug.Log("CarX bake: tunnels go on past the headwall of " + c.name + ", the probe box is not cut"); continue; }
                    max[axis] = Mathf.Min(max[axis], wall - 0.3f);
                }
                else
                {
                    if (tunnels.min[axis] < wall - 1f) { Debug.Log("CarX bake: tunnels go on past the headwall of " + c.name + ", the probe box is not cut"); continue; }
                    min[axis] = Mathf.Max(min[axis], wall + 0.3f);
                }
                Debug.Log("CarX bake: probe box cut at the headwall of " + c.name);
            }
        }

        static GameObject Holder(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == ProbesName) return root;
            return null;
        }

        static TunnelLightingState State(Scene scene, bool create)
        {
            var go = Holder(scene);
            if (go == null) return null;
            var st = go.GetComponent<TunnelLightingState>();
            if (st == null && create) st = go.AddComponent<TunnelLightingState>();
            return st;
        }

        /// <summary>The scene's baking set of Adaptive Probe Volumes (created next to the scene if it has none).</summary>
        static bool EnsureBakingSet(Scene scene)
        {
            try
            {
                string guid = AssetDatabase.AssetPathToGUID(scene.path);
                var t = typeof(ProbeVolumeBakingSet);
                const BindingFlags any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var get = t.GetMethod("GetBakingSetForScene", any, null, new[] { typeof(string) }, null);
                if (get != null)
                {
                    var existing = get.Invoke(null, new object[] { guid }) as ProbeVolumeBakingSet;
                    if (existing != null)
                    {
                        // finer probes: the soft light round a long grate keeps its shape instead of a 1 m square blob
                        if (existing.minDistanceBetweenProbes > ProbeSpacing) { existing.minDistanceBetweenProbes = ProbeSpacing; EditorUtility.SetDirty(existing); AssetDatabase.SaveAssets(); }
                        return true;
                    }
                }
                var set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
                var defaults = t.GetMethod("SetDefaults", any, null, Type.EmptyTypes, null);
                if (defaults != null) defaults.Invoke(set, null);
                set.name = scene.name + " Baking Set";
                set.minDistanceBetweenProbes = ProbeSpacing;
                string parent = Path.GetDirectoryName(scene.path).Replace('\\', '/');
                string dir = parent + "/" + scene.name;
                if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(parent, scene.name);
                string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + set.name + ".asset");
                AssetDatabase.CreateAsset(set, path);
                if (!set.TryAddScene(guid))
                {
                    Debug.LogError("CarX: Сцена уже входит в другой набор запекания проб: откройте Window → Rendering → Lighting → Adaptive Probe Volumes.");
                    return false;
                }
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssets();
                var sync = t.GetMethod("SyncBakingSets", any, null, Type.EmptyTypes, null);
                if (sync != null) sync.Invoke(null, null);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        /// <summary>Baked-only lights for the bake: the soft halo round each lit patch and the faint far light. Removed afterwards.</summary>
        static int CreateProxies(Scene scene, TunnelTypeLibrary lib)
        {
            int made = 0;
            foreach (var s in ShaftRuntime.All)
            {
                if (s == null || s.gameObject.scene != scene || s.canFlicker || s.floorLight <= 0f || s.lit <= 0f) continue;
                // No light hangs in the air over the patch any more: it lit the people standing under the grate from all sides and
                // from below, so they had no shadow. The lit floor patch throws its light outwards only: spots at its edges facing
                // away from it (onto the walls, the ceiling and down the tunnel). Whoever stands in the patch is behind them and
                // keeps the light from above (the realtime sun with shadows) plus the real bounce of the sun in the bake.
                var g = s.geom;
                float wash = s.HaloIntensity(lib) + s.WallBounceIntensity(lib);
                float far = s.FarGlowIntensity(lib);
                float k = wash + far;
                if (k <= 0.0001f) continue;
                float range = Mathf.Max(2f, Mathf.Max(lib.haloRadius * 1.4f, far > 0.0001f ? lib.farGlowRange : 0f));
                float hx = g.round ? g.radius : g.halfW, hz = g.round ? g.radius : g.halfL;
                bool longX = hx >= hz;
                var dirs = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
                foreach (var d in dirs)
                {
                    bool alongX = Mathf.Abs(d.x) > 0.5f;
                    float edge = alongX ? hx : hz;                 // patch edge in this direction
                    float side = alongX ? hz : hx;                 // half length of that edge
                    Vector3 sideAxis = alongX ? Vector3.forward : Vector3.right;
                    // a long edge gets a row of spots so the light on the wall keeps the shape of the grate
                    float half = (alongX != longX) && !g.round ? Mathf.Max(0f, side - 0.25f) : 0f;
                    int n = Mathf.Max(1, Mathf.CeilToInt(2f * half / 0.6f) + 1);
                    var aim = Quaternion.LookRotation((d + Vector3.up * 0.7f).normalized, Vector3.up);
                    for (int i = 0; i < n; i++)
                    {
                        float t = n == 1 ? 0f : -half + 2f * half * i / (n - 1);
                        var at = s.patchCentre + d * edge + sideAxis * t + Vector3.up * 0.05f;
                        var l = Proxy(s.transform, at, aim, LightType.Spot);
                        l.spotAngle = 120f;
                        l.innerSpotAngle = 60f;
                        l.range = range;
                        l.intensity = k / n;
                        l.color = s.lightColor;
                        l.shapeRadius = Mathf.Clamp(Mathf.Min(edge, 0.4f), 0.1f, 0.4f);
                        made++;
                    }
                }
            }
            return made;
        }

        static Light Proxy(Transform parent, Vector3 localPos, Quaternion localRot, LightType type)
        {
            var go = new GameObject(ProxyName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var l = go.AddComponent<Light>();
            l.type = type;
            l.lightmapBakeType = LightmapBakeType.Baked;
            l.shadows = LightShadows.Soft;
            l.bounceIntensity = 1f;
            return l;
        }

        static void RemoveProxies(Scene scene)
        {
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
                if (l != null && l.gameObject.name == ProxyName && l.gameObject.scene == scene) UnityEngine.Object.DestroyImmediate(l.gameObject);
        }

        // ------------------------------------------------------------------ what the bake depends on

        static int R(float v) { return Mathf.RoundToInt(v * 100f); }

        static int Mix(int k)
        {
            unchecked
            {
                uint x = (uint)k;
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return (int)x;
            }
        }

        static int ComputeHash(Scene scene)
        {
            unchecked
            {
                int sum = 0;
                foreach (var s in ShaftRuntime.All)
                {
                    if (s == null || s.gameObject.scene != scene) continue;
                    var p = s.transform.position;
                    var q = s.transform.rotation;
                    int k = R(p.x) * 73856093 ^ R(p.y) * 19349663 ^ R(p.z) * 83492791;
                    k = k * 31 + Mathf.RoundToInt(q.x * 1000f) * 7 + Mathf.RoundToInt(q.y * 1000f) * 13 + Mathf.RoundToInt(q.z * 1000f) * 17 + Mathf.RoundToInt(q.w * 1000f);
                    k = k * 31 + s.geom.Hash();
                    k = k * 31 + Mathf.RoundToInt(s.brightness * 1000f);
                    k = k * 31 + Mathf.RoundToInt(s.bounce * 1000f);
                    k = k * 31 + s.tint.GetHashCode();
                    k = k * 31 + (s.canFlicker ? 1 : 2);
                    sum += Mix(k);
                }
                foreach (var r in CollectGI(scene, null))
                {
                    var b = r.bounds;
                    int k = R(b.center.x) * 73856093 ^ R(b.center.y) * 19349663 ^ R(b.center.z) * 83492791;
                    k = k * 31 + R(b.size.x) * 7 + R(b.size.y) * 13 + R(b.size.z) * 17;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) k = k * 31 + mf.sharedMesh.vertexCount;
                    sum += Mix(k);
                }
                int h = TunnelTypeLibrary.Current.BakeHash() * 31 + sum;
                return h == 0 ? 1 : h;
            }
        }
    }
}
