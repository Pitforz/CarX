using UnityEditor;
using UnityEngine;

namespace CarX.DarkOrientation.EditorTools
{
    /// <summary>Opens the one asset with every knob of the dark-orientation mode (also writes in any new fields with their defaults).</summary>
    public static class DarkOrientationSettingsMenu
    {
        const string AssetPath = "Assets/_Project/DarkOrientation/Runtime/Resources/" + DarkOrientationSettings.ResourcePath + ".asset";

        [MenuItem("CarX/Dark Orientation/Настройки режима (Inspector)", priority = 10)]
        public static void Open()
        {
            var settings = AssetDatabase.LoadAssetAtPath<DarkOrientationSettings>(AssetPath);
            if (settings == null)
            {
                Debug.LogError("CarX DarkOrientation: settings asset not found at " + AssetPath);
                return;
            }
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        /// <summary>Prints the live state of the mode (for remote checks in Play).</summary>
        public static string Dump()
        {
            var s = DarkOrientationSettings.Load();
            var c = DarkOrientationController.Current;
            return "settings " + (s != null ? s.name : "null") + " near " + s.depthNearDistance + " far " + s.depthFarDistance + " curve " + s.depthCurve
                + " side " + s.depthSideShade + " merge " + s.zoneMerge + " wallCore " + s.wallZoneCore + " wallEnd " + s.wallZoneEnd
                + " | controller " + (c != null ? c.DescribeState() : "none");
        }
    }
}
