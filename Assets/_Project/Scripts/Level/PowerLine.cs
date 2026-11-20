using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>
    /// Wires between the poles that are children of this object (in hierarchy order). Each pole marks its wire points with
    /// children named "Attach..." (the PowerPole prefab has three). Move, add (Ctrl+D) or delete poles: the wires follow.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("CarX/Power Line")]
    public class PowerLine : MonoBehaviour
    {
        public Material wireMaterial;
        [Tooltip("Провис провода в середине пролёта, доля длины пролёта")]
        [Range(0f, 0.1f)] public float sag = 0.025f;
        [Range(0.004f, 0.05f)] public float wireRadius = 0.012f;
        [Tooltip("Не тянуть провод, если опоры дальше друг от друга, м")]
        public float maxSpan = 90f;

        [System.NonSerialized] int builtHash;
        [System.NonSerialized] Mesh mesh;

        void OnEnable() { builtHash = 0; Rebuild(); }
        void OnDisable() { if (mesh != null) { DestroyImmediate(mesh); mesh = null; } }
        void OnValidate() { builtHash = 0; }
        void Update() { Rebuild(); }

        List<List<Transform>> Poles()
        {
            var list = new List<List<Transform>>();
            for (int i = 0; i < transform.childCount; i++)
            {
                var pole = transform.GetChild(i);
                if (!pole.gameObject.activeInHierarchy || pole.name == "_Wires") continue;
                var pts = new List<Transform>();
                foreach (var t in pole.GetComponentsInChildren<Transform>())
                    if (t.name.StartsWith("Attach")) pts.Add(t);
                pts.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                if (pts.Count > 0) list.Add(pts);
            }
            return list;
        }

        void Rebuild()
        {
            var poles = Poles();
            int h = 17;
            unchecked
            {
                foreach (var p in poles) foreach (var t in p) h = h * 31 + t.position.GetHashCode();
                h = h * 31 + sag.GetHashCode() + wireRadius.GetHashCode() * 7 + (wireMaterial != null ? wireMaterial.GetHashCode() : 0);
                h = h * 31 + transform.localToWorldMatrix.GetHashCode();
            }
            if (h == builtHash) return;
            builtHash = h;

            var wires = transform.Find("_Wires");
            if (wires == null)
            {
                var go = new GameObject("_Wires");
                go.transform.SetParent(transform, false);
                wires = go.transform;
            }
            wires.localPosition = Vector3.zero; wires.localRotation = Quaternion.identity; wires.localScale = Vector3.one;
            var mf = wires.GetComponent<MeshFilter>();
            if (mf == null) mf = wires.gameObject.AddComponent<MeshFilter>();
            var mr = wires.GetComponent<MeshRenderer>();
            if (mr == null) mr = wires.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = wireMaterial;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            if (mesh == null) mesh = new Mesh { name = "PowerLineWires", hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
            mesh.Clear();
            var kit = new MeshKit(1);
            const int segs = 14;
            for (int i = 0; i + 1 < poles.Count; i++)
            {
                var a = poles[i]; var b = poles[i + 1];
                int n = Mathf.Min(a.Count, b.Count);
                for (int k = 0; k < n; k++)
                {
                    Vector3 p0 = transform.InverseTransformPoint(a[k].position), p1 = transform.InverseTransformPoint(b[k].position);
                    float span = Vector3.Distance(p0, p1);
                    if (span < 0.5f || span > maxSpan) continue;
                    Vector3 prev = p0;
                    for (int s = 1; s <= segs; s++)
                    {
                        float t = (float)s / segs;
                        var q = Vector3.Lerp(p0, p1, t) + Vector3.down * (4f * sag * span * t * (1f - t));
                        kit.Tube(0, prev, q, wireRadius, wireRadius, 4, true, 1f);
                        prev = q;
                    }
                }
            }
            kit.Fill(mesh);
            mf.sharedMesh = mesh;
        }
    }
}
