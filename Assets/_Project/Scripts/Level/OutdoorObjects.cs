using UnityEngine;

namespace CarX.Level
{
    /// <summary>
    /// Put any objects that stand outside (signs, cars, rubbish, your own models) under the object with this component:
    /// their renderers get the outdoor rendering layer, so the canal sun and sky light them (and the tunnel lights do not).
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("CarX/Outdoor Objects")]
    public class OutdoorObjects : MonoBehaviour
    {
        [Tooltip("Rendering Layer снаружи (как Outside Layer у CanalEntrance)")]
        [Range(3, 31)] public int outsideLayer = 3;

        [System.NonSerialized] double nextCheck;

        void OnEnable() { Apply(); }
        void OnTransformChildrenChanged() { Apply(); }

        void Update()
        {
            if (Application.isPlaying) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextCheck) return;
            nextCheck = now + 1.0;
            Apply();
        }

        public void Apply()
        {
            uint mask = 1u << Mathf.Clamp(outsideLayer, 0, 31);
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r.renderingLayerMask != mask) r.renderingLayerMask = mask;
        }
    }
}
