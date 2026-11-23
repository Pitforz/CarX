using System.Collections.Generic;
using CarX.Level;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    [CustomEditor(typeof(TunnelBridge))]
    public class TunnelBridgeEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var bridge = (TunnelBridge)target;
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "Мост: одна плита длиной в сегмент тоннеля графа A, встык к тупику A. " +
                "Граф B целиком сдвигается и поворачивается, чтобы его тупик встал встык с другим концом плиты на одной оси. " +
                "Узлы и рёбра графов не меняются. Узлы A и B должны быть тупиками. На один граф не больше двух мостов.",
                MessageType.Info);
            if (!bridge.Built && !string.IsNullOrEmpty(bridge.Status))
                EditorGUILayout.HelpBox(bridge.Status, MessageType.Warning);
            else if (bridge.Built && !string.IsNullOrEmpty(bridge.Note))
                EditorGUILayout.HelpBox(bridge.Note, MessageType.Warning);
            else if (bridge.Built)
                EditorGUILayout.HelpBox("Пролёт построен на этом объекте, граф B пристыкован. В графы ничего не записано.", MessageType.None);

            var netAProp = serializedObject.FindProperty("networkA");
            var netBProp = serializedObject.FindProperty("networkB");
            var nodeAProp = serializedObject.FindProperty("nodeA");
            var nodeBProp = serializedObject.FindProperty("nodeB");
            EditorGUILayout.PropertyField(netAProp, new GUIContent("Граф A"));
            var netA = netAProp.objectReferenceValue as TunnelNetwork;
            var netB = netBProp.objectReferenceValue as TunnelNetwork;
            NodePopup(nodeAProp, netA, bridge, nodeBProp.stringValue, netA != null && netA == netB, "Узел A");
            EditorGUILayout.PropertyField(netBProp, new GUIContent("Граф B"));
            netB = netBProp.objectReferenceValue as TunnelNetwork;
            NodePopup(nodeBProp, netB, bridge, nodeAProp.stringValue, netA != null && netA == netB, "Узел B");
            if (serializedObject.ApplyModifiedProperties())
                bridge.RebuildNow();
        }

        static void NodePopup(SerializedProperty id, TunnelNetwork net, TunnelBridge self, string otherId, bool sameNet, string label)
        {
            if (net == null)
            {
                EditorGUILayout.PropertyField(id, new GUIContent(label));
                return;
            }
            var ids = new List<string>();
            var labels = new List<string>();
            string current = id.stringValue;
            int selected = -1;
            for (int i = 0; i < net.nodes.Count; i++)
            {
                var n = net.nodes[i];
                if (n == null || string.IsNullOrEmpty(n.id)) continue;
                bool own = n.id == current;
                bool otherEnd = sameNet && n.id == otherId;
                if (!own && (otherEnd || TunnelBridge.NodeTaken(net, n.id, self))) continue;
                if (own) selected = ids.Count;
                ids.Add(n.id);
                labels.Add(n.id);
            }
            if (!string.IsNullOrEmpty(current) && selected < 0)
            {
                ids.Insert(0, current);
                labels.Insert(0, current + " (нельзя)");
                selected = 0;
            }
            if (ids.Count == 0)
            {
                EditorGUILayout.HelpBox(label + ": свободных узлов нет.", MessageType.Warning);
                return;
            }
            int next = EditorGUILayout.Popup(label, Mathf.Max(0, selected), labels.ToArray());
            if (next >= 0 && next < ids.Count && ids[next] != current)
                id.stringValue = ids[next];
        }

        void OnSceneGUI()
        {
            var bridge = (TunnelBridge)target;
            Vector3 a, b;
            if (!bridge.TryWorldEnds(out a, out b)) return;
            Handles.color = bridge.Built ? new Color(0.4f, 0.75f, 1f) : Color.red;
            Handles.DrawAAPolyLine(4f, a, b);
            if (!bridge.Built && !string.IsNullOrEmpty(bridge.Status))
                Handles.Label((a + b) * 0.5f, bridge.Status);
        }

        [MenuItem("CarX/Tunnels/New Tunnel Bridge", priority = 3)]
        [MenuItem("GameObject/CarX/Tunnel Bridge", priority = 12)]
        static void Create()
        {
            var go = new GameObject("Tunnel Bridge");
            Undo.RegisterCreatedObjectUndo(go, "Create Tunnel Bridge");
            var view = SceneView.lastActiveSceneView;
            if (view != null) go.transform.position = view.pivot;
            go.AddComponent<TunnelBridge>();
            Selection.activeGameObject = go;
        }
    }
}
