using System.Collections.Generic;
using CarX.Level;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>Wires the runtime network to editor-only services (puddles, undo, selection, schematic mode).</summary>
    [InitializeOnLoad]
    static class TunnelNetworkHooks
    {
        static TunnelNetworkHooks()
        {
            TunnelNetwork.DressingHook = (p, seed) => TunnelDressing.Scatter(p, seed, false);
            TunnelNetwork.deferBuild = TunnelNetworkEditor.Schematic;
            Undo.undoRedoPerformed += () =>
            {
                foreach (var n in Object.FindObjectsByType<TunnelNetwork>(FindObjectsInactive.Exclude)) n.MarkDirty();
                EditorApplication.delayCall += () =>
                {
                    foreach (var n in Object.FindObjectsByType<TunnelNetwork>(FindObjectsInactive.Exclude)) TunnelTrash.Sync(n);
                };
            };
            Selection.selectionChanged += Redirect;
            SceneView.duringSceneGui -= TunnelNetworkEditor.OnScene;
            SceneView.duringSceneGui += TunnelNetworkEditor.OnScene;
            EditorApplication.delayCall += () => TunnelNetworkEditor.ApplySchematic();
        }

        // a click on a generated tunnel or chamber selects the graph (and that line) instead
        static void Redirect()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var net = go.GetComponentInParent<TunnelNetwork>();
            if (net == null || go == net.gameObject) return;
            var gen = net.transform.Find(TunnelNetwork.GeneratedRoot);
            if (gen == null || !go.transform.IsChildOf(gen) || go.transform == gen) return;
            Transform owner = go.transform;
            if (owner.name == TunnelPath.GeneratedName && owner.parent != null) owner = owner.parent;
            if (owner.GetComponent<TunnelPath>() == null && owner.GetComponent<TunnelJunction>() == null) return;
            string edge = null, node = null;
            if (owner.name.StartsWith("Tunnel_")) edge = owner.name.Substring(7);
            if (owner.name.StartsWith("Chamber_")) node = owner.name.Substring(8);
            TunnelNetworkEditor.Pick(node, edge);
            Selection.activeGameObject = net.gameObject;
        }
    }

    /// <summary>Tool panel in the Scene view; shown while a tunnel graph is selected.</summary>
    [Overlay(typeof(SceneView), "carx-tunnel-graph", "Граф тоннелей", true)]
    public class TunnelGraphOverlay : IMGUIOverlay, ITransientOverlay
    {
        public bool visible { get { return TunnelNetworkEditor.Current != null; } }

        public override void OnGUI()
        {
            var net = TunnelNetworkEditor.Current;
            if (net != null) TunnelNetworkEditor.PanelGUI(net);
        }
    }

    [CustomEditor(typeof(TunnelNetwork))]
    public class TunnelNetworkEditor : Editor
    {
        enum Tool { Select, Pen, Brush, Marker, Scissors, Trash }

        static readonly string[] ToolNames = { "Выбор", "Перо", "Кисть", "Метка", "Ножницы", "Мусор" };
        static readonly string[] ToolHints =
        {
            "Клик по узлу или линии. Узел двигайте стрелками, Delete удаляет выбранное. Esc снимает выделение.",
            "Клик: новый узел и линия от предыдущего. Клик по узлу соединяет с ним, Shift+клик по линии делает врезку в неё. Esc заканчивает линию.",
            "Клик по линии красит её в выбранный тип. С Shift красит весь тоннель от камеры до камеры.",
            "Клик по линии ставит подъём наверх выбранного вида. Двигать и удалять в инспекторе линии.",
            "Клик по линии вставляет узел. Клик по узлу убирает его (линии с двух сторон сливаются).",
            "Клик по линии, комнате или камере: больше мусора. Shift+клик: меньше. Ctrl+клик: разложить по-новому (новый сид)."
        };

        static Tool tool = Tool.Select;
        static bool dragging;
        static string hoverNode, hoverEdge;
        static int typeIndex;
        static ShaftType markerType = ShaftType.GrateLong;
        static bool snap = true;
        static float penHeight;
        static string selNode, selEdge, penLast;
        static Vector2 issueScroll;
        static int replaceFrom, replaceTo = 1;

        const string SchematicKey = "CarX.TunnelSchematic";
        public static bool Schematic
        {
            get { return EditorPrefs.GetBool(SchematicKey, false); }
            set { EditorPrefs.SetBool(SchematicKey, value); ApplySchematic(); }
        }

        public static void Pick(string node, string edge) { selNode = node; selEdge = edge; tool = Tool.Select; }

        public static void ApplySchematic()
        {
            bool on = Schematic;
            TunnelNetwork.deferBuild = on;
            foreach (var net in Object.FindObjectsByType<TunnelNetwork>(FindObjectsInactive.Exclude))
            {
                var gen = net.transform.Find(TunnelNetwork.GeneratedRoot);
                if (gen != null)
                {
                    if (on) SceneVisibilityManager.instance.Hide(gen.gameObject, true);
                    else SceneVisibilityManager.instance.Show(gen.gameObject, true);
                }
                var trash = net.transform.Find(TunnelTrash.RootName);
                if (trash != null)
                {
                    if (on) SceneVisibilityManager.instance.Hide(trash.gameObject, true);
                    else SceneVisibilityManager.instance.Show(trash.gameObject, true);
                }
                if (!on) net.MarkDirty();
            }
            SceneView.RepaintAll();
        }

        static TunnelNetworkEditor inspector;

        /// <summary>The graph selected in the Hierarchy (null if none).</summary>
        public static TunnelNetwork Current
        {
            get
            {
                var go = Selection.activeGameObject;
                return go != null ? go.GetComponent<TunnelNetwork>() : null;
            }
        }

        static TunnelNetwork Net { get { return Current; } }

        static void RepaintInspector() { if (inspector != null) inspector.Repaint(); }

        void OnEnable()
        {
            inspector = this;
            var net = (TunnelNetwork)target;
            if (net.library == null)
            {
                net.library = TunnelGraphAssets.GetLibrary();
                EditorUtility.SetDirty(net);
            }
            net.EnsureLayout();
        }

        void OnDisable() { penLast = null; if (inspector == this) inspector = null; }

        [MenuItem("CarX/Tunnels/New Tunnel Network (граф)", priority = 10)]
        static void CreateNetwork()
        {
            var go = new GameObject("TunnelNetwork");
            Undo.RegisterCreatedObjectUndo(go, "Tunnel Network");
            var view = SceneView.lastActiveSceneView;
            if (view != null) go.transform.position = new Vector3(Mathf.Round(view.pivot.x), 0f, Mathf.Round(view.pivot.z));
            var net = go.AddComponent<TunnelNetwork>();
            net.library = TunnelGraphAssets.GetLibrary();
            Selection.activeGameObject = go;
            tool = Tool.Pen;
        }

        // ------------------------------------------------------------------ helpers

        static void Record(string what)
        {
            Undo.RecordObject(Net, what);
        }

        static void Changed()
        {
            Net.MarkDirty();
            EditorUtility.SetDirty(Net);
            SceneView.RepaintAll();
            RepaintInspector();
        }

        static TunnelType CurrentType
        {
            get
            {
                var lib = Net.library;
                if (lib == null || lib.types.Count == 0) return null;
                typeIndex = Mathf.Clamp(typeIndex, 0, lib.types.Count - 1);
                return lib.types[typeIndex];
            }
        }

        static Color ColorOf(TunnelNetwork net, GraphEdge e)
        {
            var t = net.TypeOf(e);
            return t != null ? t.color : Color.white;
        }

        static GraphNode HoverNode(Vector2 mouse)
        {
            GraphNode best = null;
            float bestD = 12f;
            foreach (var n in Net.nodes)
            {
                float d = Vector2.Distance(HandleUtility.WorldToGUIPoint(Net.World(n.position)), mouse);
                if (d < bestD) { bestD = d; best = n; }
            }
            return best;
        }

        static GraphEdge HoverEdge(Vector2 mouse)
        {
            GraphEdge best = null;
            float bestD = 8f;
            foreach (var e in Net.edges)
            {
                var A = Net.Node(e.a); var B = Net.Node(e.b);
                if (A == null || B == null) continue;
                float d = HandleUtility.DistanceToLine(Net.World(A.position), Net.World(B.position));
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        /// <summary>Mouse ray hit on the horizontal plane at the given local height.</summary>
        static bool MouseOnPlane(Vector2 mouse, float localY, out Vector3 local)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var plane = new Plane(Net.transform.up, Net.World(new Vector3(0f, localY, 0f)));
            float enter;
            local = Vector3.zero;
            if (!plane.Raycast(ray, out enter) || enter > 400f) return false;   // near the horizon the hit would be kilometres away
            local = Net.Local(ray.GetPoint(enter));
            local.y = localY;
            if (snap) { local.x = Mathf.Round(local.x); local.z = Mathf.Round(local.z); }
            return true;
        }

        static float EdgeParam(GraphEdge e, Vector2 mouse, out float length)
        {
            var A = Net.Node(e.a); var B = Net.Node(e.b);
            length = Vector3.Distance(A.position, B.position);
            Vector2 a = HandleUtility.WorldToGUIPoint(Net.World(A.position));
            Vector2 b = HandleUtility.WorldToGUIPoint(Net.World(B.position));
            Vector2 ab = b - a;
            return ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector2.Dot(mouse - a, ab) / ab.sqrMagnitude) : 0.5f;
        }

        // ------------------------------------------------------------------ scene view

        public static void OnScene(SceneView view)
        {
            var net = Current;
            if (net == null) return;
            var e = Event.current;
            // meshes are rebuilt once the drag ends, not on every mouse move
            if (dragging && (e.rawType == EventType.MouseUp || GUIUtility.hotControl == 0 && e.type == EventType.Layout))
            {
                dragging = false;
                TunnelNetwork.deferBuild = Schematic;
                net.MarkDirty();
            }
            net.EnsureLayout();
            if (e.type == EventType.Repaint)
            {
                var hn = HoverNode(e.mousePosition);
                var he = hn == null && (tool != Tool.Pen || e.shift) ? HoverEdge(e.mousePosition) : null;
                hoverNode = hn != null ? hn.id : null;
                hoverEdge = he != null ? he.id : null;
            }

            DrawNetwork(net, true);

            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (e.type == EventType.MouseMove) SceneView.RepaintAll();

            if (tool == Tool.Select) SelectHandles(net);

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    if (tool == Tool.Pen && penLast != null) penLast = null;
                    else if (selNode != null || selEdge != null) { selNode = null; selEdge = null; }
                    else Selection.activeObject = null;
                    e.Use();
                    RepaintInspector();
                }
            }
            // Delete key arrives as a command; with a node or line picked it deletes that, not the whole graph object
            if ((e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand) &&
                (e.commandName == "SoftDelete" || e.commandName == "Delete") && (selNode != null || selEdge != null))
            {
                if (e.type == EventType.ExecuteCommand)
                {
                    Record("Удалить из графа");
                    if (selNode != null) net.RemoveNode(selNode); else net.RemoveEdge(selEdge);
                    selNode = null; selEdge = null;
                    Changed();
                }
                e.Use();
            }

            if (tool == Tool.Pen && penLast != null && e.type == EventType.Repaint)
            {
                var last = net.Node(penLast);
                var hn = HoverNode(e.mousePosition);
                Vector3 p = Vector3.zero;
                bool ok = false;
                if (last != null)
                {
                    if (hn != null) { p = hn.position; ok = true; }
                    else ok = MouseOnPlane(e.mousePosition, last.position.y, out p);
                }
                if (ok)
                {
                    var t = CurrentType;
                    Handles.color = t != null ? t.color : Color.white;
                    Handles.DrawDottedLine(net.World(last.position), net.World(p), 4f);
                    Handles.Label(net.World(p), " " + Vector3.Distance(last.position, p).ToString("0.0") + " м");
                }
            }

            if (e.type != EventType.MouseDown || e.button != 0 || e.alt) return;

            var node = HoverNode(e.mousePosition);
            var edge = node == null && (tool != Tool.Pen || e.shift) ? HoverEdge(e.mousePosition) : null;
            switch (tool)
            {
                case Tool.Select:
                    selNode = node != null ? node.id : null;
                    selEdge = edge != null ? edge.id : null;
                    RepaintInspector();
                    SceneView.RepaintAll();
                    break;
                case Tool.Pen: PenClick(net, node, edge, e.mousePosition); break;
                case Tool.Brush:
                    if (edge != null && CurrentType != null)
                    {
                        Record("Покрасить тоннель");
                        if (e.shift)
                        {
                            var c = net.ChainOf(edge);
                            if (c != null) foreach (var x in c.edges) x.type = CurrentType;
                        }
                        edge.type = CurrentType;
                        Changed();
                    }
                    break;
                case Tool.Marker:
                    if (edge != null)
                    {
                        var t = net.TypeOf(edge);
                        if (t != null && t.section.profile == TunnelProfile.Round)
                        {
                            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.ShowNotification(new GUIContent("В круглой трубе подъёмов нет"));
                            break;
                        }
                        float len;
                        float k = EdgeParam(edge, e.mousePosition, out len);
                        Record("Метка на тоннеле");
                        edge.shafts.Add(ShaftSpec.Preset(markerType, k * len));
                        selEdge = edge.id; selNode = null;
                        Changed();
                    }
                    break;
                case Tool.Trash: TrashClick(net, node, edge, e); break;
                case Tool.Scissors:
                    if (node != null) { Record("Убрать узел"); Dissolve(net, node); Changed(); }
                    else if (edge != null)
                    {
                        float len;
                        float k = EdgeParam(edge, e.mousePosition, out len);
                        var A = net.Node(edge.a); var B = net.Node(edge.b);
                        Record("Вставить узел");
                        var p = Vector3.Lerp(A.position, B.position, k);
                        var n = net.SplitEdge(edge, p);
                        selNode = n != null ? n.id : null; selEdge = null;
                        Changed();
                    }
                    break;
            }
            e.Use();
        }

        static void PenClick(TunnelNetwork net, GraphNode node, GraphEdge edge, Vector2 mouse)
        {
            var last = net.Node(penLast);
            if (node != null && node == last) return;
            GraphNode target = node;
            Vector3 p = Vector3.zero;
            if (target == null && edge == null)
            {
                if (!MouseOnPlane(mouse, last != null ? last.position.y : penHeight, out p))
                {
                    if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.ShowNotification(new GUIContent("Слишком далеко: кликните ближе к камере"));
                    return;
                }
                if (last != null && Vector3.Distance(p, last.position) < 1f) return;   // too short for a tunnel
            }
            Record("Перо: тоннель");
            if (target == null && edge != null)
            {
                float len;
                float k = EdgeParam(edge, mouse, out len);
                var A = net.Node(edge.a); var B = net.Node(edge.b);
                target = net.SplitEdge(edge, Vector3.Lerp(A.position, B.position, k));
            }
            if (target == null) target = net.AddNode(p);
            if (last != null && last != target)
            {
                var ne = net.AddEdge(last.id, target.id, CurrentType);
                if (ne != null && ne.type == null) ne.type = CurrentType;
            }
            penLast = target.id;
            selNode = target.id; selEdge = null;
            Changed();
        }

        // ------------------------------------------------------------------ trash tool

        /// <summary>The room or chamber under the mouse (its floor), when no node or line is hovered.</summary>
        static GraphNode HoverFloorArea(TunnelNetwork net, Vector2 mouse)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            GraphNode best = null;
            float bestT = float.MaxValue;
            foreach (var n in net.nodes)
            {
                if (!TunnelTrash.CanHold(net, n)) continue;
                var plane = new Plane(net.transform.up, net.World(n.position));
                float t;
                if (!plane.Raycast(ray, out t) || t > 400f || t >= bestT) continue;
                var p = net.Local(ray.GetPoint(t));
                if (net.RoomAt(p, 0.2f) != null && net.RoomAt(p, 0.2f).node == n || net.ChamberAt(p, 0.2f) != null && net.ChamberAt(p, 0.2f).node == n)
                {
                    best = n;
                    bestT = t;
                }
            }
            return best;
        }

        static void TrashClick(TunnelNetwork net, GraphNode node, GraphEdge edge, Event ev)
        {
            if (node != null && !TunnelTrash.CanHold(net, node)) node = null;
            if (node == null && edge == null) node = HoverFloorArea(net, ev.mousePosition);
            if (node == null && edge == null) return;
            if (!TunnelTrash.Ready) Note("Пак URP_WasteOvergrowth_SA не найден: количество запомнится, мусор появится, когда пак будет в проекте");
            int step = TunnelTrash.Settings.perClick;
            Record("Мусор");
            if (node != null)
            {
                if (ev.control || ev.command) node.trashSeed = TunnelTrash.NewSeed();
                else node.trash = Mathf.Max(0, node.trash + (ev.shift ? -step : step));
                selNode = node.id; selEdge = null;
            }
            else
            {
                if (ev.control || ev.command) edge.trashSeed = TunnelTrash.NewSeed();
                else edge.trash = Mathf.Max(0, edge.trash + (ev.shift ? -step : step));
                selEdge = edge.id; selNode = null;
            }
            EditorUtility.SetDirty(net);
            TunnelTrash.Sync(net);
            SceneView.RepaintAll();
            RepaintInspector();
        }

        static void Note(string text)
        {
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.ShowNotification(new GUIContent(text));
        }

        static void TrashEverywhere(TunnelNetwork net, int sign)
        {
            Record(sign > 0 ? "Мусор везде" : "Меньше мусора везде");
            int step = TunnelTrash.Settings.perClick;
            foreach (var e in net.edges) e.trash = Mathf.Max(0, e.trash + sign * TunnelTrash.EverywhereStep(net, e, step));
            foreach (var n in net.nodes) if (TunnelTrash.CanHold(net, n)) n.trash = Mathf.Max(0, n.trash + sign * step * (net.IsRoom(n.id) ? 2 : 1));
            EditorUtility.SetDirty(net);
            TunnelTrash.Sync(net);
        }

        static void TrashPanel(TunnelNetwork net)
        {
            var st = TunnelTrash.Settings;
            EditorGUI.BeginChangeCheck();
            int pc = EditorGUILayout.IntSlider("Точек за клик", st.perClick, 1, 10);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(st, "Мусор: за клик"); st.perClick = pc; EditorUtility.SetDirty(st); }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Везде", EditorStyles.miniButton)) TrashEverywhere(net, 1);
            if (GUILayout.Button("− Везде", EditorStyles.miniButton)) TrashEverywhere(net, -1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Новый сид везде", EditorStyles.miniButton))
            {
                Record("Мусор: новый сид");
                foreach (var e in net.edges) if (e.trash > 0) e.trashSeed = TunnelTrash.NewSeed();
                foreach (var n in net.nodes) if (n.trash > 0) n.trashSeed = TunnelTrash.NewSeed();
                EditorUtility.SetDirty(net);
                TunnelTrash.Sync(net);
            }
            if (GUILayout.Button("Убрать весь", EditorStyles.miniButton))
            {
                Record("Убрать весь мусор");
                foreach (var e in net.edges) e.trash = 0;
                foreach (var n in net.nodes) n.trash = 0;
                EditorUtility.SetDirty(net);
                TunnelTrash.Sync(net);
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Настройки мусора (префабы, доли)", EditorStyles.miniButton)) EditorUtility.OpenPropertyEditor(st);
            int spots, places, objects;
            TunnelTrash.Stats(net, out spots, out places, out objects);
            var style = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            if (!TunnelTrash.Ready)
            {
                style.normal.textColor = new Color(1f, 0.6f, 0.3f);
                GUILayout.Label("Пак URP_WasteOvergrowth_SA не найден: мусор не ставится и не удаляется, количество хранится в графе.", style);
            }
            GUILayout.Label("Мусор: " + spots + " точек на " + places + " участках, объектов " + objects, style);
        }

        /// <summary>Removes a node; a node between exactly two lines merges them into one.</summary>
        static void Dissolve(TunnelNetwork net, GraphNode n)
        {
            var list = net.EdgesOf(n.id);
            if (list.Count != 2)
            {
                net.RemoveNode(n.id);
                return;
            }
            GraphEdge e1 = list[0], e2 = list[1];
            string x = TunnelNetwork.Other(e1, n.id), y = TunnelNetwork.Other(e2, n.id);
            var X = net.Node(x); var Y = net.Node(y);
            float l1 = Vector3.Distance(X.position, n.position);
            float l2 = Vector3.Distance(n.position, Y.position);
            var shafts = new List<ShaftSpec>();
            foreach (var s in e1.shafts) { var k = s.Clone(); if (e1.a != x) k.distance = l1 - s.distance; shafts.Add(k); }
            foreach (var s in e2.shafts) { var k = s.Clone(); k.distance = l1 + (e2.a == n.id ? s.distance : l2 - s.distance); shafts.Add(k); }
            net.RemoveNode(n.id);
            if (x == y) return;
            var ne = net.AddEdge(x, y, e1.type);
            if (ne != null) { ne.shafts = shafts; ne.seed = e1.seed; ne.trash = e1.trash + e2.trash; ne.trashSeed = e1.trashSeed; }
        }

        static void AddColumn(TunnelNetwork net, GraphNode n, ColumnShape shape)
        {
            Undo.RecordObject(net, "Колонна");
            n.columns.Add(new ColumnSpec { shape = shape, size = shape == ColumnShape.Round ? 0.7f : 0.6f, position = new Vector2(0.5f * n.columns.Count, 0f) });
            net.MarkDirty();
            EditorUtility.SetDirty(net);
            SceneView.RepaintAll();
        }

        static Vector2 OnFloor(TunnelNetwork net, GraphNode n, Vector3 world)
        {
            var p = net.Local(world);
            var v = new Vector2(p.x - n.position.x, p.z - n.position.z);
            if (snap) { v.x = Mathf.Round(v.x * 4f) * 0.25f; v.y = Mathf.Round(v.y * 4f) * 0.25f; }
            return v;
        }

        static Vector3 FloorWorld(TunnelNetwork net, GraphNode n, Vector2 v, float lift)
        {
            return net.World(new Vector3(n.position.x + v.x, n.position.y + lift, n.position.z + v.y));
        }

        /// <summary>Room corners (move, add on wall middles, Shift+click removes) and columns.</summary>
        static void RoomHandles(TunnelNetwork net, GraphNode n)
        {
            var c = n.roomCorners;
            if (c == null || c.Count < 3) return;
            var tr = net.transform;
            bool shift = Event.current.shift;
            int k = c.Count;
            var pts = new Vector3[k + 1];
            for (int i = 0; i < k; i++) pts[i] = FloorWorld(net, n, c[i], 0.05f);
            pts[k] = pts[0];
            Handles.color = new Color(1f, 0.85f, 0.2f);
            Handles.DrawAAPolyLine(4f, pts);
            for (int i = 0; i < k; i++)
            {
                var p = pts[i];
                float hs = HandleUtility.GetHandleSize(p) * 0.09f;
                if (shift)
                {
                    Handles.color = Color.red;
                    if (k > 3 && Handles.Button(p, Quaternion.identity, hs, hs * 1.3f, Handles.DotHandleCap)) { Record("Убрать угол комнаты"); c.RemoveAt(i); Changed(); GUIUtility.ExitGUI(); }
                    continue;
                }
                Handles.color = Color.yellow;
                EditorGUI.BeginChangeCheck();
                var np = Handles.Slider2D(p, tr.up, tr.right, tr.forward, hs, Handles.DotHandleCap, Vector2.zero);
                if (EditorGUI.EndChangeCheck())
                {
                    Record("Сдвинуть угол комнаты");
                    dragging = true;
                    TunnelNetwork.deferBuild = true;
                    c[i] = OnFloor(net, n, np);
                    Changed();
                }
                var mid = (pts[i] + pts[i + 1]) * 0.5f;
                Handles.color = Color.green;
                if (Handles.Button(mid, Quaternion.identity, hs * 0.7f, hs, Handles.DotHandleCap))
                {
                    Record("Добавить угол комнаты");
                    c.Insert(i + 1, (c[i] + c[(i + 1) % k]) * 0.5f);
                    Changed();
                    GUIUtility.ExitGUI();
                }
            }
            for (int i = 0; i < n.columns.Count; i++)
            {
                var col = n.columns[i];
                var p = FloorWorld(net, n, col.position, 0.05f);
                Handles.color = Color.cyan;
                if (col.shape == ColumnShape.Round) Handles.DrawWireDisc(p, tr.up, col.size * 0.5f);
                else
                {
                    var o = col.Outline();
                    var q = new Vector3[o.Count + 1];
                    for (int j = 0; j < o.Count; j++) q[j] = FloorWorld(net, n, o[j], 0.05f);
                    q[o.Count] = q[0];
                    Handles.DrawAAPolyLine(3f, q);
                }
                float hs = HandleUtility.GetHandleSize(p) * 0.08f;
                EditorGUI.BeginChangeCheck();
                var np = Handles.Slider2D(p, tr.up, tr.right, tr.forward, hs, Handles.DotHandleCap, Vector2.zero);
                if (EditorGUI.EndChangeCheck())
                {
                    Record("Сдвинуть колонну");
                    dragging = true;
                    TunnelNetwork.deferBuild = true;
                    col.position = OnFloor(net, n, np);
                    Changed();
                }
            }
        }

        static void SelectHandles(TunnelNetwork net)
        {
            var n = net.Node(selNode);
            if (n != null && n.kind == GraphNodeKind.Room) RoomHandles(net, n);
            if (n != null)
            {
                var w = net.World(n.position);
                EditorGUI.BeginChangeCheck();
                var nw = Handles.PositionHandle(w, net.transform.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Record("Сдвинуть узел");
                    dragging = true;
                    TunnelNetwork.deferBuild = true;
                    var p = net.Local(nw);
                    if (snap) { p.x = Mathf.Round(p.x * 2f) * 0.5f; p.z = Mathf.Round(p.z * 2f) * 0.5f; p.y = Mathf.Round(p.y * 20f) / 20f; }
                    n.position = p;
                    Changed();
                }
            }
            var e = net.Edge(selEdge);
            if (e != null)
            {
                var A = net.Node(e.a); var B = net.Node(e.b);
                if (A == null || B == null) return;
                Vector3 a = net.World(A.position), b = net.World(B.position);
                Vector3 dir = (b - a).normalized;
                float len = Vector3.Distance(a, b);
                for (int i = 0; i < e.shafts.Count; i++)
                {
                    var s = e.shafts[i];
                    var p = a + dir * s.distance + Vector3.up * 0.3f;
                    Handles.color = Color.yellow;
                    EditorGUI.BeginChangeCheck();
                    var np = Handles.Slider(p, dir, HandleUtility.GetHandleSize(p) * 0.6f, Handles.ArrowHandleCap, 0f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Record("Сдвинуть метку");
                        dragging = true;
                        TunnelNetwork.deferBuild = true;
                        s.distance = Mathf.Clamp(Vector3.Dot(np - a, dir), 0f, len);
                        Changed();
                    }
                }
            }
        }

        // ------------------------------------------------------------------ drawing

        [DrawGizmo(GizmoType.NotInSelectionHierarchy)]
        static void DrawUnselected(TunnelNetwork net, GizmoType type)
        {
            if (!Schematic) return;
            net.EnsureLayout();
            DrawNetwork(net, false);
        }

        static void DrawNetwork(TunnelNetwork net, bool selected)
        {
            // room outlines
            Handles.color = new Color(1f, 0.85f, 0.2f, selected ? 0.9f : 0.5f);
            foreach (var rn in net.nodes)
            {
                if (rn.kind != GraphNodeKind.Room || rn.roomCorners == null || rn.roomCorners.Count < 3) continue;
                var rp = new Vector3[rn.roomCorners.Count + 1];
                for (int i = 0; i < rn.roomCorners.Count; i++) rp[i] = FloorWorld(net, rn, rn.roomCorners[i], 0.05f);
                rp[rn.roomCorners.Count] = rp[0];
                Handles.DrawAAPolyLine(2f, rp);
            }
            // corners cut off automatically where a wall was too narrow for its doorway
            Handles.color = new Color(1f, 0.5f, 0.1f, selected ? 0.9f : 0.5f);
            foreach (var kv in net.rooms)
            {
                var rc = kv.Value.corners;
                var rn = kv.Value.node;
                if (rc == null || rc.Count < 3 || rn.roomCorners == null || rc.Count == rn.roomCorners.Count) continue;
                var rp = new Vector3[rc.Count + 1];
                for (int i = 0; i < rc.Count; i++) rp[i] = FloorWorld(net, rn, rc[i], 0.08f);
                rp[rc.Count] = rp[0];
                Handles.DrawAAPolyLine(3f, rp);
            }
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            var zt = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            // laid-out centre lines (what will be built)
            foreach (var c in net.chains)
            {
                if (c.samples.Count < 2) continue;
                var pts = new Vector3[c.samples.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = net.World(c.samples[i] + Vector3.up * 0.05f);
                var col = c.type != null ? c.type.color : Color.white;
                Handles.color = new Color(col.r, col.g, col.b, 0.9f);
                Handles.DrawAAPolyLine(Schematic ? 5f : 3f, pts);
            }
            // graph lines
            foreach (var e in net.edges)
            {
                var A = net.Node(e.a); var B = net.Node(e.b);
                if (A == null || B == null) continue;
                var col = ColorOf(net, e);
                bool sel = selected && e.id == selEdge, hov = selected && e.id == hoverEdge;
                Handles.color = sel ? Color.cyan : hov ? Color.yellow : new Color(col.r, col.g, col.b, 0.45f);
                Handles.DrawAAPolyLine(sel || hov ? 6f : 2f, net.World(A.position), net.World(B.position));
                Vector3 a = net.World(A.position), b = net.World(B.position), dir = (b - a).normalized;
                foreach (var s in e.shafts)
                {
                    var p = a + dir * s.distance;
                    float hs = HandleUtility.GetHandleSize(p) * 0.08f;
                    Handles.color = s.type == ShaftType.Manhole ? new Color(1f, 0.3f, 0.3f) : Color.yellow;
                    Handles.DrawSolidDisc(p, Vector3.up, hs);
                }
            }
            // chambers
            foreach (var kv in net.chambers)
            {
                var n = kv.Value.node;
                var rot = net.transform.rotation * Quaternion.Euler(0f, kv.Value.yaw, 0f);
                var c = net.World(n.position) + Vector3.up * 0.05f;
                Vector3 hx = rot * Vector3.right * (n.chamberSize.x * 0.5f), hz = rot * Vector3.forward * (n.chamberSize.y * 0.5f);
                Handles.color = new Color(1f, 0.9f, 0.4f, 0.9f);
                Handles.DrawAAPolyLine(3f, c - hx - hz, c + hx - hz, c + hx + hz, c - hx + hz, c - hx - hz);
                if (n.chamberShaft) Handles.DrawSolidDisc(c, Vector3.up, HandleUtility.GetHandleSize(c) * 0.08f);
            }
            // nodes
            foreach (var n in net.nodes)
            {
                var p = net.World(n.position);
                float s = HandleUtility.GetHandleSize(p) * 0.09f;
                Handles.color = selected && n.id == selNode ? Color.cyan : selected && n.id == hoverNode ? Color.yellow : selected && n.id == penLast ? Color.green : Color.white;
                if (selected && n.id == hoverNode) s *= 1.4f;
                Handles.SphereHandleCap(0, p, Quaternion.identity, s, EventType.Repaint);
            }
            // trash counts while the trash tool is active
            if (selected && tool == Tool.Trash)
            {
                var ls = new GUIStyle(EditorStyles.boldLabel);
                ls.normal.textColor = new Color(0.85f, 0.65f, 0.3f);
                foreach (var e in net.edges)
                {
                    if (e.trash <= 0) continue;
                    var A = net.Node(e.a); var B = net.Node(e.b);
                    if (A == null || B == null) continue;
                    Handles.Label(net.World((A.position + B.position) * 0.5f) + Vector3.up * 0.6f, "мусор " + e.trash, ls);
                }
                foreach (var n in net.nodes)
                    if (n.trash > 0) Handles.Label(net.World(n.position) + Vector3.up * 0.9f, "мусор " + n.trash, ls);
            }
            // problems
            Handles.color = Color.red;
            foreach (var i in net.issues)
            {
                var p = net.World(i.position);
                Handles.DrawWireDisc(p, Vector3.up, HandleUtility.GetHandleSize(p) * 0.35f, 3f);
            }
            Handles.zTest = zt;
        }

        public static void PanelGUI(TunnelNetwork net)
        {
            var lib = net.library;
            GUILayout.BeginVertical(GUILayout.Width(250f));
            int nt = GUILayout.SelectionGrid((int)tool, ToolNames, 3, EditorStyles.miniButton);
            if (nt != (int)tool) { tool = (Tool)nt; penLast = null; SceneView.RepaintAll(); }
            GUILayout.Label(ToolHints[(int)tool], EditorStyles.wordWrappedMiniLabel, GUILayout.Height(42f));

            if (lib != null)
            {
                for (int i = 0; i < lib.types.Count; i++)
                {
                    var t = lib.types[i];
                    if (t == null) continue;
                    var bg = GUI.backgroundColor;
                    GUI.backgroundColor = t.color;
                    string label = (i == typeIndex ? "● " : "   ") + t.Label + "   (R " + t.MinRadius.ToString("0") + " м)";
                    if (GUILayout.Button(label, i == typeIndex ? EditorStyles.miniButtonMid : EditorStyles.miniButton)) typeIndex = i;
                    GUI.backgroundColor = bg;
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("+ Новый тип", EditorStyles.miniButton)) NewType(lib);
                if (lib.types.Count > 0 && GUILayout.Button("Настроить тип", EditorStyles.miniButton))
                    EditorUtility.OpenPropertyEditor(lib.types[Mathf.Clamp(typeIndex, 0, lib.types.Count - 1)]);
                GUILayout.EndHorizontal();
            }
            if (tool == Tool.Marker) markerType = (ShaftType)EditorGUILayout.EnumPopup("Вид метки", markerType);
            if (tool == Tool.Pen) penHeight = EditorGUILayout.FloatField("Высота нового узла", penHeight);
            if (tool == Tool.Trash) TrashPanel(net);
            GUILayout.BeginHorizontal();
            snap = GUILayout.Toggle(snap, "Сетка 1 м", EditorStyles.miniButton);
            bool sch = GUILayout.Toggle(Schematic, "Схема", EditorStyles.miniButton);
            if (sch != Schematic) Schematic = sch;
            if (GUILayout.Button("Сверху", EditorStyles.miniButton)) TopView(net);
            GUILayout.EndHorizontal();
            var style = new GUIStyle(EditorStyles.miniLabel);
            if (net.issues.Count > 0) style.normal.textColor = new Color(1f, 0.4f, 0.4f);
            GUILayout.Label("Узлов " + net.nodes.Count + ", линий " + net.edges.Count + ", камер " + net.chambers.Count +
                            (net.issues.Count > 0 ? ", проблем " + net.issues.Count + " (см. инспектор)" : ", проблем нет"), style);
            GUILayout.EndVertical();
        }

        /// <summary>Copy of the selected type as a new asset in the palette; its settings window opens right away.</summary>
        static void NewType(TunnelTypeLibrary lib)
        {
            var src = lib.types.Count > 0 ? lib.types[Mathf.Clamp(typeIndex, 0, lib.types.Count - 1)] : null;
            var t = src != null ? Object.Instantiate(src) : ScriptableObject.CreateInstance<TunnelType>();
            t.displayName = (src != null ? src.Label : "Тоннель") + " (новый)";
            TunnelAssets.EnsureFolder(TunnelGraphAssets.Dir + "/Types");
            AssetDatabase.CreateAsset(t, AssetDatabase.GenerateUniqueAssetPath(TunnelGraphAssets.Dir + "/Types/NewType.asset"));
            Undo.RecordObject(lib, "Новый тип тоннеля");
            lib.types.Add(t);
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            typeIndex = lib.types.Count - 1;
            EditorUtility.OpenPropertyEditor(t);
        }

        static void TopView(TunnelNetwork net)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            var b = new Bounds(net.transform.position, Vector3.one * 10f);
            foreach (var n in net.nodes) b.Encapsulate(net.World(n.position));
            view.orthographic = true;
            view.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            view.pivot = b.center;
            view.size = Mathf.Max(b.extents.x, b.extents.z) * 1.1f + 5f;
            view.Repaint();
        }

        // ------------------------------------------------------------------ inspector

        public override void OnInspectorGUI()
        {
            var net = (TunnelNetwork)target;
            serializedObject.Update();
            EditorGUILayout.HelpBox("Тоннели и камеры строятся из этого графа. Рисуйте инструментами в окне Scene (панель слева сверху). " +
                                    "Объекты внутри «_Generated» руками не правьте: они пересобираются.", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("library"), new GUIContent("Типы тоннелей"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("puddles"), new GUIContent("Лужи"));

            bool sch = EditorGUILayout.Toggle(new GUIContent("Режим «Схема»", "Тоннели скрыты, видны только линии графа. Меши пересоберутся при выключении."), Schematic);
            if (sch != Schematic) Schematic = sch;

            net.EnsureLayout();
            EditorGUILayout.LabelField("Узлов " + net.nodes.Count + ", линий " + net.edges.Count + ", тоннелей " + net.chains.Count + ", камер " + net.chambers.Count);

            if (net.issues.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Проблемы (" + net.issues.Count + ")", EditorStyles.boldLabel);
                issueScroll = EditorGUILayout.BeginScrollView(issueScroll, GUILayout.MaxHeight(160f));
                foreach (var i in net.issues)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(i.message, EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button("Показать", GUILayout.Width(70f)))
                    {
                        selNode = i.nodeId; selEdge = i.edgeId; tool = Tool.Select;
                        var view = SceneView.lastActiveSceneView;
                        if (view != null) view.Frame(new Bounds(net.World(i.position), Vector3.one * 12f), false);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space();
            int ni = net.nodes.FindIndex(n => n.id == selNode);
            int ei = net.edges.FindIndex(x => x.id == selEdge);
            if (ni >= 0)
            {
                EditorGUILayout.LabelField("Выбранный узел", EditorStyles.boldLabel);
                var node = net.nodes[ni];
                bool chamber = net.IsChamber(selNode);
                if (node.kind == GraphNodeKind.Exit)
                    EditorGUILayout.HelpBox("Выход наружу: конец тоннеля открыт, за ним дневной свет. Свет из выхода светит вдоль тоннеля, у выхода картинка чуть темнеет (глаз привыкает). Силу задаёт Exit Brightness ниже, общие настройки в библиотеке типов.", MessageType.None);
                else
                EditorGUILayout.HelpBox(node.kind == GraphNodeKind.Room ? "Многоугольная комната. Углы двигайте жёлтыми точками в окне Scene, зелёная точка на середине стены добавляет угол, Shift+клик по углу убирает его. Колонны (голубые) тоже двигаются мышью." :
                                        chamber ? "В этом узле камера-развязка." :
                                        net.joins.ContainsKey(selNode) ? (net.joins[selNode].back > 0f ? "Примыкание: меньший тоннель выходит из боковой стены у конца большего." : net.joins[selNode].reducer ? "Переход: торец большего тоннеля - стенка с проёмом, меньший идёт дальше. Высоту меньшего задаёт Join Lift." : "Прямая врезка: тоннели поменьше входят в стену главного.") : "Обычный узел (поворот или конец тоннеля).", MessageType.None);
                if (node.kind == GraphNodeKind.Room)
                {
                    if (GUILayout.Button("Убрать комнату")) { Undo.RecordObject(net, "Комната"); node.kind = GraphNodeKind.Auto; net.MarkDirty(); serializedObject.Update(); }
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("+ Колонна квадратная")) AddColumn(net, node, ColumnShape.Square);
                    if (GUILayout.Button("+ Колонна круглая")) AddColumn(net, node, ColumnShape.Round);
                    GUILayout.EndHorizontal();
                    serializedObject.Update();
                }
                else if (node.kind == GraphNodeKind.Chamber)
                {
                    if (GUILayout.Button("Убрать камеру (тоннели соединятся напрямую)")) { Undo.RecordObject(net, "Камера"); node.kind = GraphNodeKind.Auto; net.MarkDirty(); serializedObject.Update(); }
                }
                else if (node.kind == GraphNodeKind.Exit)
                {
                    if (GUILayout.Button("Убрать выход (снова тупик со стенкой)")) { Undo.RecordObject(net, "Выход"); node.kind = GraphNodeKind.Auto; net.MarkDirty(); serializedObject.Update(); }
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Сделать камерой-развязкой")) { Undo.RecordObject(net, "Камера"); node.kind = GraphNodeKind.Chamber; net.MarkDirty(); serializedObject.Update(); }
                    if (GUILayout.Button("Сделать комнатой"))
                    {
                        Undo.RecordObject(net, "Комната");
                        node.kind = GraphNodeKind.Room;
                        if (node.roomCorners == null || node.roomCorners.Count < 3) node.roomCorners = GraphNode.DefaultRoom();
                        net.MarkDirty();
                        serializedObject.Update();
                    }
                    GUILayout.EndHorizontal();
                    if (GUILayout.Button("Сделать выходом наружу (дневной свет)"))
                    {
                        Undo.RecordObject(net, "Выход");
                        node.kind = GraphNodeKind.Exit;
                        if (node.exitBrightness <= 0f) node.exitBrightness = 1f;
                        net.MarkDirty();
                        serializedObject.Update();
                    }
                }
                DrawChildren(serializedObject.FindProperty("nodes").GetArrayElementAtIndex(ni), "id");
            }
            else if (ei >= 0)
            {
                var e = net.edges[ei];
                EditorGUILayout.LabelField("Выбранная линия", EditorStyles.boldLabel);
                var A = net.Node(e.a); var B = net.Node(e.b);
                if (A != null && B != null) EditorGUILayout.LabelField("Длина по прямой " + Vector3.Distance(A.position, B.position).ToString("0.0") + " м");
                DrawChildren(serializedObject.FindProperty("edges").GetArrayElementAtIndex(ei), "id", "a", "b");
            }
            else EditorGUILayout.LabelField("Выберите узел или линию инструментом «Выбор».", EditorStyles.miniLabel);

            if (serializedObject.ApplyModifiedProperties()) net.MarkDirty();

            ReplaceTypeGUI(net);

            TunnelAnchorHooks.NetworkGUI(net);

            EditorGUILayout.Space();
            if (GUILayout.Button("Перестроить всё")) { net.ForceRebuild(); if (!Schematic) net.Build(); }
        }

        /// <summary>Every line of one type gets another type; the graph then rebuilds by the new type's rules.</summary>
        static void ReplaceTypeGUI(TunnelNetwork net)
        {
            var lib = net.library;
            if (lib == null || lib.types.Count < 2) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Заменить тип во всём графе", EditorStyles.boldLabel);
            var labels = new string[lib.types.Count];
            for (int i = 0; i < labels.Length; i++) labels[i] = lib.types[i] != null ? lib.types[i].Label : "—";
            replaceFrom = Mathf.Clamp(EditorGUILayout.Popup("Был тип", replaceFrom, labels), 0, labels.Length - 1);
            replaceTo = Mathf.Clamp(EditorGUILayout.Popup("Станет", replaceTo, labels), 0, labels.Length - 1);
            var from = lib.types[replaceFrom];
            var to = lib.types[replaceTo];
            int count = 0;
            foreach (var e in net.edges) if (net.TypeOf(e) == from) count++;
            using (new EditorGUI.DisabledScope(count == 0 || from == to || to == null))
            {
                if (GUILayout.Button("Заменить (" + count + " линий)"))
                {
                    Undo.RecordObject(net, "Заменить тип тоннеля");
                    foreach (var e in net.edges) if (net.TypeOf(e) == from) e.type = to;
                    EditorUtility.SetDirty(net);
                    net.MarkDirty();
                }
            }
        }

        static void DrawChildren(SerializedProperty prop, params string[] skip)
        {
            var end = prop.GetEndProperty();
            var it = prop.Copy();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                if (System.Array.IndexOf(skip, it.name) >= 0) continue;
                EditorGUILayout.PropertyField(it, true);
            }
        }
    }
}
