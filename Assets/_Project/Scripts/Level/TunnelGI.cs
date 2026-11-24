using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>
    /// Baked light of the tunnels lives in Adaptive Probe Volumes (world space), not in lightmaps:
    /// the tunnel meshes are rebuilt on every load, so lightmap UVs would not survive.
    /// Call Setup on every generated tunnel renderer (walls, floor, chambers, rooms, shafts, the black skin).
    /// </summary>
    public static class TunnelGI
    {
        /// <param name="contributeGI">true: the mesh blocks and bounces baked light (walls, floor, skin).</param>
        public static void Setup(Renderer r, bool contributeGI = true)
        {
            if (r == null) return;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;   // the probes carry the baked bounce light
#if UNITY_EDITOR
            // receiveGI exists only in the Editor (it matters for baking only)
            var mr = r as MeshRenderer;
            if (mr != null && contributeGI) mr.receiveGI = ReceiveGI.LightProbes;   // no lightmap, no lightmap UVs needed
            if (contributeGI) MarkStatic(r.gameObject);
#endif
        }

        /// <summary>Visible light effects (beams, dust, glowing sky) never take part in baking.</summary>
        public static void Exclude(Renderer r)
        {
            if (r == null) return;
            r.lightProbeUsage = LightProbeUsage.Off;
#if UNITY_EDITOR
            var go = r.gameObject;
            var f = UnityEditor.GameObjectUtility.GetStaticEditorFlags(go);
            if ((f & UnityEditor.StaticEditorFlags.ContributeGI) != 0)
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, f & ~UnityEditor.StaticEditorFlags.ContributeGI);
#endif
        }

#if UNITY_EDITOR
        /// <summary>Contribute GI only: no static batching (the meshes are not saved with the scene).</summary>
        public static void MarkStatic(GameObject go)
        {
            if (go == null) return;
            var f = UnityEditor.GameObjectUtility.GetStaticEditorFlags(go);
            var want = (f | UnityEditor.StaticEditorFlags.ContributeGI) & ~UnityEditor.StaticEditorFlags.BatchingStatic;
            if (want != f) UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, want);
        }
#endif
    }
}
