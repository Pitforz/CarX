using System.Reflection;
using System.Text;
using CarX.Level;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>Remote-bridge diagnostics for the canal scene.</summary>
    public static class CanalDebug
    {
        public static string SceneViewInfo()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return "no scene view";
            var cam = sv.camera;
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            var sb = new StringBuilder();
            sb.Append("pos " + cam.transform.position + " euler " + cam.transform.eulerAngles + " fov " + cam.fieldOfView + " ortho " + sv.orthographic);
            sb.Append(" near " + cam.nearClipPlane + " far " + cam.farClipPlane + " lighting " + sv.sceneLighting + " drawMode " + sv.cameraMode.drawMode);
            sb.Append(" outside " + (c != null ? c.Outside(cam.transform.position).ToString() : "no canal"));
            var st = sv.sceneViewState;
            foreach (var f in st.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)) sb.Append(" " + f.Name + "=" + f.GetValue(st));
            foreach (var p in st.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                try { sb.Append(" " + p.Name + "=" + p.GetValue(st)); } catch { }
            sb.Append(" | ambient " + RenderSettings.ambientMode + " " + RenderSettings.ambientLight + " sky " + RenderSettings.skybox + " fog " + RenderSettings.fog + " " + RenderSettings.fogMode + " " + RenderSettings.fogDensity);
            return sb.ToString();
        }
    }
}
