using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>
    /// Daylight at the tunnel exits in the same warm colour as the sun in the grates, so that from inside a tunnel the way out glows
    /// like the grate light, not cold white. Afterwards the colour is the Daylight Color field of CarX → Настройки света тоннелей.
    /// Bridge: call CarX.EditorTools.TunnelWarmExits.Apply
    /// </summary>
    public static class TunnelWarmExits
    {
        public static string Apply()
        {
            var lib = TunnelTypeLibrary.Current;
            if (lib == null || !AssetDatabase.Contains(lib)) return "no tunnel settings asset";
            var old = lib.daylightColor;
            var warm = new Color(lib.grateLightColor.r, lib.grateLightColor.g, lib.grateLightColor.b, 1f);
            Undo.RecordObject(lib, "Warm tunnel exits");
            lib.daylightColor = warm;
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssetIfDirty(lib);
            // the exits take the colour from the settings when the network is built; set them now without rebuilding the tunnels
            int n = 0, kept = 0;
            foreach (var ex in Object.FindObjectsByType<TunnelExit>(FindObjectsInactive.Include))
            {
                if (ex.lightColor != old && ex.lightColor != warm) { kept++; continue; }   // a colour set by hand stays
                Undo.RecordObject(ex, "Warm tunnel exits");
                ex.lightColor = warm;
                ex.skyColor = lib.SkyColor;
                ex.Rebuild();
                EditorUtility.SetDirty(ex);
                EditorSceneManager.MarkSceneDirty(ex.gameObject.scene);
                n++;
            }
            return "daylight " + old + " -> " + warm + ", exits " + n + (kept > 0 ? ", own colour kept " + kept : "");
        }
    }
}
