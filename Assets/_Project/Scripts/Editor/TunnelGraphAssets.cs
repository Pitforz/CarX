using CarX.Level;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>Creates the default tunnel type library (Assets/_Project/Tunnels/TunnelTypes.asset) on first use.</summary>
    public static class TunnelGraphAssets
    {
        public const string Dir = "Assets/_Project/Tunnels";
        public const string LibraryPath = Dir + "/TunnelTypes.asset";

        public static TunnelTypeLibrary GetLibrary()
        {
            var lib = AssetDatabase.LoadAssetAtPath<TunnelTypeLibrary>(LibraryPath);
            if (lib != null && lib.types.Count > 0) return lib;
            TunnelAssets.EnsureFolder(Dir + "/Types");
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<TunnelTypeLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }
            lib.materials = TunnelAssets.Get(false);

            var s = new TunnelSection { profile = TunnelProfile.Octagon, width = 3f, height = 2.4f, chamfer = 0.3f };
            lib.types.Add(Make("Octagon", "Восьмиугольник", new Color(1f, 0.55f, 0.1f), s, true));
            s = new TunnelSection { profile = TunnelProfile.BoxWithCurbs, width = 3.2f, height = 2.5f, curbWidth = 0.5f, curbHeight = 0.16f };
            lib.types.Add(Make("BoxChannel", "Короб с каналом", new Color(0.2f, 0.55f, 1f), s, true));
            s = new TunnelSection { profile = TunnelProfile.Box, width = 2.4f, height = 2.4f };
            lib.types.Add(Make("BoxNarrow", "Короб узкий", new Color(0.3f, 0.85f, 0.3f), s, false));
            s = new TunnelSection { profile = TunnelProfile.Box, width = 3.4f, height = 2.1f };
            lib.types.Add(Make("BoxWide", "Короб широкий", new Color(0.7f, 0.35f, 1f), s, false));
            s = new TunnelSection { profile = TunnelProfile.Round, diameter = 2.2f, silt = 0.22f };
            lib.types.Add(Make("RoundPipe", "Круглая труба", new Color(0.65f, 0.65f, 0.65f), s, false));

            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log("CarX: создана библиотека типов тоннелей " + LibraryPath);
            return lib;
        }

        static TunnelType Make(string file, string label, Color color, TunnelSection section, bool water)
        {
            string path = Dir + "/Types/" + file + ".asset";
            var t = AssetDatabase.LoadAssetAtPath<TunnelType>(path);
            if (t != null) return t;
            t = ScriptableObject.CreateInstance<TunnelType>();
            t.displayName = label;
            t.color = color;
            t.section = section;
            t.segmentLength = 2f;
            t.maxJointAngle = 2f;
            t.channelWater = water;
            AssetDatabase.CreateAsset(t, path);
            return t;
        }
    }
}
