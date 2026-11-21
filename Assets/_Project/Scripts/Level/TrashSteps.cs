using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CarX.Level
{
    public enum TrashKind { [InspectorName("Куча")] Heap, [InspectorName("Куча из-под пола")] Pile, [InspectorName("Мелочь")] Litter }

    /// <summary>One step zone of placed trash: a box around a heap or a piece of litter (group space).</summary>
    [Serializable]
    public struct TrashStepBox
    {
        public Vector3 center;
        public Quaternion rotation;
        public Vector3 size;
        public TrashKind kind;
    }

    /// <summary>What happened: who walks through which trash, and where.</summary>
    public struct TrashStep
    {
        public GameObject walker;
        public TrashKind kind;
        public Vector3 point;
        public TunnelTrashGroup group;
        public int box;
    }

    /// <summary>Marks anything else that walks (a player or NPC without CharacterController or NavMeshAgent),
    /// so that its steps through trash are reported too.</summary>
    [AddComponentMenu("CarX/Trash Walker")]
    public class TrashWalker : MonoBehaviour
    {
        [Tooltip("Точка ступней относительно объекта")]
        public Vector3 feet;
    }

    /// <summary>Trash has no colliders, so nothing physical happens when someone walks through it. This tracker
    /// (play mode only, created by itself) checks the feet of every walker against the trash step zones and raises
    /// events, e.g. for rustling sounds:
    ///   TrashSteps.Entered / Exited  - a walker came into / left a heap or a piece of litter;
    ///   TrashSteps.Stepped           - every StepDistance metres walked inside trash;
    ///   TrashSteps.At(point, out k)  - is this point on trash (for a footstep system).
    /// Walkers: everything with a CharacterController (the player), a NavMeshAgent (NPCs) or a TrashWalker.</summary>
    public class TrashSteps : MonoBehaviour
    {
        // ---- tunables (static so that sound code can change them; the inspector of the runtime object shows them too)
        [Tooltip("Как часто проверять, с")] public float interval = 0.05f;
        [Tooltip("Событие Stepped каждые столько метров внутри мусора")] public float stepDistance = 0.6f;
        [Tooltip("Как часто искать новых ходоков (появившихся NPC), с")] public float rescan = 1f;
        [Tooltip("Писать события в консоль (для проверки)")] public bool log;

        public static event Action<TrashStep> Entered, Exited, Stepped;

        static readonly List<TunnelTrashGroup> groups = new List<TunnelTrashGroup>();
        static TrashSteps instance;

        class Walker
        {
            public Component src;
            public TunnelTrashGroup group;
            public int box = -1;
            public Vector3 last;
            public float walked;
        }
        readonly Dictionary<Component, Walker> walkers = new Dictionary<Component, Walker>();
        float nextCheck, nextScan;

        public static TrashSteps Instance { get { return instance; } }

        public static void Register(TunnelTrashGroup g)
        {
            if (!groups.Contains(g)) groups.Add(g);
            if (instance == null && Application.isPlaying)
            {
                var go = new GameObject("_TrashSteps");
                instance = go.AddComponent<TrashSteps>();
            }
        }

        public static void Unregister(TunnelTrashGroup g) { groups.Remove(g); }

        /// <summary>Is a world point (feet) on trash; which kind.</summary>
        public static bool At(Vector3 point, out TrashKind kind)
        {
            kind = TrashKind.Litter;
            TunnelTrashGroup g; int b;
            if (!Find(point, out g, out b)) return false;
            kind = g.steps[b].kind;
            return true;
        }

        static bool Find(Vector3 point, out TunnelTrashGroup group, out int box)
        {
            foreach (var g in groups)
            {
                if (g == null) continue;
                int b = g.BoxAt(point);
                if (b >= 0) { group = g; box = b; return true; }
            }
            group = null; box = -1;
            return false;
        }

        void OnDestroy() { if (instance == this) instance = null; }

        void Update()
        {
            if (groups.Count == 0) return;
            float t = Time.time;
            if (t >= nextScan) { nextScan = t + rescan; Scan(); }
            if (t < nextCheck) return;
            nextCheck = t + interval;
            List<Component> gone = null;
            foreach (var kv in walkers)
            {
                var w = kv.Value;
                if (w.src == null || !w.src.gameObject.activeInHierarchy) { (gone ?? (gone = new List<Component>())).Add(kv.Key); continue; }
                var feet = Feet(w.src);
                TunnelTrashGroup g; int b;
                Find(feet, out g, out b);
                if (g != w.group || b != w.box)
                {
                    if (w.group != null) Raise(Exited, "вышел из", w, w.group, w.box, feet);
                    w.group = g; w.box = b; w.walked = 0f;
                    if (g != null) Raise(Entered, "вошёл в", w, g, b, feet);
                }
                else if (g != null)
                {
                    w.walked += Vector3.Distance(new Vector3(feet.x, 0f, feet.z), new Vector3(w.last.x, 0f, w.last.z));
                    if (w.walked >= stepDistance) { w.walked = 0f; Raise(Stepped, "шагнул по", w, g, b, feet); }
                }
                w.last = feet;
            }
            if (gone != null) foreach (var k in gone) walkers.Remove(k);
        }

        void Raise(Action<TrashStep> ev, string what, Walker w, TunnelTrashGroup g, int b, Vector3 feet)
        {
            var s = new TrashStep { walker = w.src.gameObject, kind = g != null && b >= 0 && b < g.steps.Count ? g.steps[b].kind : TrashKind.Litter, point = feet, group = g, box = b };
            if (log) Debug.Log("CarX TrashSteps: " + s.walker.name + " " + what + " мусор (" + s.kind + ")", s.walker);
            if (ev != null) ev(s);
        }

        void Scan()
        {
            foreach (var c in FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude)) Add(c);
            foreach (var a in FindObjectsByType<NavMeshAgent>(FindObjectsInactive.Exclude)) Add(a);
            foreach (var m in FindObjectsByType<TrashWalker>(FindObjectsInactive.Exclude)) Add(m);
        }

        void Add(Component c)
        {
            if (walkers.ContainsKey(c)) return;
            // one walker per object: a TrashWalker wins over the controller next to it
            if (!(c is TrashWalker) && c.GetComponent<TrashWalker>() != null) return;
            if (c is NavMeshAgent && c.GetComponent<CharacterController>() != null) return;
            walkers[c] = new Walker { src = c, last = Feet(c) };
        }

        static Vector3 Feet(Component c)
        {
            var cc = c as CharacterController;
            if (cc != null)
            {
                var b = cc.bounds;
                return new Vector3(b.center.x, b.min.y + 0.05f, b.center.z);
            }
            var tw = c as TrashWalker;
            if (tw != null) return c.transform.TransformPoint(tw.feet);
            return c.transform.position + Vector3.up * 0.05f;
        }
    }
}
