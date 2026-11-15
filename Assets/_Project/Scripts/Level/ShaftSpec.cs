using System;
using UnityEngine;

namespace CarX.Level
{
    public enum ShaftType
    {
        [InspectorName("Длинная ливнёвка поперёк хода")] GrateLong,
        [InspectorName("Квадратная решётка")] GrateSquare,
        [InspectorName("Люк с лестницей")] Manhole
    }

    /// <summary>A vertical rise to the surface: storm grate or closed manhole with a ladder.</summary>
    [Serializable]
    public class ShaftSpec
    {
        [Tooltip("Тип подъёма")]
        public ShaftType type = ShaftType.GrateLong;
        [Tooltip("Где на пути: метры от начала тоннеля (можно тянуть ручкой в окне Scene)")]
        [Min(0f)] public float distance = 6f;
        [Tooltip("Сдвиг поперёк хода, м (+ вправо). Люк сам прижимается к стене, если значение большое")]
        public float lateral = 0f;
        [Tooltip("Высота шахты над потолком тоннеля, м")]
        [Range(0.3f, 12f)] public float height = 2.5f;
        [Tooltip("Ливнёвка: длина поперёк тоннеля, м. Квадратная: сторона")]
        [Range(0.3f, 4f)] public float width = 2.4f;
        [Tooltip("Ливнёвка: ширина вдоль тоннеля, м")]
        [Range(0.2f, 1.6f)] public float length = 0.55f;
        [Tooltip("Диаметр люка, м")]
        [Range(0.6f, 1.2f)] public float diameter = 0.9f;
        [Tooltip("Оттенок этого проёма. Белый = общий дневной цвет (TunnelTypes → Daylight Color)")]
        public Color lightColor = Color.white;
        [Tooltip("Яркость этого проёма. 30 у решётки, 26 у квадратной, 14 у люка = ×1 к общей настройке в TunnelTypes")]
        [Range(0f, 200f)] public float brightness = 30f;
        [Tooltip("Множитель видимого столба света (общий: TunnelTypes → Shaft Beam). 0 = нет")]
        [Range(0f, 4f)] public float beam = 1f;
        [Tooltip("Свет, отражённый от пола на потолок рядом с проёмом")]
        [Range(0f, 4f)] public float bounce = 1f;
        [Tooltip("Пылинки в луче (только если включены в TunnelTypes → Shaft Dust)")]
        public bool dust = true;

        public static float DefaultBrightness(ShaftType t) { return t == ShaftType.Manhole ? 14f : t == ShaftType.GrateSquare ? 26f : 30f; }
        public ShaftSpec Clone() { return (ShaftSpec)MemberwiseClone(); }

        public static ShaftSpec Preset(ShaftType t, float distance)
        {
            var s = new ShaftSpec { type = t, distance = distance };
            switch (t)
            {
                case ShaftType.GrateLong: s.width = 2.4f; s.length = 0.55f; s.height = 2.2f; break;
                case ShaftType.GrateSquare: s.width = 1.1f; s.length = 1.1f; s.height = 2.6f; s.brightness = 26f; break;
                case ShaftType.Manhole: s.lateral = 10f; s.height = 3.2f; s.brightness = 14f; s.beam = 1f; break;
            }
            return s;
        }

        public int Hash()
        {
            unchecked
            {
                int h = (int)type + 17;
                h = h * 31 + lateral.GetHashCode();
                h = h * 31 + height.GetHashCode();
                h = h * 31 + width.GetHashCode();
                h = h * 31 + length.GetHashCode();
                h = h * 31 + diameter.GetHashCode();
                h = h * 31 + lightColor.GetHashCode();
                h = h * 31 + brightness.GetHashCode();
                h = h * 31 + beam.GetHashCode();
                h = h * 31 + bounce.GetHashCode();
                h = h * 31 + (dust ? 1 : 0);
                return h;
            }
        }
    }

    /// <summary>Hole layout of the vented manhole cover, in UV space of the lid (0..1).</summary>
    public static class LidPattern
    {
        public static readonly Vector3[] Holes = Build(); // x, y = uv centre, z = uv radius

        static Vector3[] Build()
        {
            var l = new System.Collections.Generic.List<Vector3>();
            l.Add(new Vector3(0.5f, 0.5f, 0.022f));
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f + 0.26f;
                l.Add(new Vector3(0.5f + Mathf.Cos(a) * 0.17f, 0.5f + Mathf.Sin(a) * 0.17f, 0.018f));
            }
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                l.Add(new Vector3(0.5f + Mathf.Cos(a) * 0.33f, 0.5f + Mathf.Sin(a) * 0.33f, 0.016f));
            }
            return l.ToArray();
        }
    }
}
