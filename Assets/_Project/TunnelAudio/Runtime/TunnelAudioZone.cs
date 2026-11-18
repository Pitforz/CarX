using System.Collections.Generic;
using UnityEngine;

namespace CarX.TunnelAudio
{
    /// <summary>
    /// A box on the map. While the ear is inside, its rates replace the director's.
    /// Resize the box collider to cover any part of the level.
    /// </summary>
    [AddComponentMenu("CarX/Tunnel Audio/Zone")]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class TunnelAudioZone : MonoBehaviour
    {
        public static readonly List<TunnelAudioZone> All = new List<TunnelAudioZone>();

        [Tooltip("Если зоны пересекаются, берётся больший приоритет. При равенстве — меньший объём")]
        public int priority;
        [Tooltip("Пока ухо внутри, числа ниже полностью заменяют общие на Tunnel Audio")]
        public bool replaceRates = true;
        public TunnelAudioRates rates = new TunnelAudioRates();
        public Color gizmoColor = new Color(0.35f, 0.7f, 1f, 0.9f);

        BoxCollider _box;

        public BoxCollider Box
        {
            get
            {
                if (_box == null) _box = GetComponent<BoxCollider>();
                return _box;
            }
        }

        public float Volume
        {
            get
            {
                var box = Box;
                if (box == null) return float.MaxValue;
                Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
                return Mathf.Abs(size.x * size.y * size.z);
            }
        }

        void Reset()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            if (Box != null) Box.isTrigger = true;
        }

        void OnDisable()
        {
            All.Remove(this);
        }

        public bool Contains(Vector3 world)
        {
            var box = Box;
            if (box == null) return false;
            Vector3 local = transform.InverseTransformPoint(world) - box.center;
            Vector3 half = box.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        public static TunnelAudioZone Pick(Vector3 world)
        {
            TunnelAudioZone best = null;
            int bestPriority = int.MinValue;
            float bestVolume = float.MaxValue;
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var zone = All[i];
                if (zone == null) { All.RemoveAt(i); continue; }
                if (!zone.isActiveAndEnabled || !zone.replaceRates || !zone.Contains(world)) continue;
                float volume = zone.Volume;
                if (zone.priority > bestPriority || (zone.priority == bestPriority && volume < bestVolume))
                {
                    best = zone;
                    bestPriority = zone.priority;
                    bestVolume = volume;
                }
            }
            return best;
        }

        void OnDrawGizmos()
        {
            var box = Box;
            if (box == null) return;
            bool hot = Application.isPlaying && TunnelAudioDirector.Current != null && TunnelAudioDirector.Current.ActiveZone == this;
            var color = gizmoColor;
            color.a = hot ? 1f : 0.45f;
            Gizmos.color = color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
