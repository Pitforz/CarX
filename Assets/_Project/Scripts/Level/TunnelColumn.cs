using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    public enum ColumnShape
    {
        [InspectorName("Квадратная")] Square,
        [InspectorName("Круглая")] Round
    }

    /// <summary>Concrete column: position on the floor (local XZ), shape and size. Used by rooms and by TunnelColumn.</summary>
    [Serializable]
    public class ColumnSpec
    {
        public Vector2 position;
        public ColumnShape shape = ColumnShape.Square;
        [Tooltip("Сторона квадрата или диаметр, м")]
        [Range(0.2f, 3f)] public float size = 0.6f;
        [Tooltip("Поворот квадратной колонны, градусы")]
        public float rotation;

        public List<Vector2> Outline()
        {
            if (shape == ColumnShape.Round) return MeshKit.Circle(position, size * 0.5f, 20);
            var l = MeshKit.Rect(Vector2.zero, size * 0.5f, size * 0.5f);
            var q = Quaternion.Euler(0f, 0f, -rotation);
            for (int i = 0; i < l.Count; i++) l[i] = position + (Vector2)(q * l[i]);
            return l;
        }

        /// <summary>Sides + top/bottom caps (caps hide the column ends when it is shorter than the room).</summary>
        public void Emit(MeshKit kit, int sub, float y0, float y1)
        {
            var o = Outline();
            kit.Prism(sub, o, y0, y1, false, shape == ColumnShape.Round, 0.5f);
            var c = position;
            for (int i = 0; i < o.Count; i++)
            {
                Vector2 a = o[i], b = o[(i + 1) % o.Count];
                kit.Tri(sub, new Vector3(c.x, y1, c.y), new Vector3(a.x, y1, a.y), new Vector3(b.x, y1, b.y), c * 0.5f, a * 0.5f, b * 0.5f, Vector3.up);
                kit.Tri(sub, new Vector3(c.x, y0, c.y), new Vector3(a.x, y0, a.y), new Vector3(b.x, y0, b.y), c * 0.5f, a * 0.5f, b * 0.5f, Vector3.down);
            }
        }
    }

    /// <summary>Stand-alone concrete column (square or round) with a collider.</summary>
    [ExecuteAlways, DisallowMultipleComponent, SelectionBase]
    [AddComponentMenu("CarX/Concrete Column")]
    public class TunnelColumn : MonoBehaviour
    {
        public ColumnShape shape = ColumnShape.Square;
        [Range(0.2f, 3f)] public float size = 0.6f;
        [Min(0.2f)] public float height = 3f;
        public TunnelMaterials materials;

        [NonSerialized] Mesh mesh;

        void OnEnable() { Rebuild(); }
        void OnValidate() { if (isActiveAndEnabled) Rebuild(); }

        public void Rebuild()
        {
            var kit = new MeshKit(5);
            new ColumnSpec { shape = shape, size = size }.Emit(kit, 0, 0f, height);
            if (mesh == null) mesh = new Mesh { name = name + "_Column", hideFlags = HideFlags.DontSave };
            kit.Fill(mesh);
            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
            var mats = TunnelMaterials.Resolve(materials);
            if (mats != null) mr.sharedMaterials = mats.TunnelSet();
            mr.shadowCastingMode = ShadowCastingMode.On;
            var mc = GetComponent<MeshCollider>();
            if (mc == null) mc = gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = null;
            mc.sharedMesh = mesh;
        }
    }
}
