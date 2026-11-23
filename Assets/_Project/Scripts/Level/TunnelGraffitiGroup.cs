using UnityEngine;

namespace CarX.Level
{
    /// <summary>One line, room or chamber of graph graffiti. The editor rebuilds the decals under it from the graph
    /// (count + seed); the signature says whether that result is still current.</summary>
    public class TunnelGraffitiGroup : MonoBehaviour
    {
        public string owner;
        public int signature;
        public int items;
    }
}
