using System.Collections.Generic;
using CarX.Level;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>Anything put into a network's "Props" folder gets a TunnelAnchor, so it follows the tunnels.</summary>
    [InitializeOnLoad]
    static class TunnelAnchorHooks
    {
        public const string PropsName = "Props";

        static TunnelAnchorHooks()
        {
            EditorApplication.hierarchyChanged -= AutoAnchor;
            EditorApplication.hierarchyChanged += AutoAnchor;
        }

        static void AutoAnchor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var net in Object.FindObjectsByType<TunnelNetwork>(FindObjectsInactive.Exclude))
            {
                var props = net.transform.Find(PropsName);
                if (props == null) continue;
                foreach (Transform t in props)
                {
                    if (t.GetComponent<TunnelAnchor>() != null) continue;
                    var a = Undo.AddComponent<TunnelAnchor>(t.gameObject);
                    a.network = net;
                    a.Bind();
                }
            }
        }

        public static Transform EnsureProps(TunnelNetwork net)
        {
            var t = net.transform.Find(PropsName);
            if (t != null) return t;
            var go = new GameObject(PropsName);
            Undo.RegisterCreatedObjectUndo(go, "Папка Props");
            go.transform.SetParent(net.transform, false);
            return go.transform;
        }

        [MenuItem("GameObject/CarX/Привязать к тоннелю", false, 20)]
        static void AnchorSelected()
        {
            foreach (var go in Selection.gameObjects)
            {
                var a = go.GetComponent<TunnelAnchor>();
                if (a == null) a = Undo.AddComponent<TunnelAnchor>(go);
                a.Bind();
            }
        }

        [MenuItem("GameObject/CarX/Привязать к тоннелю", true)]
        static bool AnchorSelectedValid() { return Selection.gameObjects.Length > 0 && !EditorApplication.isPlaying; }

        /// <summary>Inspector block for the network: how many objects follow it, and the ones that lost their tunnel.</summary>
        public static void NetworkGUI(TunnelNetwork net)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Объекты в тоннелях (пропсы, NPC, граффити)", EditorStyles.boldLabel);
            var lost = new List<Object>();
            int count = 0;
            foreach (var a in Object.FindObjectsByType<TunnelAnchor>(FindObjectsInactive.Exclude))
            {
                if (a.network != net) continue;
                count++;
                if (a.Lost) lost.Add(a.gameObject);
            }
            var props = net.transform.Find(PropsName);
            if (props == null)
            {
                EditorGUILayout.HelpBox("Положите объекты в папку «Props» внутри графа: они привяжутся к тоннелям и поедут вместе с ними, когда вы двигаете узлы.", MessageType.None);
                if (GUILayout.Button("Создать папку Props")) Selection.activeGameObject = EnsureProps(net).gameObject;
            }
            else if (GUILayout.Button("Папка Props")) Selection.activeGameObject = props.gameObject;
            EditorGUILayout.LabelField("Привязано " + count + (lost.Count > 0 ? ", без тоннеля рядом " + lost.Count : ""));
            if (lost.Count > 0 && GUILayout.Button("Выделить объекты без тоннеля")) Selection.objects = lost.ToArray();
        }
    }

    [CustomEditor(typeof(TunnelAnchor)), CanEditMultipleObjects]
    public class TunnelAnchorEditor : Editor
    {
        static string SurfaceName(AnchorSurface s)
        {
            switch (s)
            {
                case AnchorSurface.Floor: return "пол";
                case AnchorSurface.Wall: return "стена";
                case AnchorSurface.Ceiling: return "потолок";
                default: return "в воздухе";
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("network"), new GUIContent("Граф"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("surface"), new GUIContent("Поверхность"));
            serializedObject.ApplyModifiedProperties();

            if (targets.Length == 1)
            {
                var a = (TunnelAnchor)target;
                string text;
                MessageType type = MessageType.Info;
                switch (a.Mode)
                {
                    case AnchorMode.Tunnel:
                        text = "В тоннеле, " + a.Distance.ToString("0.00") + " м от узла " + a.NodeA + ", " + SurfaceName(a.BoundSurface) + ".";
                        break;
                    case AnchorMode.Chamber: text = "В камере-развязке " + a.NodeId + ": двигается и поворачивается вместе с ней."; break;
                    case AnchorMode.Room: text = "В комнате " + a.NodeId + ": двигается вместе с ней."; break;
                    case AnchorMode.RoomWall: text = "На стене комнаты " + a.NodeId + "."; break;
                    default: text = "Не привязан."; break;
                }
                if (a.Lost)
                {
                    text = "Рядом нет тоннеля, камеры или комнаты (дальше 1,5 м от стен): объект не двигается вместе с графом. Перенесите его в тоннель.";
                    type = MessageType.Warning;
                }
                EditorGUILayout.HelpBox(text + "\nСдвигаете узлы: объект сохраняет расстояние от ближайшего неподвижного узла и остаётся на своей поверхности. Двигаете сам объект: он привязывается заново.", type);
            }
            if (GUILayout.Button("Привязать заново здесь"))
                foreach (var t in targets) ((TunnelAnchor)t).Bind();
        }
    }
}
