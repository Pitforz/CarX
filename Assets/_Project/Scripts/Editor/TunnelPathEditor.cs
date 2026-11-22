using System.Collections.Generic;
using CarX.Level;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    [CustomEditor(typeof(TunnelPath))]
    public class TunnelPathEditor : Editor
    {
        static readonly Color PathColor = new Color(1f, 0.55f, 0.1f, 0.95f);
        static readonly Color ShaftColor = new Color(1f, 0.9f, 0.2f, 1f);

        public override void OnInspectorGUI()
        {
            var path = (TunnelPath)target;
            if (path.IsDriven)
            {
                EditorGUILayout.HelpBox("Этот тоннель построен графом (TunnelNetwork). Меняйте его через граф: выберите объект TunnelNetwork.", MessageType.Info);
                var graph = path.GetComponentInParent<TunnelNetwork>();
                if (graph != null && GUILayout.Button("Выбрать граф")) Selection.activeGameObject = graph.gameObject;
                return;
            }
            EditorGUILayout.HelpBox(
                "Тоннель строится по точкам — это дочерние объекты Point_0, Point_1… Двигайте их стрелками в окне Scene.\n" +
                "Подъёмы наверх (ливнёвки, люки) добавляются кнопками ниже и двигаются жёлтым шариком вдоль тоннеля.\n" +
                "Все числа ниже меняются на лету.", MessageType.Info);
            if (!string.IsNullOrEmpty(path.warning)) EditorGUILayout.HelpBox(path.warning, MessageType.Warning);
            EditorGUILayout.LabelField("Длина: " + path.totalLength.ToString("0.0") + " м   Плит: " + path.slabs.Count, EditorStyles.miniBoldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Точка в конец")) AddPoint(path);
                using (new EditorGUI.DisabledScope(path.Points().Count <= 2))
                    if (GUILayout.Button("− Последняя точка")) RemovePoint(path);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Ливнёвка")) AddShaft(path, ShaftType.GrateLong);
                if (GUILayout.Button("+ Решётка")) AddShaft(path, ShaftType.GrateSquare);
                if (GUILayout.Button("+ Люк")) AddShaft(path, ShaftType.Manhole);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Раскидать лужи")) TunnelDressing.Scatter(path, Random.Range(1, 99999), true);
                if (GUILayout.Button("Убрать лужи")) TunnelDressing.Clear(path, true);
            }
            if (GUILayout.Button("Пересобрать")) path.Rebuild(true);
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        static void AddPoint(TunnelPath path)
        {
            var pts = path.Points();
            Vector3 pos;
            if (pts.Count >= 2)
            {
                Vector3 a = pts[pts.Count - 2].position, b = pts[pts.Count - 1].position;
                pos = b + (b - a).normalized * 4f;
            }
            else if (pts.Count == 1) pos = pts[0].position + path.transform.forward * 4f;
            else pos = path.transform.position;
            var go = new GameObject("Point_" + pts.Count);
            Undo.RegisterCreatedObjectUndo(go, "Add tunnel point");
            go.transform.SetParent(path.transform, true);
            go.transform.position = pos;
            path.MarkDirty();
        }

        static void RemovePoint(TunnelPath path)
        {
            var pts = path.Points();
            if (pts.Count <= 2) return;
            Undo.DestroyObjectImmediate(pts[pts.Count - 1].gameObject);
            path.MarkDirty();
        }

        static void AddShaft(TunnelPath path, ShaftType type)
        {
            Undo.RecordObject(path, "Add shaft");
            float d = path.totalLength * 0.5f;
            var view = SceneView.lastActiveSceneView;
            if (view != null && path.slabs.Count > 0) d = path.ClosestDistance(view.pivot);
            path.shafts.Add(ShaftSpec.Preset(type, d));
            EditorUtility.SetDirty(path);
            path.MarkDirty();
        }

        static string Label(ShaftType t)
        {
            switch (t)
            {
                case ShaftType.GrateLong: return "Ливнёвка";
                case ShaftType.GrateSquare: return "Решётка";
                case ShaftType.PipeGrate: return "Решётка трубы";
                default: return "Люк";
            }
        }

        void OnSceneGUI()
        {
            var path = (TunnelPath)target;
            if (path.IsDriven) return;
            var pts = path.Points();
            var line = new List<Vector3>();
            foreach (var p in pts) line.Add(p.position);
            Handles.color = PathColor;
            if (line.Count > 1) Handles.DrawAAPolyLine(4f, line.ToArray());

            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                Handles.Label(p.position + Vector3.up * 0.5f, "P" + i, EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                Vector3 np = Handles.PositionHandle(p.position, Tools.pivotRotation == PivotRotation.Local ? p.rotation : Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(p, "Move tunnel point");
                    p.position = np;
                    path.MarkDirty();
                }
            }

            for (int i = 0; i < path.shafts.Count && i < path.shaftCentres.Count; i++)
            {
                Vector3 c = path.shaftCentres[i];
                if (float.IsNaN(c.x)) continue;
                Vector3 w = path.transform.TransformPoint(c);
                float size = HandleUtility.GetHandleSize(w) * 0.16f;
                Handles.color = ShaftColor;
                EditorGUI.BeginChangeCheck();
                Vector3 nw = Handles.FreeMoveHandle(w, size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(path, "Move shaft");
                    path.shafts[i].distance = path.ClosestDistance(nw);
                    EditorUtility.SetDirty(path);
                    path.MarkDirty();
                }
                Handles.Label(w + Vector3.down * 0.35f, Label(path.shafts[i].type) + " " + path.shafts[i].distance.ToString("0.0") + " м");
            }
        }

        // ------------------------------------------------------------------ menus

        [MenuItem("CarX/Tunnels/New Tunnel Path", priority = 1)]
        [MenuItem("GameObject/CarX/Tunnel Path", priority = 10)]
        static void NewPath()
        {
            var mats = TunnelAssets.Get(false);
            var go = new GameObject("TunnelPath");
            Undo.RegisterCreatedObjectUndo(go, "New tunnel path");
            var view = SceneView.lastActiveSceneView;
            go.transform.position = view != null ? view.pivot : Vector3.zero;
            var pts = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 4), new Vector3(1.5f, 0, 8), new Vector3(2.5f, 0, 12) };
            for (int i = 0; i < pts.Length; i++)
            {
                var p = new GameObject("Point_" + i);
                p.transform.SetParent(go.transform, false);
                p.transform.localPosition = pts[i];
            }
            var path = go.AddComponent<TunnelPath>();
            path.materials = mats;
            path.Rebuild(true);
            Selection.activeGameObject = go;
        }

        [MenuItem("CarX/Tunnels/New Junction Chamber", priority = 2)]
        [MenuItem("GameObject/CarX/Tunnel Junction", priority = 11)]
        static void NewJunction()
        {
            var mats = TunnelAssets.Get(false);
            var go = new GameObject("TunnelJunction");
            Undo.RegisterCreatedObjectUndo(go, "New junction");
            var view = SceneView.lastActiveSceneView;
            go.transform.position = view != null ? view.pivot : Vector3.zero;
            var j = go.AddComponent<TunnelJunction>();
            j.materials = mats;
            j.Rebuild(true);
            Selection.activeGameObject = go;
        }

        [MenuItem("CarX/Tunnels/Add Atmosphere (darkness, fog, post FX)", priority = 30)]
        static void Atmosphere()
        {
            TunnelAssets.Get(false);
            TunnelAssets.ApplyAtmosphere();
            Debug.Log("CarX: atmosphere applied to the open scene");
        }
    }

    [CustomEditor(typeof(TunnelJunction))]
    public class TunnelJunctionEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var j = (TunnelJunction)target;
            if (j.drivenSignature != 0 && j.GetComponentInParent<TunnelNetwork>() != null)
            {
                EditorGUILayout.HelpBox("Эта камера построена графом (TunnelNetwork). Размеры и подъём меняйте в узле графа.", MessageType.Info);
                if (GUILayout.Button("Выбрать граф")) Selection.activeGameObject = j.GetComponentInParent<TunnelNetwork>().gameObject;
                return;
            }
            EditorGUILayout.HelpBox(
                "Камера-развязка. Перетащите тоннели (объекты с Tunnel Path) в поля сторон и нажмите «Пристыковать тоннели»: " +
                "ближняя крайняя точка тоннеля встанет в центр стены, а в стене вырежется проём точно по форме тоннеля.", MessageType.Info);
            if (!string.IsNullOrEmpty(j.warning)) EditorGUILayout.HelpBox(j.warning, MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Пристыковать тоннели")) Snap(j);
                if (GUILayout.Button("Пересобрать")) j.Rebuild(true);
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        static void Snap(TunnelJunction j)
        {
            for (int side = 0; side < 4; side++)
            {
                var path = j.GetSide(side);
                if (path == null) continue;
                var pts = path.Points();
                if (pts.Count < 2) continue;
                Vector3 d = j.transform.TransformDirection(TunnelJunction.SideDirs[side]);
                Vector3 wall = j.transform.TransformPoint(TunnelJunction.SideDirs[side] * j.HalfDepth(side));
                bool atStart = (pts[0].position - wall).sqrMagnitude <= (pts[pts.Count - 1].position - wall).sqrMagnitude;
                Transform end = atStart ? pts[0] : pts[pts.Count - 1];
                Transform next = atStart ? pts[1] : pts[pts.Count - 2];
                Undo.RecordObject(end, "Snap tunnel");
                Undo.RecordObject(next, "Snap tunnel");
                Undo.RecordObject(path, "Snap tunnel");
                end.position = wall;
                if (pts.Count >= 3) next.position = wall + d * Mathf.Max(2f, path.segmentLength);
                if (atStart) path.startAtWall = true; else path.endAtWall = true;
                path.straightEnds = true;
                EditorUtility.SetDirty(path);
                path.MarkDirty();
            }
            j.MarkDirty();
        }
    }
}
