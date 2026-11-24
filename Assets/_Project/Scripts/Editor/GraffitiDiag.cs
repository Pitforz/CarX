using System.Text;
using CarX.Level;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CarX.EditorTools
{
    static class GraffitiDiag
    {
        static string List()
        {
            var sb = new StringBuilder();
            foreach (var g in Object.FindObjectsByType<GraffitiDecal>(FindObjectsInactive.Include))
            {
                var p = g.GetComponent<DecalProjector>();
                sb.Append("\n  " + g.name + " parent " + (g.transform.parent ? g.transform.parent.name : "-") + " size " + p.size + " depthField " + g.depth +
                          " fade " + p.startAngleFade + ".." + p.endAngleFade + " mask " + p.renderingLayerMask + " pos " + g.transform.position + " fwd " + g.transform.forward);
            }
            return sb.ToString();
        }
    }
}
