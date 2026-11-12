using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>Washed-in sand/silt heap generated from a few numbers.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("CarX/Sand Pile")]
    public class SandPile : MonoBehaviour, IFloorDressing
    {
        [Tooltip("Размер кучи, м")]
        public Vector2 size = new Vector2(1.2f, 0.8f);
        [Tooltip("Высота, м")]
        [Range(0.03f, 0.9f)] public float height = 0.22f;
        [Tooltip("Неровность")]
        [Range(0f, 0.6f)] public float roughness = 0.25f;
        public int seed = 1;
        [Tooltip("Пусто = стандартные материалы тоннелей")]
        public TunnelMaterials materials;

        public float FloorOffset { get { return -0.015f; } }

        [NonSerialized] Mesh mesh;
        [NonSerialized] bool dirty = true;

        void OnEnable() { Build(); }

        void OnValidate()
        {
            dirty = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void Update() { if (dirty) Build(); }

        void OnDestroy() { if (mesh != null) DestroyImmediate(mesh); }

        public void Build()
        {
            dirty = false;
            var rnd = new System.Random(seed);
            const int rings = 12, segs = 40;
            float[] ph = new float[6];
            for (int i = 0; i < ph.Length; i++) ph[i] = (float)rnd.NextDouble() * 6.28f;
            Vector2 peak = new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 0.35f;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            verts.Add(new Vector3(peak.x * size.x * 0.5f, height, peak.y * size.y * 0.5f));
            uvs.Add(new Vector2(verts[0].x, verts[0].z) / 1.2f);
            for (int r = 1; r <= rings; r++)
            {
                float rr = (float)r / rings;
                for (int s = 0; s < segs; s++)
                {
                    float a = s * Mathf.PI * 2f / segs;
                    float edge = 1f + (Mathf.Sin(a * 2f + ph[0]) * 0.5f + Mathf.Sin(a * 3f + ph[1]) * 0.3f + Mathf.Sin(a * 5f + ph[2]) * 0.2f) * roughness;
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Vector2 flat = Vector2.Lerp(peak, dir * edge, rr);
                    float bump = 1f + (Mathf.Sin(a * 4f + ph[3] + rr * 5f) * 0.5f + Mathf.Sin(a * 9f + ph[4] - rr * 7f) * 0.25f) * roughness * 0.6f;
                    float h = height * Mathf.Pow(Mathf.Max(0f, 1f - rr * rr), 1.35f) * bump;
                    if (r == rings) h = -0.02f;
                    var p = new Vector3(flat.x * size.x * 0.5f, h, flat.y * size.y * 0.5f);
                    verts.Add(p);
                    uvs.Add(new Vector2(p.x, p.z) / 1.2f);
                }
            }
            for (int s = 0; s < segs; s++)
            {
                int a = 1 + s, b = 1 + (s + 1) % segs;
                tris.Add(0); tris.Add(b); tris.Add(a);
            }
            for (int r = 1; r < rings; r++)
            {
                int o0 = 1 + (r - 1) * segs, o1 = 1 + r * segs;
                for (int s = 0; s < segs; s++)
                {
                    int s1 = (s + 1) % segs;
                    tris.Add(o0 + s); tris.Add(o0 + s1); tris.Add(o1 + s);
                    tris.Add(o0 + s1); tris.Add(o1 + s1); tris.Add(o1 + s);
                }
            }
            if (mesh == null) mesh = new Mesh { name = "SandPile", hideFlags = HideFlags.DontSave };
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
            var mats = TunnelMaterials.Resolve(materials);
            if (mats != null) mr.sharedMaterial = mats.sand;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var mc = GetComponent<MeshCollider>();
            if (mc == null) mc = gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = null;
            mc.sharedMesh = mesh;
        }
    }
}
