using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>
    /// Eye adaptation for daylight in the tunnels, without any code on the player or the camera:
    /// before every camera renders, it looks at the exits and shafts near the camera (and whether the camera looks at them)
    /// and fades one global volume (TunnelTypes → Exit Exposure, lower exposure) in or out over ~1 s.
    /// Standing in the light or looking into it: the picture darkens, the tunnel beyond the light reads as black.
    /// Standing in the dark facing the light: only a little darker, the lit walls between you and the light stay visible.
    /// </summary>
    public static class TunnelEyeAdaptation
    {
        static bool hooked;
        static Volume volume;
        static readonly Dictionary<Camera, float> level = new Dictionary<Camera, float>();
        static readonly Dictionary<Camera, float> lastTime = new Dictionary<Camera, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RuntimeInit() { Hook(); }
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorInit() { UnityEditor.EditorApplication.delayCall += Hook; }
#endif
        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
        }

        static void BeforeCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            bool any = TunnelExit.Active.Count > 0 || ShaftRuntime.All.Count > 0;
            var lib = TunnelTypeLibrary.Current;
            if (!any || lib == null || !lib.eyeAdaptation || lib.exitExposure == null)
            {
                if (volume != null) volume.weight = 0f;
                return;
            }
            float target = Glare(cam, lib);
            var id = cam;
            float now = Time.realtimeSinceStartup;
            float w, t0;
            if (!level.TryGetValue(id, out w) || !lastTime.TryGetValue(id, out t0)) { w = target; t0 = now; }
            float dt = Mathf.Clamp(now - t0, 0f, 0.5f);
            w = Mathf.Lerp(w, target, 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, lib.eyeAdaptTime) * 2.3f));
            level[id] = w;
            lastTime[id] = now;
            var v = GetVolume(lib.exitExposure);
            v.weight = Mathf.Clamp01(w * lib.eyeAdaptStrength);
        }

        /// <summary>0 = deep in the dark, 1 = in the daylight or staring into it.</summary>
        static float Glare(Camera cam, TunnelTypeLibrary lib)
        {
            var pos = cam.transform.position;
            var fwd = cam.transform.forward;
            float keep = 1f;   // product of (1 - glare of each source)
            foreach (var e in TunnelExit.Active)
            {
                if (e == null || !e.isActiveAndEnabled || e.brightness <= 0.001f) continue;
                var p = e.transform.InverseTransformPoint(pos);          // z < 0: inside the tunnel
                float depth = Mathf.Max(2f, e.exposureDepth);
                if (p.z > 4f || p.z < -depth * 1.5f) continue;
                var mouth = e.transform.TransformPoint(new Vector3(0f, e.height * 0.5f, 0f));
                if (Blocked(pos, mouth)) continue;
                float d = Mathf.Max(0f, -p.z);
                float near = 1f - Smooth(1f, depth, d);
                float look = Smooth(-0.1f, 0.85f, Vector3.Dot(fwd, (mouth - pos).normalized));
                float g = near * Mathf.Lerp(0.35f, 1f, look);
                if (d < 1.5f) g = Mathf.Max(g, 0.9f);                 // standing in the light: dazzled whichever way you look
                keep *= 1f - Mathf.Clamp01(g * Mathf.Min(1f, e.brightness));
            }
            float reach = Mathf.Max(1f, lib.shaftReach);
            foreach (var s in ShaftRuntime.All)
            {
                if (s == null || !s.isActiveAndEnabled || s.glare <= 0.001f) continue;
                var src = s.transform.TransformPoint(s.openingLocal);
                var to = src - pos;
                float r = new Vector2(to.x, to.z).magnitude;
                if (r > reach) continue;
                if (Blocked(pos, src - Vector3.up * 0.1f)) continue;
                float near = 1f - Smooth(0.6f, reach * 0.7f, r);
                float look = Smooth(0.2f, 0.9f, Vector3.Dot(fwd, to.normalized));
                float g = near * Mathf.Lerp(0.3f, 1f, look);
                keep *= 1f - Mathf.Clamp01(g * s.glare * lib.grateEyeAdapt);   // a grate is a small light: the eye darkens far less than at an exit
            }
            return 1f - keep;
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Is there a tunnel wall between the camera and the light? (the tunnels have mesh colliders)</summary>
        static bool Blocked(Vector3 from, Vector3 to)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len < 0.6f) return false;
            return Physics.Raycast(from, d / len, len - 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        static Volume GetVolume(VolumeProfile profile)
        {
            if (volume == null)
            {
                // a left-over from before a script reload would keep its last weight: remove it
                foreach (var old in Resources.FindObjectsOfTypeAll<Volume>())
                    if (old != null && old.gameObject.name == "_CarX_EyeAdaptation") Object.DestroyImmediate(old.gameObject);
                var go = new GameObject("_CarX_EyeAdaptation") { hideFlags = HideFlags.HideAndDontSave };
                volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 20f;
            }
            if (volume.sharedProfile != profile) volume.sharedProfile = profile;
            return volume;
        }
    }
}
