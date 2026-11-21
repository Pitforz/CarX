using System.Collections.Generic;
using UnityEngine;

namespace CarX.Level
{
    /// <summary>What the "Мусор" tool of the tunnel graph scatters and how. One asset for the whole project
    /// (Assets/_Project/Tunnels/TrashSettings.asset). Prefabs come from URP_WasteOvergrowth_SA, which is not in git:
    /// without the pack nothing is placed or removed, the graph keeps its trash counts.</summary>
    [CreateAssetMenu(menuName = "CarX/Tunnels/Trash Settings", fileName = "TrashSettings")]
    public class TunnelTrashSettings : ScriptableObject
    {
        [Tooltip("Кучи мусора (Trash Group). Ставятся у стен и в углах тупиков")]
        public List<GameObject> heaps = new List<GameObject>();
        [Tooltip("Отдельные кучи (Trash Pile): торчат из-под пола, чаще в углах")]
        public List<GameObject> piles = new List<GameObject>();
        [Tooltip("Мелочь (бутылки, бумага, коробки): рядом с кучами и в средней части тоннеля")]
        public List<GameObject> litter = new List<GameObject>();

        [Header("Где")]
        [Tooltip("Доля точек в средней части тоннеля (там только мелочь)")]
        [Range(0f, 1f)] public float centerChance = 0.25f;
        [Tooltip("Доля точек у стен, где вместо кучи - торчащий из пола Pile (в углах тупиков вдвое чаще)")]
        [Range(0f, 1f)] public float pileChance = 0.35f;
        [Tooltip("Не ставить у стен ближе этого к развилкам, камерам и комнатам, м")]
        [Min(0f)] public float endMargin = 1.5f;
        [Tooltip("Зазор до стены, м (отрицательный - куча чуть заходит в стену)")]
        public float wallGap = 0.02f;

        [Header("Как")]
        [Tooltip("Сколько мелочи вокруг каждой кучи (от и до)")]
        public Vector2Int litterAround = new Vector2Int(1, 3);
        [Tooltip("Сколько мелочи в точке посередине тоннеля (от и до)")]
        public Vector2Int litterCenter = new Vector2Int(2, 4);
        [Tooltip("Насколько Pile уходит под пол, доля его высоты (от и до)")]
        public Vector2 pileSink = new Vector2(0.35f, 0.6f);
        [Tooltip("Насколько куча уходит под пол, м (от и до)")]
        public Vector2 heapSink = new Vector2(0f, 0.06f);
        [Tooltip("Масштаб (от и до)")]
        public Vector2 scale = new Vector2(0.9f, 1.15f);
        [Tooltip("Разброс поворота кучи вдоль стены, градусов")]
        [Range(0f, 180f)] public float yawJitter = 25f;

        [Header("Инструмент")]
        [Tooltip("Сколько точек мусора добавляет или убирает один клик")]
        [Range(1, 10)] public int perClick = 1;
    }
}
