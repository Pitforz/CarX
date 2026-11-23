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
            SceneView.beforeSceneGui -= BeforeScene;
            SceneView.beforeSceneGui += BeforeScene;
        }

        void OnDisable()
        {
            open.Remove(this);
            EditorApplication.projectChanged -= Reload;
            if (open.Count == 0)
            {
                SceneView.duringSceneGui -= OnScene;
                SceneView.beforeSceneGui -= BeforeScene;
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

            bool mouse = e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.ScrollWheel;
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
            }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) view.Repaint();
        }

        static bool downTaken;

        /// <summary>Automation (bridge: call CarX.EditorTools.GraffitiWindow.ClickTest): sends a mouse move + click to the Scene view centre
        /// with the spray on and reports whether a graffiti appeared; the new graffiti is undone right away.</summary>
        static string ClickTest()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return "no scene view";
            Open();
            var imgs = GraffitiLibrary.Images();
            if (imgs.Count == 0) return "no pictures";
            if (current == null) current = imgs[0];
            SetSpray(true);
            int before = Object.FindObjectsByType<GraffitiDecal>(FindObjectsInactive.Include).Length;
            var c = new Vector2(view.position.width * 0.5f, view.position.height * 0.5f);
            view.SendEvent(new Event { type = EventType.MouseMove, mousePosition = c });
            view.SendEvent(new Event { type = EventType.Layout, mousePosition = c });
            bool hitBefore = lastHit;
            view.SendEvent(new Event { type = EventType.MouseDown, button = 0, clickCount = 1, mousePosition = c });
            view.SendEvent(new Event { type = EventType.MouseUp, button = 0, clickCount = 1, mousePosition = c });
            int after = Object.FindObjectsByType<GraffitiDecal>(FindObjectsInactive.Include).Length;
            string where = lastGo != null ? lastGo.name : "-";
            if (after > before) Undo.PerformUndo();
            int undone = Object.FindObjectsByType<GraffitiDecal>(FindObjectsInactive.Include).Length;
            SetSpray(false);
            return "hit " + hitBefore + " on " + where + ", graffiti " + before + " -> " + after + " -> after undo " + undone;
        }

        /// <summary>Clicks, keys and the wheel are taken here, before any other Scene view tool (the tunnel graph editor also listens
        /// to clicks and would otherwise eat the click). The spot under the cursor comes from the last mouse move.</summary>
        static void BeforeScene(SceneView view)
        {
            if (!spray || current == null || EditorApplication.isPlaying) { downTaken = false; return; }
            var e = Event.current;
            switch (e.type)
            {
                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Escape) { SetSpray(false); e.Use(); }
                    break;
                case EventType.MouseDown:
                    if (e.alt) break;   // Alt + mouse = orbit the camera as usual
                    if (e.button == 1) { SetSpray(false); e.Use(); break; }
                    if (e.button != 0) break;
                    if (lastHit && (e.mousePosition - lastMouse).sqrMagnitude < 36f)
                        Place(current, lastPos, Orientation(lastNormal, view.camera, angle), lastGo, size, opacity, wear, flip);
                    downTaken = true;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (downTaken && e.button == 0) e.Use();
                    break;
                case EventType.MouseUp:
                    if (downTaken && e.button == 0) { downTaken = false; e.Use(); }
                    break;
                case EventType.ScrollWheel:
                    if (!e.shift && !e.control) break;
                    float d = Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
                    if (e.shift) size = Mathf.Clamp(size * (d > 0 ? 1f / 1.08f : 1.08f), 0.1f, 8f);
                    else angle = Mathf.Repeat(angle + (d > 0 ? -5f : 5f) + 180f, 360f) - 180f;
                    Save();
                    RepaintAll();
                    view.Repaint();
                    e.Use();
                    break;
            }
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
            p.size = new Vector3(size, h, Mathf.Clamp(size * 0.2f, 0.25f, 0.8f));
            p.startAngleFade = 40f;
            p.endAngleFade = 65f;
            p.renderingLayerMask = uint.MaxValue;
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

        public static bool Raycast(Vector2 guiPos, out Vector3 pos, out Vector3 normal, out GameObject go)
        {
            var picked = HandleUtility.PickGameObject(guiPos, false);
            return Cast(HandleUtility.GUIPointToWorldRay(guiPos), picked, out pos, out normal, out go);
        }

        /// <summary>First surface that is actually visible along the ray. Tunnels are one-sided meshes: from outside, the wall nearest to the
        /// camera has its back to the viewer and is not drawn, so the ray passes through it and lands on the inner wall behind.
        /// `picked` is what the Scene view picked under the cursor (may be null).</summary>
        public static bool Cast(Ray ray, GameObject picked, out Vector3 pos, out Vector3 normal, out GameObject go)
        {
            pos = normal = Vector3.zero;
            go = picked;
            if (go != null && go.name.StartsWith("_CarX_Graffiti")) go = null;
            if (go != null)
            {
                var mf = go.GetComponent<MeshFilter>();
                RaycastHit h;
                if (mf != null && mf.sharedMesh != null && RayMeshVisible(ray, mf, out h))
                {
                    pos = h.point;
                    normal = h.normal;
                    return true;
                }
            }
            // terrains, skinned meshes, objects without a mesh filter, or the picked object has no visible face here: physics
            foreach (var h in SortedHits(ray))
            {
                if (!h.collider.gameObject.scene.isLoaded) continue;
                Vector3 n = FaceNormal(h);
                if (!Visible(n, ray.direction, CullOf(h.collider.gameObject))) continue;
                pos = h.point;
                normal = Vector3.Dot(n, ray.direction) > 0f ? -n : n;
                go = h.collider.gameObject;
                return true;
            }
            go = null;
            return false;
        }

        static RaycastHit[] SortedHits(Ray ray)
        {
            var hits = Physics.RaycastAll(ray, 2000f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            return hits;
        }

        /// <summary>Which faces the material draws: 0 = both sides, 1 = front faces only (normal towards the viewer), 2 = back faces only.</summary>
        static int CullOf(GameObject go)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return 1;
            var m = r.sharedMaterial;
            if (m != null && m.HasProperty("_Cull"))
            {
                int c = Mathf.RoundToInt(m.GetFloat("_Cull"));   // UnityEngine.Rendering.CullMode: Off 0, Front 1, Back 2
                return c == 0 ? 0 : c == 1 ? 2 : 1;
            }
            return 1;
        }

        static bool Visible(Vector3 faceNormal, Vector3 dir, int sides)
        {
            float d = Vector3.Dot(faceNormal, dir);
            return sides == 0 || (sides == 1 ? d < 0f : d > 0f);
        }

        /// <summary>Normal of the triangle that was hit, from its winding (what decides which side is drawn).</summary>
        static Vector3 FaceNormal(RaycastHit h)
        {
            var mc = h.collider as MeshCollider;
            if (mc != null && mc.sharedMesh != null && mc.sharedMesh.isReadable && h.triangleIndex >= 0)
                return TriNormal(mc.sharedMesh, h.triangleIndex, mc.transform);
            return h.normal;
        }

        static Vector3 TriNormal(Mesh mesh, int tri, Transform t)
        {
            var idx = mesh.triangles;
            if (tri * 3 + 2 >= idx.Length) return Vector3.zero;
            var v = mesh.vertices;
            Vector3 a = v[idx[tri * 3]], b = v[idx[tri * 3 + 1]], c = v[idx[tri * 3 + 2]];
            return t.TransformDirection(Vector3.Cross(b - a, c - a)).normalized;   // Unity: clockwise = front
        }

        static MethodInfo intersectRayMesh;

        /// <summary>First drawn face of the mesh along the ray: faces seen from behind are skipped (the ray goes on through them).</summary>
        static bool RayMeshVisible(Ray ray, MeshFilter mf, out RaycastHit hit)
        {
            int sides = CullOf(mf.gameObject);
            float travelled = 0f;
            Vector3 origin = ray.origin;
            for (int i = 0; i < 24; i++)
            {
                if (!RayMesh(new Ray(origin, ray.direction), mf, out hit)) return false;
                Vector3 n = hit.normal;
                if (mf.sharedMesh.isReadable && hit.triangleIndex >= 0) n = TriNormal(mf.sharedMesh, hit.triangleIndex, mf.transform);
                if (n.sqrMagnitude < 0.25f) n = hit.normal;
                travelled += hit.distance;
                if (Visible(n, ray.direction, sides))
                {
                    hit.normal = Vector3.Dot(n, ray.direction) > 0f ? -n : n;   // double-sided: face the viewer
                    hit.distance = travelled;
                    return true;
                }
                origin = hit.point + ray.direction * 0.02f;
                travelled += 0.02f;
            }
            hit = default(RaycastHit);
            return false;
        }

        internal static bool RayMesh(Ray ray, MeshFilter mf, out RaycastHit hit)
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
            g.depth = Mathf.Clamp(width * 0.2f, 0.25f, 0.8f);
            g.Refresh();
            float fit;
            if (FitDepth(g, out fit)) { g.depth = fit; g.Refresh(); }   // as thin as the surface allows: no smear into openings and corners
            if (net != null)
            {
                var a = go.AddComponent<TunnelAnchor>();
                a.network = net;
                a.surface = AnchorSurface.Auto;
                a.Bind();
            }
            return g;
        }

        /// <summary>Measures how far the surface under the graffiti departs from the graffiti plane (curved tunnel walls, bumps)
        /// and returns a paint layer just thick enough to cover it. A flat wall gets a few centimetres, so paint cannot run
        /// into a tunnel mouth or round a corner. False if the surface could not be measured (no colliders).</summary>
        public static bool FitDepth(GraffitiDecal g, out float depth)
        {
            depth = g.depth;
            Physics.SyncTransforms();
            var t = g.transform;
            Vector3 n = -t.forward, right = t.right, up = t.up;
            float w = g.size, h = g.Height;
            const float lift = 0.5f, near = 0.35f;   // surfaces farther than `near` from the graffiti plane are other surfaces (behind an opening, a ledge)
            float lo = float.MaxValue, hi = float.MinValue;
            int found = 0, total = 0;
            for (int iy = 0; iy < 7; iy++)
                for (int ix = 0; ix < 9; ix++)
                {
                    total++;
                    Vector3 o = t.position + right * w * (ix / 8f - 0.5f) + up * h * (iy / 6f - 0.5f) + n * lift;
                    float best = float.MaxValue;
                    foreach (var hit in Physics.RaycastAll(o, -n, lift * 2f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                    {
                        if (!hit.collider.gameObject.scene.IsValid() || hit.collider.gameObject.scene != g.gameObject.scene) continue;
                        if (Vector3.Dot(hit.normal, n) < 0.3f) continue;   // only the painted surface, not walls at an angle to it
                        if (hit.distance < best) best = hit.distance;
                    }
                    if (best == float.MaxValue) continue;
                    float dev = lift - best;   // + = surface towards the viewer, - = behind the graffiti plane
                    if (Mathf.Abs(dev) > near) continue;
                    lo = Mathf.Min(lo, dev);
                    hi = Mathf.Max(hi, dev);
                    found++;
                }
            if (found < total / 4) return false;
            depth = Mathf.Clamp(2f * Mathf.Max(Mathf.Abs(lo), Mathf.Abs(hi)) + 0.04f, 0.04f, 1.2f);
            return true;
        }

        /// <summary>Re-fits the paint thickness of every graffiti in the open scenes (bridge: call CarX.EditorTools.GraffitiWindow.FitAll).</summary>
        [MenuItem("CarX/Граффити: подогнать толщину слоя у всех")]
        public static string FitAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("CarX Graffiti: выйдите из Play, потом подгоняйте толщину."); return "play mode"; }
            int fitted = 0, skipped = 0;
            foreach (var g in Object.FindObjectsByType<GraffitiDecal>(FindObjectsInactive.Include))
            {
                float d;
                if (!FitDepth(g, out d)) { skipped++; continue; }
                Undo.RecordObject(g, "Толщина граффити");
                g.depth = d;
                g.Refresh();
                EditorUtility.SetDirty(g);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(g.gameObject.scene);
                fitted++;
            }
            string r = "подогнано " + fitted + ", не измерено (нет коллайдеров под граффити) " + skipped;
            Debug.Log("CarX Graffiti: " + r);
            return r;
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
