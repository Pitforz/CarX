using System.Collections.Generic;
using System.IO;
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
    /// Look development against the reference photos: straight tunnels of both Vegas types with a daylight exit
    /// at the far end, and cameras placed like in the photos. Shots go to Logs/shots/look_*.png.
    /// </summary>
    public static class TunnelLookDev
    {
        public const string ScenePath = "Assets/_Project/Scenes/Test/TunnelLook_Test.unity";
        static readonly Vector3 Origin = new Vector3(3000f, 0f, 3000f);
        const float SecondX = 400f;   // far apart: one exit light in view at a time

        [MenuItem("CarX/Tunnels/Look Test Scene (reference photos)", priority = 60)]
        static void Menu()
        {
            var scene = Build();
            SceneManager.SetActiveScene(scene);
            var view = SceneView.lastActiveSceneView;
            if (view != null) { view.orthographic = false; view.LookAt(Origin + new Vector3(0, 1.3f, 8f), Quaternion.identity, 4f); }
        }

        public static Scene Build()
        {
            var lib = TunnelGraphAssets.GetLibrary();
            TunnelAssets.EnsureFolder("Assets/_Project/Scenes/Test");
            var prev = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded) foreach (var r in scene.GetRootGameObjects()) Object.DestroyImmediate(r);
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            SceneManager.SetActiveScene(scene);
            TunnelAssets.ApplyAtmosphere();
            var big = AssetDatabase.LoadAssetAtPath<TunnelType>(TunnelGraphAssets.Dir + "/Types/Octagon.asset");
            var small = AssetDatabase.LoadAssetAtPath<TunnelType>(TunnelGraphAssets.Dir + "/Types/Vegas10x6.asset");
            Tunnel(lib, "Look_12x8", Origin, big);
            Tunnel(lib, "Look_10x6", Origin + new Vector3(SecondX, 0f, 0f), small != null ? small : big);
            EditorSceneManager.SaveScene(scene);
            if (prev.IsValid() && prev.isLoaded && prev != scene) SceneManager.SetActiveScene(prev);
            return scene;
        }

        static void Tunnel(TunnelTypeLibrary lib, string name, Vector3 at, TunnelType type)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            var net = go.AddComponent<TunnelNetwork>();
            net.library = lib;
            var a = net.AddNode(new Vector3(0, 0, -20f));
            var b = net.AddNode(new Vector3(0, 0, 60f));
            b.kind = GraphNodeKind.Exit;
            net.AddEdge(a.id, b.id, type);
            net.Build();
        }

        /// <summary>Tuning: the same shots with other light numbers (library values are changed and saved).</summary>
        public static string ShotsWith(float intensity, float range, float fog, bool shadows)
        {
            var lib = TunnelGraphAssets.GetLibrary();
            lib.exitShadows = shadows;
            lib.exitLightIntensity = intensity;
            lib.exitLightRange = range;
            lib.fogDensity = fog;
            EditorUtility.SetDirty(lib);
            return Shots() + " (light " + intensity + ", range " + range + ", fog " + fog + ")";
        }

        public static string ShadowDistance(float d)
        {
            var a = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            var q = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            string s = "";
            foreach (var x in new[] { a, q })
                if (x != null) { s += x.name + " " + x.shadowDistance + " -> " + d + "; "; x.shadowDistance = d; }
            return s;
        }

        /// <summary>Debug: raw HDR colour (no post effects) at a few screen points, camera on the 12×8 axis at z.</summary>
        public static string ProbeHdr(float z, int post)
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene = Build();
            var prev = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            const int w = 300, h = 200;
            var go = new GameObject("_ProbeCamera") { hideFlags = HideFlags.HideAndDontSave };
            string s = "z " + z + ":";
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.transform.SetPositionAndRotation(Origin + new Vector3(0f, 1.6f, z), Quaternion.identity);
                cam.fieldOfView = 60f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 400f;
                TunnelAssets.SetupCamera(cam);
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = post != 0;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf);
                rt.Create();
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
                var prevRt = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prevRt;
                foreach (var px in new[] { 150, 100, 60, 30 })
                {
                    var c = tex.GetPixel(px, h / 2);
                    s += " x" + px + "=" + c.r.ToString("0.000") + "/" + c.b.ToString("0.000");
                }
                Object.DestroyImmediate(tex);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (prev.IsValid() && prev.isLoaded && prev != scene) SceneManager.SetActiveScene(prev);
            }
            return s;
        }

        /// <summary>Debug: one view down the 12×8 tunnel from z (0 = 60 m from the exit), scene as it is.</summary>
        public static string ShotAt(string name, float z, float y, float yaw)
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene = Build();
            var prev = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            var off = new List<Light>();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.gameObject.scene != scene && l.enabled) { l.enabled = false; off.Add(l); }
            try { Shot(name, Origin + new Vector3(0f, y, z), yaw, 0f, 60f); }
            finally
            {
                foreach (var l in off) if (l != null) l.enabled = true;
                if (prev.IsValid() && prev.isLoaded && prev != scene) SceneManager.SetActiveScene(prev);
            }
            return "shot " + name;
        }

        /// <summary>Builds the scene and renders the photo-like views.</summary>
        public static string Shots()
        {
            // reuse the open scene: lights made in this very frame are not rendered with shadows yet
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene = Build();
            else
                foreach (var r in scene.GetRootGameObjects())
                    foreach (var net in r.GetComponentsInChildren<TunnelNetwork>()) net.Build();
            var prev = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            TunnelAssets.ApplyAtmosphere();
            // lights of other open scenes must not leak in
            var off = new List<Light>();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.gameObject.scene != scene && l.enabled) { l.enabled = false; off.Add(l); }
            try
            {
                Shot("_warmup", Origin + new Vector3(0f, 1.25f, 0f), 0f, 0f, 52f);
                // photo 1: on the axis, half height, looking straight at the light
                Shot("look_1_12x8", Origin + new Vector3(0f, 1.25f, 0f), 0f, 0f, 52f);
                // photo 2: a little lower and to the side, looking slightly down the tunnel
                Shot("look_2_12x8", Origin + new Vector3(0.35f, 1.45f, 2f), -2f, 9f, 68f);
                Shot("look_1_10x6", Origin + new Vector3(SecondX, 0.95f, 0f), 0f, 0f, 52f);
                Shot("look_2_10x6", Origin + new Vector3(SecondX + 0.3f, 1.1f, 2f), -2f, 9f, 68f);
                // walking towards the exit (local exposure) and with the flashlight
                Shot("look_3_near_exit", Origin + new Vector3(0f, 1.6f, 50f), 0f, 0f, 60f);
                Shot("look_4_flashlight", Origin + new Vector3(0f, 1.6f, -10f), 180f, 5f, 60f, true);
            }
            finally
            {
                foreach (var l in off) if (l != null) l.enabled = true;
                if (prev.IsValid() && prev.isLoaded && prev != scene) SceneManager.SetActiveScene(prev);
            }
            return "CarX look shots saved to Logs/shots/look_*.png";
        }

        static void Shot(string name, Vector3 pos, float yaw, float pitch, float fov, bool flashlight = false)
        {
            const int w = 1200, h = 800;
            var go = new GameObject("_LookCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, 0f));
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 2000f;
            TunnelAssets.SetupCamera(cam);
            if (flashlight) TunnelMazeBuilder.AddFlashlight(go.transform);
            // half-float target: URP renders a camera with a target texture in that texture's format,
            // and an 8-bit target would clip the daylight to 1 before exposure and bloom (unlike the game view)
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf);
            rt.Create();
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var ldr = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(rt, ldr);
            var prevRt = RenderTexture.active;
            RenderTexture.active = ldr;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevRt;
            RenderTexture.ReleaseTemporary(ldr);
            Directory.CreateDirectory("Logs/shots");
            File.WriteAllBytes("Logs/shots/" + name + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }
}
