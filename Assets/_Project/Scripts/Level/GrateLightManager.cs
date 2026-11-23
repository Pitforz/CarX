using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>
    /// Live shadows cost a shadow map each: only the grates nearest to the camera (and in view) get them, so NPCs and the
    /// player cast shadows there. The other grates light without live shadows: their cookie keeps the light inside the
    /// opening and its range ends under the floor, so it does not leak through walls.
    /// Also removes the player's shadow in Play mode when TunnelTypes says so.
    /// </summary>
    public static class GrateLightManager
    {
        static bool hooked;
        static readonly Plane[] planes = new Plane[6];
        static readonly List<ShaftRuntime> chosen = new List<ShaftRuntime>();
        static readonly List<float> chosenD = new List<float>();
        static readonly Dictionary<Renderer, ShadowCastingMode> playerOriginal = new Dictionary<Renderer, ShadowCastingMode>();
        static float nextPlayerScan;

        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += OnCamera;
        }

        static void OnCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null || (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView)) return;
            var lib = TunnelTypeLibrary.Current;
            if (Application.isPlaying && cam.cameraType == CameraType.Game && Time.unscaledTime >= nextPlayerScan)
            {
                nextPlayerScan = Time.unscaledTime + 1f;
                PlayerShadow(lib.playerShadow);
            }
            var all = ShaftRuntime.All;
            if (all.Count == 0) return;

            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            var camPos = cam.transform.position;
            float maxD = Mathf.Max(1f, lib.realtimeShadowDistance);
            int maxN = Mathf.Max(0, lib.realtimeShadowGrates);
            chosen.Clear();
            chosenD.Clear();
            foreach (var s in all)
            {
                if (s == null || s.sun == null) continue;
                var wb = WorldBounds(s);
                float d = Mathf.Sqrt(wb.SqrDistance(camPos));
                bool near = d <= maxD;
                // the live bounce of a flickering opening has no baked shadows to stop it: only near the camera
                if (s.bounceLight != null)
                {
                    bool on = near && s.lit > 0.001f && s.bounceLight.intensity > 0.0001f;
                    if (s.bounceLight.enabled != on) s.bounceLight.enabled = on;
                }
                if (!near || !s.sun.enabled || maxN == 0 || !GeometryUtility.TestPlanesAABB(planes, wb)) continue;
                int at = chosenD.Count;
                while (at > 0 && chosenD[at - 1] > d) at--;
                if (at >= maxN) continue;
                chosen.Insert(at, s);
                chosenD.Insert(at, d);
                if (chosen.Count > maxN)
                {
                    chosen.RemoveAt(maxN);
                    chosenD.RemoveAt(maxN);
                }
            }
            foreach (var s in all)
            {
                if (s == null || s.sun == null) continue;
                var want = chosen.Contains(s) ? LightShadows.Soft : LightShadows.None;
                if (s.sun.shadows != want) s.sun.shadows = want;
            }
        }

        static readonly Vector3[] corners = new Vector3[8];

        static Bounds WorldBounds(ShaftRuntime s)
        {
            var b = s.lightBounds;
            var t = s.transform;
            var mn = b.min;
            var mx = b.max;
            corners[0] = new Vector3(mn.x, mn.y, mn.z);
            corners[1] = new Vector3(mx.x, mn.y, mn.z);
            corners[2] = new Vector3(mn.x, mx.y, mn.z);
            corners[3] = new Vector3(mx.x, mx.y, mn.z);
            corners[4] = new Vector3(mn.x, mn.y, mx.z);
            corners[5] = new Vector3(mx.x, mn.y, mx.z);
            corners[6] = new Vector3(mn.x, mx.y, mx.z);
            corners[7] = new Vector3(mx.x, mx.y, mx.z);
            var w = new Bounds(t.TransformPoint(corners[0]), Vector3.zero);
            for (int i = 1; i < 8; i++) w.Encapsulate(t.TransformPoint(corners[i]));
            return w;
        }

        /// <summary>Objects tagged Player: their renderers stop casting shadows (on = back to how they were made).</summary>
        static void PlayerShadow(bool on)
        {
            var dead = new List<Renderer>();
            foreach (var kv in playerOriginal) if (kv.Key == null) dead.Add(kv.Key);
            foreach (var r in dead) playerOriginal.Remove(r);
            GameObject[] players;
            try { players = GameObject.FindGameObjectsWithTag("Player"); }
            catch (UnityException) { return; }
            foreach (var p in players)
            {
                if (p == null) continue;
                foreach (var r in p.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    ShadowCastingMode orig;
                    if (!playerOriginal.TryGetValue(r, out orig))
                    {
                        orig = r.shadowCastingMode;
                        playerOriginal[r] = orig;
                    }
                    var want = on ? orig : ShadowCastingMode.Off;
                    if (r.shadowCastingMode != want) r.shadowCastingMode = want;
                }
            }
        }
    }
}
