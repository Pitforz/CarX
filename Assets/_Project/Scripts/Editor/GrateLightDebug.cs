using System.Globalization;
using CarX.Level;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarX.EditorTools
{
    /// <summary>Helpers for tuning the grate light through the remote bridge: scene state and where the grates are.</summary>
    static class GrateLightDebug
    {
        static void Log(string s) { Debug.Log("CarX " + s); }
        static string F(float v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }
        static string V(Vector3 v) { return F(v.x) + " " + F(v.y) + " " + F(v.z); }

        public static void Info()
        {
            var sc = SceneManager.GetActiveScene();
            Log("scene=" + sc.path + " dirty=" + sc.isDirty + " playing=" + EditorApplication.isPlaying +
                " grates=" + ShaftRuntime.All.Count + " baking=" + Lightmapping.isRunning);
        }

        public static void Scene()
        {
            Log("ambient mode=" + RenderSettings.ambientMode + " sky=" + RenderSettings.ambientSkyColor + " int=" + F(RenderSettings.ambientIntensity) +
                " skybox=" + (RenderSettings.skybox != null ? RenderSettings.skybox.name : "none") + " sun=" + (RenderSettings.sun != null ? RenderSettings.sun.name : "none") +
                " fog=" + RenderSettings.fog + " lightingSettings=" + (Lightmapping.TryGetLightingSettings(out var ls) ? ls.name : "none"));
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (l.GetComponentInParent<ShaftRuntime>() != null) continue;
                Log("light " + l.name + " parent=" + (l.transform.parent != null ? l.transform.parent.name : "-") + " " + l.type + " mode=" + l.lightmapBakeType +
                    " I=" + F(l.intensity) + " enabled=" + (l.enabled && l.gameObject.activeInHierarchy) + " shadows=" + l.shadows + " pos " + V(l.transform.position));
            }
            foreach (var pv in Object.FindObjectsByType<UnityEngine.Rendering.ProbeVolume>(FindObjectsInactive.Include))
                Log("probevolume " + pv.name + " global=" + pv.mode);
        }

        /// <summary>The Game view as the player sees it (Play mode only).</summary>
        public static void PlayShot(string name)
        {
            System.IO.Directory.CreateDirectory("Logs/shots");
            ScreenCapture.CaptureScreenshot("Logs/shots/" + name + ".png");
            Log("play shot requested " + name);
        }

        public static void CamInfo()
        {
            var cam = Camera.main;
            if (cam == null) { Log("no main camera"); return; }
            var t = cam.transform;
            Log("cam " + V(t.position) + " yaw " + F(t.eulerAngles.y) + " pitch " + F(t.eulerAngles.x) + " fov " + F(cam.fieldOfView));
        }

        public static void StopPlay() { EditorApplication.isPlaying = false; Log("play stopped"); }

        public static void Grates()
        {
            for (int i = 0; i < ShaftRuntime.All.Count; i++)
            {
                var s = ShaftRuntime.All[i];
                if (s == null) continue;
                var t = s.transform;
                var sun = s.sun;
                float yaw = Mathf.Atan2(t.forward.x, t.forward.z) * Mathf.Rad2Deg;
                Log("grate " + i + " " + s.name + " " + s.geom.type + (s.canFlicker ? " flicker" : "") +
                    " patch " + V(t.TransformPoint(s.patchCentre)) + " opening " + V(t.TransformPoint(new Vector3(0f, s.geom.ceilY, 0f))) +
                    " yaw " + F(yaw) + " floor " + F(t.TransformPoint(new Vector3(0f, s.geom.floorY, 0f)).y) +
                    " sun " + (sun != null ? (sun.enabled ? "on" : "off") + " I " + F(sun.intensity) + " range " + F(sun.range) +
                    " angle " + F(sun.spotAngle) + " shadows " + sun.shadows + " cookie " + (sun.cookie != null ? sun.cookie.width + "x" + sun.cookie.height : "none") : "none"));
            }
        }
    }
}
