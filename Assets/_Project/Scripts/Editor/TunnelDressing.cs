using CarX.Level;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>
    /// Scatters puddles along a tunnel: puddles collect in low, flat places,
    /// slopes stay almost dry, sand piles up near the walls (or as islands in a wet channel).
    /// Everything it creates is ordinary objects under "_Dressing": move, scale or delete them freely.
    /// </summary>
    public static class TunnelDressing
    {
        public static void Clear(TunnelPath path, bool undo)
        {
            var d = path.transform.Find(TunnelPath.DressingName);
            if (d == null) return;
            if (undo) Undo.DestroyObjectImmediate(d.gameObject); else Object.DestroyImmediate(d.gameObject);
        }

        static float Range(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }

        static float MidY(TunnelPath.Slab s) { return (s.a.y + s.b.y) * 0.5f; }

        public static void Scatter(TunnelPath path, int seed, bool undo)
        {
            if (path.slabs.Count == 0) path.Rebuild(false);
            if (path.slabs.Count == 0) return;
            Clear(path, undo);
            var root = new GameObject(TunnelPath.DressingName);
            if (undo) Undo.RegisterCreatedObjectUndo(root, "Scatter puddles");
            root.transform.SetParent(path.transform, false);
            var rnd = new System.Random(seed);
            var sec = path.section;
            bool curbs = sec.profile == TunnelProfile.BoxWithCurbs;
            float half = sec.FloorHalfWidth;
            int n = path.slabs.Count;
            for (int k = 0; k < n; k++)
            {
                var sl = path.slabs[k];
                float len = sl.length - sl.tStart - sl.tEnd;
                if (len < 0.6f) continue;
                float slope = Mathf.Abs(Mathf.Asin(Mathf.Clamp(sl.fwd.y, -1f, 1f)) * Mathf.Rad2Deg);
                bool flat = slope < 1.2f;
                float y = MidY(sl);
                bool lowL = k == 0 || MidY(path.slabs[k - 1]) >= y - 0.002f;
                bool lowR = k == n - 1 || MidY(path.slabs[k + 1]) >= y - 0.002f;
                bool valley = lowL && lowR && ((k > 0 && MidY(path.slabs[k - 1]) > y + 0.01f) || (k < n - 1 && MidY(path.slabs[k + 1]) > y + 0.01f));

                double pPuddle = !flat ? 0.03 : (valley ? 0.9 : 0.32);
                if (curbs) pPuddle *= 0.35;
                if (rnd.NextDouble() < pPuddle)
                {
                    float sx, x;
                    if (curbs)
                    {
                        float cw = Mathf.Clamp(sec.curbWidth, 0.1f, sec.width * 0.4f);
                        sx = cw * Range(rnd, 0.6f, 0.85f);
                        x = (rnd.NextDouble() < 0.5 ? -1f : 1f) * (sec.width * 0.5f - cw * 0.5f);
                    }
                    else
                    {
                        sx = Range(rnd, 0.6f, Mathf.Min(2f * half - 0.1f, valley ? 2.2f : 1.6f));
                        x = Range(rnd, -(half - sx * 0.45f), half - sx * 0.45f) * 0.8f;
                    }
                    float sz = Range(rnd, 0.8f, Mathf.Min(len * (valley ? 1.3f : 0.9f), valley ? 3f : 2f));
                    float s = sl.tStart + len * Range(rnd, 0.3f, 0.7f);
                    var p = Make<Puddle>(root.transform, "Puddle", sl, s, x, sec.FloorHeightAt(x) + 0.012f, undo);
                    p.size = new Vector2(sx, sz);
                    p.seed = rnd.Next(1, 99999);
                    p.materials = path.materials;
                    p.Build();
                }
            }
        }

        /// <summary>Removes the old random sand heaps (they looked bad) from every loaded scene. Undo-able.</summary>
        [MenuItem("CarX/Tunnels/Remove Sand Piles (open scenes)", priority = 41)]
        public static void RemoveSandPiles()
        {
            int n = 0;
            foreach (var sp in Object.FindObjectsByType<SandPile>(FindObjectsInactive.Include))
            {
                Undo.DestroyObjectImmediate(sp.gameObject);
                n++;
            }
            Debug.Log("CarX: removed sand piles: " + n);
        }

        /// <summary>For the remote bridge: open a scene additively, remove sand piles, save, close. Skips a loaded dirty scene.</summary>
        public static void RemoveSandPilesInScene(string scenePath)
        {
            var open = UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(scenePath);
            bool wasLoaded = open.IsValid() && open.isLoaded;
            if (wasLoaded && open.isDirty) { Debug.LogWarning("CarX: scene has unsaved changes, sand not removed: " + scenePath); return; }
            var scene = wasLoaded ? open : UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var sp in root.GetComponentsInChildren<SandPile>(true)) { Object.DestroyImmediate(sp.gameObject); n++; }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            if (!wasLoaded) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            Debug.Log("CarX: removed " + n + " sand piles from " + scenePath);
        }

        public static void RemoveSandPilesFromMazeDemo() { RemoveSandPilesInScene("Assets/_Project/Scenes/TunnelMaze_Demo.unity"); }

        static T Make<T>(Transform parent, string name, TunnelPath.Slab sl, float s, float x, float y, bool undo) where T : Component
        {
            var go = new GameObject(name);
            if (undo) Undo.RegisterCreatedObjectUndo(go, "Scatter");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = sl.Point(s, x, y);
            var flatFwd = new Vector3(sl.fwd.x, 0f, sl.fwd.z);
            if (flatFwd.sqrMagnitude < 1e-4f) flatFwd = Vector3.forward;
            go.transform.localRotation = Quaternion.LookRotation(flatFwd.normalized, Vector3.up);
            return go.AddComponent<T>();
        }
    }
}
