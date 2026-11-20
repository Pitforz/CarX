using System.Collections.Generic;
using CarX.Level;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>Inspector buttons and Scene view handles for the channel course of <see cref="CanalEntrance"/>.</summary>
    [CustomEditor(typeof(CanalEntrance))]
    public class CanalEntranceEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var c = (CanalEntrance)target;
            EditorGUILayout.HelpBox(
                "Русло: двигайте жёлтые точки в окне Scene (первая у стенки), затем «Пересоздать русло».\n" +
                "Русло (объект «Русло»): дно, плиты, земля, заборы, водопропуск. Дно и землю можно красить Polybrush (Paint Vertex Colors): " +
                "чёрный мелкий гравий, красный крупный, зелёный ил, синий сухая трава. После покраски нажмите «Трава по покраске».\n" +
                "Красные рамки в Scene - невидимые стенки, дальше которых игрок не пройдёт (раздел «Где может ходить игрок»).\n" +
                "Стенка, фартук, шоссе, небо, свет и невидимые стенки пересобираются сами.", MessageType.None);

            if (GUILayout.Button("Пересоздать русло"))
            {
                if (c.transform.Find(CanalEntrance.ChannelRootName) == null || EditorUtility.DisplayDialog("Русло",
                        "Дно, плиты, земля, заборы и трава будут построены заново. Покраска Polybrush и ручные правки внутри объекта «Русло» пропадут " +
                        "(деревья, щиты и всё в «Снаружи: объекты» останутся). Продолжить?", "Да", "Отмена"))
                    CanalEntranceBuilder.RebuildChannel(c);
            }
            if (GUILayout.Button("Трава по покраске"))
                CanalEntranceBuilder.RegrowGrass(c);
            if (GUILayout.Button("Посадить деревья заново"))
            {
                if (EditorUtility.DisplayDialog("Деревья", "Все пальмы, деревья и кусты в «Снаружи: объекты / Деревья» будут посажены заново (и переставленные вручную тоже). Продолжить?", "Да", "Отмена"))
                    CanalEntranceBuilder.PlantTrees(c);
            }
            if (GameObject.Find(CanalEntranceBuilder.PropsRootName) == null && GUILayout.Button("Добавить щиты, знаки и ЛЭП"))
                Undo.RegisterCreatedObjectUndo(CanalEntranceBuilder.PlaceProps(c), "Объекты снаружи");
            if (GUILayout.Button("Город: ЛЭП через русло, вывески, башни"))
            {
                if (GameObject.Find(CanalEntranceBuilder.CityRootName) == null || EditorUtility.DisplayDialog("Город",
                        "Объект «Город» будет построен заново (сцена сохранится), ручные правки внутри него пропадут. Продолжить?", "Да", "Отмена"))
                    CanalEntranceBuilder.AddCity();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Точка русла"))
            {
                Undo.RecordObject(c, "Русло");
                var p = c.channelPath;
                if (p.Count == 0) c.ResetPath();
                else
                {
                    var dir = p.Count > 1 ? (p[p.Count - 1] - p[p.Count - 2]).normalized : Vector2.up;
                    p.Add(p[p.Count - 1] + dir * 30f);
                }
                c.ForceRebuild();
            }
            if (GUILayout.Button("− Последняя точка") && c.channelPath.Count > 2)
            {
                Undo.RecordObject(c, "Русло");
                c.channelPath.RemoveAt(c.channelPath.Count - 1);
                c.ForceRebuild();
            }
            if (GUILayout.Button("Русло по умолчанию"))
            {
                Undo.RecordObject(c, "Русло");
                c.ResetPath();
                c.ForceRebuild();
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        void OnSceneGUI()
        {
            var c = (CanalEntrance)target;
            var p = c.channelPath;
            if (p == null || p.Count == 0) return;
            var line = new List<Vector3>();
            for (int i = 0; i < p.Count; i++)
            {
                var w = c.transform.TransformPoint(new Vector3(p[i].x, 0.2f, p[i].y));
                line.Add(w);
                float size = HandleUtility.GetHandleSize(w) * 0.12f;
                Handles.color = i == 0 ? new Color(1f, 0.5f, 0.1f) : Color.yellow;
                EditorGUI.BeginChangeCheck();
                var moved = Handles.FreeMoveHandle(w, size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    // move on the ground plane
                    var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
                    var plane = new Plane(c.transform.up, c.transform.TransformPoint(new Vector3(0f, 0.2f, 0f)));
                    float t;
                    if (plane.Raycast(ray, out t)) moved = ray.GetPoint(t);
                    Undo.RecordObject(c, "Русло");
                    var l = c.transform.InverseTransformPoint(moved);
                    if (i == 0) l.z = 0f;   // the first point stays on the wall line
                    p[i] = new Vector2(l.x, l.z);
                    c.ForceRebuild();
                }
                Handles.Label(w + Vector3.up * 1.5f, i == 0 ? "Русло: у стенки" : "Русло " + i);
            }
            Handles.color = Color.yellow;
            Handles.DrawAAPolyLine(4f, line.ToArray());

            // invisible walls of the play area
            var b = c.transform.Find(CanalEntrance.RootName + "/Bounds");
            if (b != null)
            {
                Handles.color = new Color(1f, 0.25f, 0.2f, 0.9f);
                foreach (var bc in b.GetComponentsInChildren<BoxCollider>())
                {
                    Handles.matrix = bc.transform.localToWorldMatrix;
                    Handles.DrawWireCube(bc.center, bc.size);
                }
                Handles.matrix = Matrix4x4.identity;
            }
        }
    }
}
