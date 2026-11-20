using System.Collections.Generic;
using System.Reflection;
using CarX.Level;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CarX.EditorTools
{
    /// <summary>CarX → Граффити: pick a picture, switch the spray on and click in the Scene view.
    /// Shift+wheel = size, Ctrl+wheel = rotation, Esc or right click = spray off.</summary>
    public class GraffitiWindow : EditorWindow
    {
        const string PrefKey = "CarX.Graffiti.";
        const int Thumb = 76;

        // settings for the next graffiti (remembered between sessions)
        static Texture2D current;
        static float size = 1.5f, angle, opacity = 1f, wear;
        static bool spray, flip;

        List<Texture2D> images = new List<Texture2D>();
        Vector2 scroll;

        static readonly List<GraffitiWindow> open = new List<GraffitiWindow>();

        [MenuItem("CarX/Граффити", false, 40)]
        public static void Open()
        {
            var w = GetWindow<GraffitiWindow>();
            w.titleContent = new GUIContent("Граффити");
            w.minSize = new Vector2(260, 300);
            w.Show();
        }

        public static void RepaintAll() { foreach (var w in open) if (w != null) { w.Reload(); w.Repaint(); } }

        void OnEnable()
        {
            open.Add(this);
            size = EditorPrefs.GetFloat(PrefKey + "size", 1.5f);
            opacity = EditorPrefs.GetFloat(PrefKey + "opacity", 1f);
            wear = EditorPrefs.GetFloat(PrefKey + "wear", 0f);
            if (current == null)
                current = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(PrefKey + "image", "")));
            Reload();
            EditorApplication.projectChanged += Reload;
            SceneView.duringSceneGui -= OnScene;
            SceneView.duringSceneGui += OnScene;
        }

        void OnDisable()
        {
            open.Remove(this);
            EditorApplication.projectChanged -= Reload;
            if (open.Count == 0)
            {
                SceneView.duringSceneGui -= OnScene;
                SetSpray(false);
            }
            Save();
        }

        static void Save()
        {
            EditorPrefs.SetFloat(PrefKey + "size", size);
            EditorPrefs.SetFloat(PrefKey + "opacity", opacity);
            EditorPrefs.SetFloat(PrefKey + "wear", wear);
            if (current != null) EditorPrefs.SetString(PrefKey + "image", AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(current)));
        }

        void Reload()
        {
            images = GraffitiLibrary.Images();
            if (current != null && !images.Contains(current)) current = null;
            Repaint();
        }

        // ------------------------------------------------------------------ window

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Папка с картинками"))
                {
                    GraffitiLibrary.EnsureFolders();
                    var f = AssetDatabase.LoadAssetAtPath<Object>(GraffitiLibrary.Root);
                    EditorGUIUtility.PingObject(f);
                    Selection.activeObject = f;
                }
                if (GUILayout.Button("Обновить", GUILayout.Width(80))) { GraffitiLibrary.Sync(); Reload(); }
            }

            if (images.Count == 0)
            {
                EditorGUILayout.HelpBox("Положите PNG с прозрачным фоном в папку " + GraffitiLibrary.Root + ". Они появятся здесь сами.", MessageType.Info);
                if (GUILayout.Button("Создать 3 тестовые картинки")) { GraffitiLibrary.CreateTestImages(); Reload(); }
            }

            // picture grid
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            int cols = Mathf.Max(1, (int)((position.width - 20) / (Thumb + 6)));
            for (int i = 0; i < images.Count; i += cols)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < Mathf.Min(i + cols, images.Count); j++) Cell(images[j]);
                    GUILayout.FlexibleSpace();
                }
            }
            EditorGUILayout.EndScrollView();

            // sheets
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                float gap = EditorGUILayout.Slider(new GUIContent("Зазор склейки, %", "Листы режутся автоматически: части рисунка ближе этого зазора считаются одним граффити. Положите лист в Graffiti или Graffiti/_Sheets."), GraffitiSheet.GapPercent, 0.3f, 8f);
                if (EditorGUI.EndChangeCheck()) GraffitiSheet.GapPercent = gap;
                if (GUILayout.Button("Нарезать листы заново", GUILayout.Width(150)))
                {
                    int n = GraffitiSheet.ProcessAll();
                    GraffitiLibrary.Sync();
                    Reload();
                    ShowNotification(new GUIContent(n > 0 ? "Нарезано граффити: " + n : "В _Sheets нет листов"));
                }
            }

            // settings for the next graffiti
            EditorGUI.BeginChangeCheck();
            size = EditorGUILayout.Slider(new GUIContent("Ширина, м", "Shift + колесо мыши в окне Scene"), size, 0.1f, 8f);
            angle = EditorGUILayout.Slider(new GUIContent("Поворот, °", "Ctrl + колесо мыши в окне Scene"), angle, -180f, 180f);
            opacity = EditorGUILayout.Slider("Прозрачность", opacity, 0f, 1f);
            wear = GraffitiDecal.WearStep(EditorGUILayout.Slider(new GUIContent("Потёртость", "0 = свежее, 1 = почти стёрлось"), wear, 0f, 1f));
            flip = EditorGUILayout.Toggle("Отразить", flip);
            if (EditorGUI.EndChangeCheck()) { Save(); SceneView.RepaintAll(); }

            GUI.enabled = current != null;
            var old = GUI.backgroundColor;
            if (spray) GUI.backgroundColor = new Color(1f, 0.55f, 0.3f);
            if (GUILayout.Button(spray ? "Спрей ВКЛ (Esc — выключить)" : "Включить спрей", GUILayout.Height(32))) SetSpray(!spray);
            GUI.backgroundColor = old;
            GUI.enabled = true;
            EditorGUILayout.HelpBox(
                "ЛКМ в окне Scene — нанести.\nShift + колесо — размер, Ctrl + колесо — поворот.\nEsc или ПКМ — выключить спрей.\n" +
                "В тоннеле граффити едет вместе с тоннелем, в остальных местах — вместе с объектом, на который нанесено.", MessageType.None);

            // selected graffiti: swap picture
            var sel = Selected();
            if (sel.Count > 0 && current != null && GUILayout.Button("Поставить выбранную картинку выделенным граффити (" + sel.Count + ")"))
            {
                foreach (var g in sel)
                {
                    Undo.RecordObject(g, "Сменить картинку");
                    g.image = current;
                    g.Refresh();
                }
            }

            var missing = GraffitiLibrary.Missing();
            if (missing.Count > 0)
            {
                EditorGUILayout.HelpBox("Граффити без картинки (файл удалили): " + missing.Count, MessageType.Warning);
                if (GUILayout.Button("Выделить их")) Selection.objects = missing.ConvertAll(g => (Object)g.gameObject).ToArray();
            }
        }

        void Cell(Texture2D img)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Thumb + 2)))
            {
                var r = GUILayoutUtility.GetRect(Thumb, Thumb, GUILayout.Width(Thumb), GUILayout.Height(Thumb));
                bool on = img == current;
                EditorGUI.DrawRect(r, on ? new Color(1f, 0.55f, 0.2f) : new Color(0.16f, 0.16f, 0.16f));
                var inner = new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6);
                EditorGUI.DrawRect(inner, new Color(0.32f, 0.32f, 0.32f));   // grey so white and black paint both show
                GUI.DrawTexture(inner, img, ScaleMode.ScaleToFit, true);
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    current = img;
                    Save();
                    if (Event.current.clickCount == 2) SetSpray(true);
                    Event.current.Use();
                    Repaint();
                    SceneView.RepaintAll();
                }
                GUILayout.Label(img.name, EditorStyles.miniLabel, GUILayout.Width(Thumb));
            }
        }

        static List<GraffitiDecal> Selected()
        {
            var list = new List<GraffitiDecal>();
            foreach (var go in Selection.gameObjects)
            {
                var g = go.GetComponent<GraffitiDecal>();
                if (g != null) list.Add(g);
            }
            return list;
        }

        static void SetSpray(bool on)
        {
            spray = on && current != null;
            if (!spray) KillPreview();
            foreach (var w in open) if (w != null) w.Repaint();
            SceneView.RepaintAll();
        }

        // ------------------------------------------------------------------ spraying in the Scene view

        static GameObject preview;
        static Vector2 lastMouse = new Vector2(-1, -1);
        static bool lastHit;
        static Vector3 lastPos, lastNormal;
        static GameObject lastGo;
        static readonly int Hint = "CarXGraffitiSpray".GetHashCode();

        static void OnScene(SceneView view)
        {
            if (!spray || current == null || EditorApplication.isPlaying) { KillPreview(); return; }
            var e = Event.current;
            int id = GUIUtility.GetControlID(Hint, FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);   // clicks spray instead of selecting

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { SetSpray(false); e.Use(); return; }
            if (e.type == EventType.MouseDown && e.button == 1 && !e.alt) { SetSpray(false); e.Use(); return; }
            if (e.type == EventType.ScrollWheel && (e.shift || e.control))
            {
                float d = Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
                if (e.shift) size = Mathf.Clamp(size * (d > 0 ? 1f / 1.08f : 1.08f), 0.1f, 8f);
                else angle = Mathf.Repeat(angle + (d > 0 ? -5f : 5f) + 180f, 360f) - 180f;
                Save();
                RepaintAll();
                e.Use();
            }

            bool mouse = e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseDown || e.type == EventType.ScrollWheel;
            if (mouse || (e.type == EventType.Layout && e.mousePosition != lastMouse))
            {
                lastMouse = e.mousePosition;
                lastHit = Raycast(e.mousePosition, out lastPos, out lastNormal, out lastGo);
            }
            Vector3 pos = lastPos, normal = lastNormal;
            GameObject hitGo = lastGo;
            bool hit = lastHit;
            if (!hit) { if (preview != null) preview.SetActive(false); }
            else
            {
                var rot = Orientation(normal, view.camera, angle);
                ShowPreview(pos, rot);
                DrawOutline(pos, rot);
                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
                {
                    Place(current, pos, rot, hitGo, size, opacity, wear, flip);
                    e.Use();
                }
            }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) view.Repaint();
        }

        /// <summary>Upright on walls (picture "up" = world up), on floors and ceilings it faces the viewer.</summary>
        public static Quaternion Orientation(Vector3 normal, Camera cam, float rollDeg)
        {
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < 0.05f && cam != null) up = Vector3.ProjectOnPlane(cam.transform.up, normal);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            return Quaternion.LookRotation(-normal, up.normalized) * Quaternion.Euler(0f, 0f, rollDeg);
        }

        static void ShowPreview(Vector3 pos, Quaternion rot)
        {
            if (preview == null)
            {
                preview = new GameObject("_CarX_GraffitiPreview") { hideFlags = HideFlags.HideAndDontSave };
                preview.AddComponent<DecalProjector>();
            }
            preview.SetActive(true);
            preview.transform.SetPositionAndRotation(pos, rot);
            var p = preview.GetComponent<DecalProjector>();
            var m = GraffitiLibrary.MaterialFor(current, wear);
            if (p.material != m) p.material = m;
            float h = current.width > 0 ? size * current.height / current.width : size;
            p.size = new Vector3(size, h, 0.4f);
            p.pivot = Vector3.zero;
            p.uvScale = new Vector2(flip ? -1f : 1f, 1f);
            p.uvBias = new Vector2(flip ? 1f : 0f, 0f);
            p.fadeFactor = Mathf.Max(0.35f, opacity * 0.8f);
        }

        static void KillPreview()
        {
            if (preview != null) Object.DestroyImmediate(preview);
            preview = null;
        }

        static void DrawOutline(Vector3 pos, Quaternion rot)
        {
            float h = current.width > 0 ? size * current.height / current.width : size;
            Vector3 r = rot * Vector3.right * size * 0.5f, u = rot * Vector3.up * h * 0.5f;
            var c = Handles.color;
            Handles.color = new Color(1f, 0.6f, 0.2f, 0.9f);
            Handles.DrawAAPolyLine(2.5f, pos - r - u, pos + r - u, pos + r + u, pos - r + u, pos - r - u);
            Handles.DrawAAPolyLine(2f, pos, pos + u * 0.6f);   // "up" of the picture
            Handles.color = c;
        }

        // ------------------------------------------------------------------ hit test: what you see, not just colliders

        static MethodInfo intersectRayMesh;

        public static bool Raycast(Vector2 guiPos, out Vector3 pos, out Vector3 normal, out GameObject go)
        {
            var ray = HandleUtility.GUIPointToWorldRay(guiPos);
            pos = normal = Vector3.zero;
            go = HandleUtility.PickGameObject(guiPos, false);
            if (go != null && go.name.StartsWith("_CarX_Graffiti")) go = null;
            if (go != null)
            {
                var mf = go.GetComponent<MeshFilter>();
                RaycastHit h;
                if (mf != null && mf.sharedMesh != null && RayMesh(ray, mf, out h))
                {
                    pos = h.point;
                    normal = h.normal.sqrMagnitude > 0.5f ? h.normal.normalized : -ray.direction;
                    if (Vector3.Dot(normal, ray.direction) > 0f) normal = -normal;   // inside tunnels: face the viewer
                    return true;
                }
            }
            // terrains, skinned meshes, objects without a mesh filter: physics
            RaycastHit best = default(RaycastHit);
            float bestD = float.MaxValue;
            foreach (var h in Physics.RaycastAll(ray, 2000f, ~(1 << 2), QueryTriggerInteraction.Ignore))
            {
                if (!h.collider.gameObject.scene.isLoaded) continue;
                if (h.distance < bestD) { bestD = h.distance; best = h; }
            }
            if (bestD == float.MaxValue) return false;
            pos = best.point;
            normal = Vector3.Dot(best.normal, ray.direction) > 0f ? -best.normal : best.normal;
            go = best.collider.gameObject;
            return true;
        }

        static bool RayMesh(Ray ray, MeshFilter mf, out RaycastHit hit)
        {
            hit = default(RaycastHit);
            if (intersectRayMesh == null)
                intersectRayMesh = typeof(HandleUtility).GetMethod("IntersectRayMesh", BindingFlags.Static | BindingFlags.NonPublic);
            if (intersectRayMesh == null) return false;
            var args = new object[] { ray, mf.sharedMesh, mf.transform.localToWorldMatrix, null };
            bool ok = (bool)intersectRayMesh.Invoke(null, args);
            if (ok) hit = (RaycastHit)args[3];
            return ok;
        }

        // ------------------------------------------------------------------ placing

        /// <summary>Creates a graffiti and attaches it: tunnels → the network's Props + TunnelAnchor;
        /// generated geometry (canal, standalone tunnels, rooms) → a «Граффити» child of the generator; anything else → the hit object.</summary>
        public static GraffitiDecal Place(Texture2D img, Vector3 pos, Quaternion rot, GameObject hitGo, float width, float alpha, float wearAmount, bool flipped)
        {
            if (img == null) return null;
            TunnelNetwork net;
            var parent = ParentFor(hitGo, out net);
            var go = new GameObject("Граффити " + img.name);
            Undo.RegisterCreatedObjectUndo(go, "Граффити");
            if (parent != null && parent.gameObject.scene != go.scene) UnityEditor.SceneManagement.EditorSceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
            go.transform.SetPositionAndRotation(pos, rot);
            if (parent != null) go.transform.SetParent(parent, true);
            go.AddComponent<DecalProjector>();
            var g = go.AddComponent<GraffitiDecal>();
            g.image = img;
            g.size = width;
            g.opacity = alpha;
            g.wear = GraffitiDecal.WearStep(wearAmount);
            g.flip = flipped;
            g.depth = Mathf.Clamp(width * 0.25f, 0.3f, 1f);   // wide pieces wrap over curved walls
            g.Refresh();
            if (net != null)
            {
                var a = go.AddComponent<TunnelAnchor>();
                a.network = net;
                a.surface = AnchorSurface.Auto;
                a.Bind();
            }
            return g;
        }

        public static Transform ParentFor(GameObject hit, out TunnelNetwork net)
        {
            net = null;
            if (hit == null) return null;
            net = hit.GetComponentInParent<TunnelNetwork>();
            if (net != null) return TunnelAnchorHooks.EnsureProps(net);

            // generators rebuild their children: hang the graffiti next to them, not inside
            Transform owner = null;
            for (var t = hit.transform; t != null; t = t.parent)
            {
                if (t.GetComponent<CanalEntrance>() != null || t.GetComponent<TunnelPath>() != null ||
                    t.GetComponent<TunnelJunction>() != null || t.GetComponent<TunnelRoom>() != null) owner = t;
                else if (owner == null && t.name.StartsWith("_Generated") && t.parent != null) owner = t.parent;
            }
            if (owner == null) return hit.transform;
            var folder = owner.Find("Граффити");
            if (folder == null)
            {
                folder = new GameObject("Граффити").transform;
                Undo.RegisterCreatedObjectUndo(folder.gameObject, "Папка граффити");
                folder.SetParent(owner, false);
            }
            return folder;
        }
    }
}
