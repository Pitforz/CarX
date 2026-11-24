using System.Text;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarX.EditorTools
{
    /// <summary>Automation check (bridge: call CarX.EditorTools.GraffitiRayTest.Run): the spray ray must skip the tunnel wall that
    /// faces away from the viewer and land on the visible inner wall. Uses the tunnel test scene, closes it without saving.</summary>
    static class GraffitiRayTest
    {
        static int ok, bad;
        static StringBuilder log;
        static void Check(bool c, string what) { if (c) ok++; else { bad++; log.Append("\n  FAIL " + what); } }

        public static void Run()
        {
            ok = bad = 0;
            log = new StringBuilder();
            if (!System.IO.File.Exists(TunnelGraphDemo.TestPath)) TunnelGraphDemo.BuildTestScene();
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(TunnelGraphDemo.TestPath, OpenSceneMode.Additive);
            try { InScene(scene); }
            catch (System.Exception ex) { bad++; log.Append("\n  EXC " + ex); }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (active.IsValid()) SceneManager.SetActiveScene(active);
            }
            Debug.Log("CarX graffiti ray test: ok " + ok + ", bad " + bad + log);
        }

        static GameObject Picked(Scene scene, Ray ray)
        {
            // what the Scene view would pick: the first collider along the ray (it does not care which side faces it)
            RaycastHit best = default(RaycastHit);
            float d = float.MaxValue;
            foreach (var h in Physics.RaycastAll(ray, 500f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                if (h.collider.gameObject.scene == scene && h.distance < d) { d = h.distance; best = h; }
            return d < float.MaxValue ? best.collider.gameObject : null;
        }

        static void InScene(Scene scene)
        {
            TunnelNetwork net = null;
            foreach (var r in scene.GetRootGameObjects()) { net = r.GetComponentInChildren<TunnelNetwork>(); if (net != null) break; }
            Check(net != null, "network");
            if (net == null) return;
            net.EnsureLayout();
            Physics.SyncTransforms();
            GraphEdge edge = null;
            float best = 0f;
            foreach (var e in net.edges) { float len; if (net.EdgeSpan(e, out len) && len > best) { best = len; edge = e; } }
            Vector3 axis, dir;
            TunnelSection sec;
            net.EdgeFrame(edge, best * 0.4f, out axis, out dir, out sec);
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            float floorY = sec.FloorHeightAt(0f), ceilY = sec.CeilingHeightAt(0f), wallX = sec.WallXAt(1.2f);
            Vector3 centre = net.World(axis);

            // 1. camera above the tunnel, looking down: the roof is not drawn, the floor is
            Vector3 o = centre + Vector3.up * (ceilY + 6f);
            var ray = new Ray(o, Vector3.down);
            Vector3 pos, n;
            GameObject go;
            var pick = Picked(scene, ray);
            Check(pick != null, "picked something from above");
            bool hit = GraffitiWindow.Cast(ray, pick, out pos, out n, out go);
            Check(hit, "hit from above");
            Check(hit && n.y > 0.8f, "floor normal up, got " + n);
            Check(hit && Mathf.Abs(pos.y - (centre.y + floorY)) < 0.3f, "lands on the floor y " + pos.y + " want " + (centre.y + floorY));
            // the plain mesh test (what the Scene view used before) hits the roof first: the bug this fixes
            var mf = pick != null ? pick.GetComponent<MeshFilter>() : null;
            RaycastHit rh;
            Check(mf != null && GraffitiWindow.RayMesh(ray, mf, out rh) && rh.point.y > centre.y + ceilY - 0.5f, "plain mesh hit is the roof (sanity)");

            // 2. camera beside the tunnel, looking across: the near wall is skipped, the far inner wall is hit
            ray = new Ray(centre + right * (wallX + 6f) + Vector3.up * 1.2f, -right);
            hit = GraffitiWindow.Cast(ray, Picked(scene, ray), out pos, out n, out go);
            Check(hit, "hit from the side");
            Check(hit && Vector3.Dot(n, ray.direction) < -0.5f, "side normal faces the viewer, got " + n);
            Check(hit && Vector3.Dot(pos - centre, right) < -wallX + 0.4f && Vector3.Dot(pos - centre, right) > -wallX - 0.4f, "far wall, offset " + Vector3.Dot(pos - centre, right) + " want " + (-wallX));

            // 3. camera under the floor, looking up: the floor is skipped, the roof's inner side is hit
            ray = new Ray(centre + Vector3.down * 5f, Vector3.up);
            hit = GraffitiWindow.Cast(ray, Picked(scene, ray), out pos, out n, out go);
            Check(hit && n.y < -0.5f, "from below: ceiling normal down, got " + n);

            // 4. camera inside the tunnel: the near wall is hit as before
            ray = new Ray(centre + Vector3.up * 1.2f, right);
            hit = GraffitiWindow.Cast(ray, Picked(scene, ray), out pos, out n, out go);
            Check(hit && Vector3.Dot(n, ray.direction) < -0.5f && Mathf.Abs(Vector3.Dot(pos - centre, right) - wallX) < 0.4f, "from inside: wall at " + (hit ? Vector3.Dot(pos - centre, right).ToString("0.00") : "none") + " want " + wallX);

            // 5. a plain cube: its outer face is hit from outside, nothing from a ray that starts inside and leaves
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(cube, scene);
            cube.transform.position = new Vector3(0f, 1f, 400f);
            Physics.SyncTransforms();
            ray = new Ray(new Vector3(0f, 1f, 395f), Vector3.forward);
            hit = GraffitiWindow.Cast(ray, Picked(scene, ray), out pos, out n, out go);
            Check(hit && go == cube && n.z < -0.9f && Mathf.Abs(pos.z - 399.5f) < 0.01f, "cube front face");
        }
    }
}
