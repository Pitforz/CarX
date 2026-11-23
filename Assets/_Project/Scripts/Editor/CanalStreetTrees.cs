using System.Collections.Generic;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>
    /// Straight rows of pack palms and trees behind the far canal bank, so that from the tunnel mouths they read as tree-lined streets.
    /// </summary>
    public static partial class CanalEntranceBuilder
    {
        public const string StreetTreesRootName = "Деревья вдоль улиц (рядами)";

        [MenuItem("CarX/Canal/Деревья рядами вдоль улиц")]
        static void PlantStreetTreesMenu()
        {
            Debug.Log("Деревья рядами: " + PlantStreetTrees());
        }

        /// <summary>Bridge (debug): what a ray straight down hits at canal-local (x, z), top first.</summary>
        public static string GroundProbe(string sx, string sz)
        {
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c == null) return "no canal";
            Physics.SyncTransforms();
            float x = float.Parse(sx, System.Globalization.CultureInfo.InvariantCulture), z = float.Parse(sz, System.Globalization.CultureInfo.InvariantCulture);
            var origin = c.transform.TransformPoint(new Vector3(x, c.groundHeight + 40f, z));
            var hits = Physics.RaycastAll(origin, -c.transform.up, 200f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var sb = new System.Text.StringBuilder("hits " + hits.Length);
            foreach (var h in hits)
                sb.Append(" | " + h.collider.name + " (" + (h.collider.transform.parent != null ? h.collider.transform.parent.name : "-") + ") y "
                          + c.transform.InverseTransformPoint(h.point).y.ToString("F2") + " ny " + h.normal.y.ToString("F2") + " layer " + h.collider.gameObject.layer);
            var ch = c.transform.Find(CanalEntrance.ChannelRootName);
            if (ch != null)
                foreach (var mf in ch.GetComponentsInChildren<MeshFilter>())
                {
                    var r = mf.GetComponent<Renderer>();
                    if (r == null || r.bounds.max.z < z - 40f) continue;
                    sb.Append(" || " + mf.name + " z " + r.bounds.min.z.ToString("F0") + ".." + r.bounds.max.z.ToString("F0") + " x " + r.bounds.min.x.ToString("F0") + ".." + r.bounds.max.x.ToString("F0")
                              + (mf.GetComponent<Collider>() != null ? " col" : ""));
                }
            return sb.ToString();
        }

        /// <summary>Bridge: rows of trees along streets behind the far bank. Safe to run again (the old rows are replaced).</summary>
        public static string PlantStreetTrees()
        {
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c == null) return "no canal";
            Physics.SyncTransforms();
            var palms = PackPrefabs("palm");
            var trees = PackPrefabs("tree");
            if (palms.Count == 0 || trees.Count == 0) return "no pack prefabs";

            var props = GameObject.Find(PropsRootName);
            if (props == null) props = PlaceProps(c);
            var old = props.transform.Find(StreetTreesRootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            // trunk positions of the existing random trees and bounds of small obstacles (collected before the new root exists)
            var treesRoot = props.transform.Find(TreesRootName);
            var trunks = new List<Vector2>();
            if (treesRoot != null)
                foreach (Transform t in treesRoot) trunks.Add(new Vector2(t.position.x, t.position.z));
            var obstacles = new List<Bounds>();
            var channel = c.transform.Find(CanalEntrance.ChannelRootName);
            float groundY = c.transform.TransformPoint(new Vector3(0f, c.groundHeight, 0f)).y;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                if (treesRoot != null && r.transform.IsChildOf(treesRoot)) continue;
                if (channel != null && r.transform.IsChildOf(channel)) continue;   // the ground, slabs and grass of the canal itself
                var b = r.bounds;
                if (b.size.x >= 20f || b.size.z >= 20f) continue;
                if (b.max.y < groundY + 0.5f) continue;                            // flat things lying on the ground
                b.Expand(new Vector3(3f, 0f, 3f));   // 1.5 m on each side in XZ
                obstacles.Add(b);
            }

            var root = new GameObject(StreetTreesRootName);
            Undo.RegisterCreatedObjectUndo(root, "Деревья рядами");
            root.transform.SetParent(props.transform, false);

            var rnd = new System.Random(c.seed * 131 + 17);
            System.Func<float> R = () => (float)rnd.NextDouble();

            // far crest of the canal behind the bend
            float zOuter = float.MinValue;
            for (float s = 20f; s <= c.channelLength; s += 1f)
                zOuter = Mathf.Max(zOuter, c.PointAt(s, -c.ConcreteSign * c.CrestAt(s)).y);

            GameObject palm = palms[0];
            foreach (var p in palms) if (PrefabHeight(p) > PrefabHeight(palm)) palm = p;
            trees.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            GameObject tree = trees[0];

            int rows = 0, placed = 0, skipped = 0, noGround = 0, nearTree = 0, nearThing = 0;
            // one tree at (x, z) along a row: alongAxisX tells which axis the row runs along
            System.Action<GameObject, float, float, bool, float, float> put = (prefab, x, z, alongX, height, vary) =>
            {
                float along = (R() * 2f - 1f) * 0.6f, across = (R() * 2f - 1f) * 0.3f;
                x += alongX ? along : across;
                z += alongX ? across : along;
                float yaw = R() * 360f;
                float h = height * (1f + (R() * 2f - 1f) * vary);
                var g = GroundAt(c, new Vector2(x, z));
                if (g == null)
                {
                    // past the modelled ground (it ends a few metres behind the crest): from the canal only the crowns show over
                    // the bank, so the tree stands at ground height on nothing. A spot over something else is skipped
                    var top = c.transform.TransformPoint(new Vector3(x, c.groundHeight + 40f, z));
                    if (Physics.Raycast(top, -c.transform.up, 200f, ~(1 << 2), QueryTriggerInteraction.Ignore)) { skipped++; noGround++; return; }
                    g = new Vector3(x, c.groundHeight, z);
                }
                else if (Mathf.Abs(g.Value.y - c.groundHeight) > 0.6f) { skipped++; noGround++; return; }
                var world = c.transform.TransformPoint(g.Value - Vector3.up * 0.1f);
                bool blocked = false;
                foreach (var t in trunks)
                    if ((t - new Vector2(world.x, world.z)).sqrMagnitude < 16f) { blocked = true; nearTree++; break; }
                if (!blocked)
                    foreach (var b in obstacles)
                        if (world.x >= b.min.x && world.x <= b.max.x && world.z >= b.min.z && world.z <= b.max.z) { blocked = true; nearThing++; break; }
                if (blocked) { skipped++; return; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                go.transform.localPosition = g.Value - Vector3.up * 0.1f;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                go.transform.localScale = Vector3.one * (h / PrefabHeight(prefab));
                placed++;
            };

            // rows A and B: palms on both sides of a street running along X
            float zA = zOuter + 9f, zB = zOuter + 9f + 14f;
            for (float x = -95f; x <= 45f + 0.01f; x += 8f) put(palm, x, zA, true, 12f, 0.08f);
            rows++;
            for (float x = -95f + 4f; x <= 45f + 0.01f; x += 8f) put(palm, x, zB, true, 12f, 0.08f);
            rows++;
            // row C: street running away from the canal, two sides
            foreach (float x in new[] { 30f, 42f })
            {
                for (float z = zOuter + 30f; z <= zOuter + 140f + 0.01f; z += 9f) put(tree, x, z, false, 9f, 0.10f);
                rows++;
            }

            // wind shader needs real object positions: no static batching
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var f = GameObjectUtility.GetStaticEditorFlags(mr.gameObject);
                if ((f & StaticEditorFlags.BatchingStatic) != 0)
                    GameObjectUtility.SetStaticEditorFlags(mr.gameObject, f & ~StaticEditorFlags.BatchingStatic);
            }

            var oo = props.GetComponent<OutdoorObjects>();
            if (oo != null) oo.Apply();
            EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
            return "rows " + rows + ", trees " + placed + ", skipped " + skipped + " (no ground " + noGround + ", near a tree " + nearTree
                   + ", near a thing " + nearThing + "), zOuter " + zOuter.ToString("F1");
        }
    }
}
