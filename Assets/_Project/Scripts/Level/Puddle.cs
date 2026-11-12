using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>Flat water puddle. Water stays horizontal: on a slope it only shows where the floor is lower than the water level.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("CarX/Puddle")]
    public class Puddle : MonoBehaviour, IFloorDressing
    {
        [Tooltip("Размер лужи, м")]
        public Vector2 size = new Vector2(1.4f, 0.9f);
        [Tooltip("Неровность края")]
        [Range(0f, 0.6f)] public float edgeNoise = 0.3f;
        [Tooltip("Уровень воды над полом, м")]
        [Range(0.002f, 0.06f)] public float lift = 0.012f;
        public int seed = 1;
        [Tooltip("Пусто = стандартные материалы тоннелей")]
        public TunnelMaterials materials;

        public float FloorOffset { get { return lift; } }

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
            float p1 = (float)rnd.NextDouble() * 6.28f, p2 = (float)rnd.NextDouble() * 6.28f, p3 = (float)rnd.NextDouble() * 6.28f;
            Vector2 uvOff = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
            const int n = 56;
            var rim = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float noise = Mathf.Sin(a * 2f + p1) * 0.5f + Mathf.Sin(a * 3f + p2) * 0.3f + Mathf.Sin(a * 7f + p3) * 0.2f;
                float k = 1f + noise * edgeNoise;
                rim[i] = new Vector3(Mathf.Cos(a) * size.x * 0.5f * k, 0f, Mathf.Sin(a) * size.y * 0.5f * k);
            }
            var kit = new MeshKit(1);
            for (int i = 0; i < n; i++)
            {
                Vector3 a = rim[i], b = rim[(i + 1) % n];
                kit.Tri(0, Vector3.zero, a, b, uvOff, uvOff + new Vector2(a.x, a.z) / 1.5f, uvOff + new Vector2(b.x, b.z) / 1.5f, Vector3.up);
            }
            if (mesh == null) mesh = new Mesh { name = "Puddle", hideFlags = HideFlags.DontSave };
            kit.Fill(mesh);
            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
            var mats = TunnelMaterials.Resolve(materials);
            if (mats != null) mr.sharedMaterial = mats.water;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
