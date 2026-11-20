using System.Collections.Generic;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.EditorTools
{
    /// <summary>
    /// Stage 5, the city around the canal: a steel transmission line across the channel, a casino pylon sign and a lawyer
    /// billboard behind the tunnel, and placeholder Strip towers far behind it (replace them with real models).
    /// Everything is ordinary scene objects under one root: move, copy or delete them by hand.
    /// </summary>
    public static partial class CanalEntranceBuilder
    {
        public const string CityRootName = "Город: ЛЭП, вывески, башни (двигайте, удаляйте)";

        /// <summary>Bridge entry point: (re)builds the city root and saves the scene.</summary>
        public static string AddCity()
        {
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c == null) return "no canal";
            var scene = c.gameObject.scene;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == CityRootName) Object.DestroyImmediate(go);
            var root = BuildCity(c);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "city built: " + root.GetComponentsInChildren<MeshRenderer>(true).Length + " renderers";
        }

        /// <summary>Bridge: delete the city prefabs (pylon, steel pole) so they are made again, then rebuild the city.</summary>
        public static string RebuildCityAssets()
        {
            AssetDatabase.DeleteAsset(PrefabDir + "/CasinoPylon.prefab");
            AssetDatabase.DeleteAsset(PrefabDir + "/SteelPole25.prefab");
            return AddCity();
        }

        public static GameObject BuildCity(CanalEntrance c)
        {
            var root = new GameObject(CityRootName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, c.gameObject.scene);
            var oo = root.AddComponent<OutdoorObjects>();
            oo.outsideLayer = c.outsideLayer;
            root.transform.SetPositionAndRotation(c.transform.position, c.transform.rotation);
            float G = c.groundHeight, cx = c.MouthsCentre();
            float roadFar = -c.wallThickness - c.roadSetback - c.roadWidth;
            System.Func<Vector2, Vector3> At = v => new Vector3(v.x, G, v.y);
            var view = At(c.PointAt(c.areaDepth, 0f));
            System.Func<Vector3, Quaternion> Face = from => { var d = view - from; d.y = 0f; return Quaternion.LookRotation(d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward); };

            // 1. steel transmission line crossing the channel downstream of the bend
            var line = new GameObject("ЛЭП через русло (опоры двигайте, провода следуют)");
            line.transform.SetParent(root.transform, false);
            var pl = line.AddComponent<PowerLine>();
            pl.wireMaterial = Lit("Canal_Wire", new Color(0.07f, 0.07f, 0.07f), 0.3f, null, 1f);
            pl.maxSpan = 120f; pl.sag = 0.03f; pl.wireRadius = 0.03f;
            var pole = SteelPolePrefab();
            const float sCross = 46f;
            Vector2 p0 = c.PointAt(sCross, 0f), dir = c.DirAt(sCross), across = new Vector2(-dir.y, dir.x);
            float crest = c.CrestAt(sCross);
            float[] ts = { -175f, -95f, -(crest + 9f), crest + 9f, 85f, 160f };
            for (int i = 0; i < ts.Length; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pole, line.transform);
                go.name = "Стальная опора " + (i + 1);
                go.transform.localPosition = At(p0 + across * ts[i]);
                go.transform.localRotation = Quaternion.LookRotation(new Vector3(across.x, 0f, across.y)) * Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 0.3f : -0.25f));
            }

            // 2. casino pylon sign and a lawyer billboard behind the road, seen above the headwall
            var signs = new GameObject("Вывески за дорогой");
            signs.transform.SetParent(root.transform, false);
            var pylon = (GameObject)PrefabUtility.InstantiatePrefab(CasinoPylonPrefab(), signs.transform);
            pylon.name = "Вывеска казино (выдуманное название)";
            pylon.transform.localPosition = new Vector3(cx - 22f, G, roadFar - 14f);
            pylon.transform.localRotation = Face(pylon.transform.localPosition);
            var bb = (GameObject)PrefabUtility.InstantiatePrefab(BillboardPrefab(), signs.transform);
            bb.name = "Билборд адвоката";
            bb.transform.localPosition = new Vector3(cx + 30f, G, roadFar - 24f);
            bb.transform.localRotation = Face(bb.transform.localPosition);
            var bmr = bb.GetComponent<MeshRenderer>();
            var law = Lit("City_LawyerAd", Color.white, 0.25f, CanalTextures.LawyerAd(false), 1f);
            // the billboard face is mapped mirrored for a viewer in front of it: flip the texture
            law.SetTextureScale("_BaseMap", new Vector2(-1f, 1f)); law.SetTextureOffset("_BaseMap", new Vector2(1f, 0f));
            EditorUtility.SetDirty(law);
            bmr.sharedMaterials = new[] { Steel(), law };

            // 3. placeholder Strip towers far behind the tunnel: only their tops show over the headwall from the far end of the walk zone
            var towers = new GameObject("Башни Стрипа (заглушки, замените на свои модели)");
            towers.transform.SetParent(root.transform, false);
            Tower(towers.transform, "Башня Кариока A", new Vector2(cx + 70f, -330f), 25f, "Carnival", true,
                new[] { new Vector4(17f, 10f, 0f, 150f) });
            Tower(towers.transform, "Башня Кариока B", new Vector2(cx + 112f, -362f), 25f, "Carnival", false,
                new[] { new Vector4(15f, 9f, 0f, 132f) });
            Tower(towers.transform, "Золотая башня", new Vector2(cx - 150f, -470f), -15f, "Gold", false,
                new[] { new Vector4(20f, 11f, 0f, 175f), new Vector4(14f, 8f, 175f, 192f) });
            Tower(towers.transform, "Белая башня", new Vector2(cx - 40f, -560f), 8f, "White", false,
                new[] { new Vector4(13f, 13f, 0f, 165f), new Vector4(9f, 9f, 165f, 181f), new Vector4(0.8f, 0.8f, 181f, 214f) });
            Tower(towers.transform, "Тёмная башня", new Vector2(cx + 215f, -440f), -30f, "Dark", false,
                new[] { new Vector4(18f, 12f, 0f, 110f), new Vector4(13f, 9f, 110f, 140f), new Vector4(8f, 6f, 140f, 160f) });
            Needle(towers.transform, new Vector2(cx - 380f, -950f));

            oo.Apply();
            return root;
        }

        // ------------------------------------------------------------------ steel transmission pole

        static GameObject SteelPolePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/SteelPole25.prefab");
            if (existing != null) return existing;
            var kit = new MeshKit(3);
            const float H = 25f, rb = 0.46f, rt = 0.22f;
            System.Func<float, float> R = y => Mathf.Lerp(rb, rt, Mathf.Clamp01((y + 0.5f) / (H + 0.5f)));
            kit.Tube(0, new Vector3(0f, -0.5f, 0f), new Vector3(0f, H, 0f), rb, rt, 16, true, 1f);
            kit.Disc(0, new Vector3(0f, H, 0f), rt, 16, true, 1f);
            kit.Tube(2, new Vector3(0f, -0.3f, 0f), new Vector3(0f, 0.45f, 0f), 0.78f, 0.75f, 16, true, 1f);
            kit.Disc(2, new Vector3(0f, 0.45f, 0f), 0.75f, 16, true, 1f);
            var attach = new List<Vector3>();
            float[] hs = { 17.6f, 20.4f, 23.2f }, reach = { 3.4f, 3.0f, 2.6f };
            for (int i = 0; i < hs.Length; i++)
                foreach (var sx in new[] { -1f, 1f })
                {
                    float h = hs[i];
                    var a = new Vector3(sx * R(h) * 0.7f, h, 0f);
                    var tip = new Vector3(sx * reach[i], h + 0.55f, 0f);
                    kit.Tube(0, a, tip, 0.13f, 0.07f, 8, true, 1f);
                    var bottom = tip + Vector3.down * 1.7f;
                    kit.Tube(1, tip, bottom, 0.035f, 0.035f, 6, true, 1f);
                    for (int k = 0; k < 7; k++)
                    {
                        float y = tip.y - 0.25f - k * 0.2f;
                        kit.Tube(1, new Vector3(tip.x, y, 0f), new Vector3(tip.x, y - 0.05f, 0f), 0.13f, 0.11f, 10, true, 1f);
                    }
                    attach.Add(bottom);
                }
            kit.Tube(0, new Vector3(0f, H, 0f), new Vector3(0f, H + 1.4f, 0f), 0.09f, 0.05f, 8, true, 1f);
            attach.Add(new Vector3(0f, H + 1.4f, 0f));
            int n = 0;
            for (float y = 3f; y < H - 1.5f; y += 0.42f, n++)
            {
                float side = n % 2 == 0 ? 1f : -1f, r = R(y);
                var d = new Vector3(0.25f, 0f, side).normalized;
                kit.Tube(0, new Vector3(0f, y, 0f) + d * (r * 0.9f), new Vector3(0f, y, 0f) + d * (r + 0.22f), 0.018f, 0.018f, 4, true, 1f);
            }
            var mesh = SaveMesh(kit.ToMesh("SteelPole25"), "SteelPole25");
            var steel = Lit("Canal_SteelPole", new Color(0.52f, 0.53f, 0.53f), 0.3f, null, 1f);
            var ins = Lit("Canal_Insulator", new Color(0.72f, 0.72f, 0.68f), 0.6f, null, 1f);
            var foot = Lit("Canal_PoleFoundation", new Color(0.62f, 0.60f, 0.56f), 0.05f, null, 1f);
            return SavePrefab("SteelPole25", mesh, new[] { steel, ins, foot }, go =>
            {
                for (int i = 0; i < attach.Count; i++)
                {
                    var a = new GameObject("Attach" + i);
                    a.transform.SetParent(go.transform, false);
                    a.transform.localPosition = attach[i];
                }
                var col = go.AddComponent<CapsuleCollider>();
                col.radius = rb; col.height = H + 0.5f; col.center = new Vector3(0f, (H - 0.5f) * 0.5f, 0f);
            });
        }

        // ------------------------------------------------------------------ casino pylon sign

        static GameObject CasinoPylonPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/CasinoPylon.prefab");
            if (existing != null) return existing;
            var kit = new MeshKit(4); // 0 paint, 1 payback face, 2 name face, 3 gold trim
            const float colTop = 20f, cabW = 4.2f, cabY0 = 20f, cabY1 = 25.2f, cabD = 0.7f;
            foreach (var x in new[] { -2.6f, 2.6f })
                kit.Box(0, new Vector3(x, (colTop - 0.5f) * 0.5f, 0f), new Vector3(0.38f, 0f, 0f), new Vector3(0f, (colTop + 0.5f) * 0.5f, 0f), new Vector3(0f, 0f, 0.38f), 1f);
            // payback LED panel between the columns
            const float pW = 3.2f, pY0 = 9.2f, pY1 = 12.8f;
            kit.Box(3, new Vector3(0f, (pY0 + pY1) * 0.5f, 0f), new Vector3(pW + 0.12f, 0f, 0f), new Vector3(0f, (pY1 - pY0) * 0.5f + 0.12f, 0f), new Vector3(0f, 0f, 0.3f), 1f);
            Faces(kit, 1, pW, pY0, pY1, 0.31f);
            // name cabinet on top, gold frame
            kit.Box(3, new Vector3(0f, (cabY0 + cabY1) * 0.5f, 0f), new Vector3(cabW + 0.18f, 0f, 0f), new Vector3(0f, (cabY1 - cabY0) * 0.5f + 0.18f, 0f), new Vector3(0f, 0f, cabD), 1f);
            Faces(kit, 2, cabW, cabY0, cabY1, cabD + 0.01f);
            // starburst crown
            for (int k = 0; k < 9; k++)
            {
                float ang = Mathf.Lerp(25f, 155f, k / 8f) * Mathf.Deg2Rad, len = k % 2 == 0 ? 4.2f : 2.8f;
                var b = new Vector3(0f, cabY1 + 0.18f, 0f);
                var tip = b + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * len;
                var side = new Vector3(-Mathf.Sin(ang), Mathf.Cos(ang), 0f) * 0.35f;
                foreach (var f in new[] { Vector3.forward, Vector3.back })
                    kit.Tri(3, b - side + f * 0.05f, b + side + f * 0.05f, tip + f * 0.05f, Vector2.zero, Vector2.right, Vector2.up, f);
            }
            var mesh = SaveMesh(kit.ToMesh("CasinoPylon"), "CasinoPylon");
            var paint = Lit("City_PylonPaint", new Color(0.55f, 0.07f, 0.12f), 0.45f, null, 1f);
            var payback = Lit("City_PaybackSign", Color.white, 0.3f, CanalTextures.PaybackSign(false), 1f);
            var name = Lit("City_CasinoSign", Color.white, 0.3f, CanalTextures.CasinoSign(false), 1f);
            var gold = Lit("City_PylonGold", new Color(0.86f, 0.66f, 0.26f), 0.55f, null, 1f);
            return SavePrefab("CasinoPylon", mesh, new[] { paint, payback, name, gold }, go =>
            {
                foreach (var x in new[] { -2.6f, 2.6f })
                {
                    var b = go.AddComponent<BoxCollider>();
                    b.center = new Vector3(x, colTop * 0.5f, 0f); b.size = new Vector3(0.76f, colTop, 0.76f);
                }
            });
        }

        /// <summary>Front (+z) and back (-z) textured faces, both read left to right.</summary>
        static void Faces(MeshKit kit, int sub, float hw, float y0, float y1, float z)
        {
            // seen from +z the viewer's right is -x, so the front face runs +x -> -x
            kit.Quad(sub, new Vector3(hw, y0, z), new Vector3(-hw, y0, z), new Vector3(-hw, y1, z), new Vector3(hw, y1, z),
                     new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), Vector3.forward);
            kit.Quad(sub, new Vector3(-hw, y0, -z), new Vector3(hw, y0, -z), new Vector3(hw, y1, -z), new Vector3(-hw, y1, -z),
                     new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), Vector3.back);
        }

        // ------------------------------------------------------------------ placeholder towers

        /// <summary>Tower of stacked blocks: each Vector4 = (half width, half depth, bottom, top), metres.</summary>
        static void Tower(Transform parent, string name, Vector2 at, float rot, string style, bool nameSign, Vector4[] blocks)
        {
            var kit = new MeshKit(3); // 0 facade, 1 roof, 2 sign
            const float tileW = 6.4f, tileH = 12.8f;
            foreach (var b in blocks)
            {
                Vector3[] k = { new Vector3(-b.x, 0f, -b.y), new Vector3(b.x, 0f, -b.y), new Vector3(b.x, 0f, b.y), new Vector3(-b.x, 0f, b.y) };
                float u = 0f;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 a = k[i], c = k[(i + 1) % 4];
                    float len = Vector3.Distance(a, c);
                    var outward = ((a + c) * 0.5f).normalized;
                    kit.Quad(b.x < 2f ? 1 : 0, a + Vector3.up * b.z, c + Vector3.up * b.z, c + Vector3.up * b.w, a + Vector3.up * b.w,
                             new Vector2(u / tileW, b.z / tileH), new Vector2((u + len) / tileW, b.z / tileH),
                             new Vector2((u + len) / tileW, b.w / tileH), new Vector2(u / tileW, b.w / tileH), outward);
                    u += len;
                }
                kit.Quad(1, k[0] + Vector3.up * b.w, k[1] + Vector3.up * b.w, k[2] + Vector3.up * b.w, k[3] + Vector3.up * b.w,
                         Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
            }
            if (nameSign)
            {
                var top = blocks[blocks.Length - 1];
                float hw = top.x * 0.8f, y0 = top.w + 1f, y1 = y0 + hw * 2f * 640f / 1024f;
                Faces(kit, 2, hw, y0, y1, 0f);
                kit.Box(1, new Vector3(0f, (top.w + y0) * 0.5f, 0f), new Vector3(hw * 0.8f, 0f, 0f), new Vector3(0f, (y0 - top.w) * 0.5f, 0f), new Vector3(0f, 0f, 0.4f), 1f);
            }
            var mesh = SaveMesh(kit.ToMesh("City_" + style + "_" + Mathf.Abs(at.x + at.y * 3f).ToString("0")), "City_" + style + "_" + Mathf.Abs(at.x + at.y * 3f).ToString("0"));
            var facade = Lit("City_Facade_" + style, Color.white, 0.45f, CanalTextures.Facade(style, false), 1f);
            var roof = Lit("City_Roof", new Color(0.70f, 0.71f, 0.72f), 0.1f, null, 1f);
            var sign = Lit("City_CasinoSign", Color.white, 0.3f, CanalTextures.CasinoSign(false), 1f);
            Place(parent, name, mesh, new[] { facade, roof, sign }, new Vector3(at.x, 0f, at.y), rot);
        }

        /// <summary>Observation needle far away: concrete shaft, glass pod, antenna.</summary>
        static void Needle(Transform parent, Vector2 at)
        {
            var kit = new MeshKit(3); // 0 concrete, 1 glass, 2 roof
            kit.Tube(0, new Vector3(0f, 0f, 0f), new Vector3(0f, 262f, 0f), 10f, 6f, 24, true, 0.05f);
            foreach (var leg in new[] { 0f, 120f, 240f })
            {
                var d = Quaternion.Euler(0f, leg, 0f) * Vector3.forward;
                kit.Tube(0, d * 22f, d * 7f + Vector3.up * 70f, 3f, 2f, 10, true, 0.05f);
            }
            kit.Tube(0, new Vector3(0f, 255f, 0f), new Vector3(0f, 264f, 0f), 12f, 21f, 24, true, 0.05f);
            kit.Tube(1, new Vector3(0f, 264f, 0f), new Vector3(0f, 276f, 0f), 23f, 23f, 24, true, 0.05f);
            kit.Tube(0, new Vector3(0f, 276f, 0f), new Vector3(0f, 282f, 0f), 22f, 18f, 24, true, 0.05f);
            kit.Disc(2, new Vector3(0f, 282f, 0f), 18f, 24, true, 1f);
            kit.Disc(0, new Vector3(0f, 255f, 0f), 12f, 24, false, 1f);
            kit.Tube(0, new Vector3(0f, 282f, 0f), new Vector3(0f, 340f, 0f), 1.4f, 0.35f, 8, true, 0.05f);
            var mesh = SaveMesh(kit.ToMesh("City_Needle"), "City_Needle");
            var concrete = Lit("City_NeedleConcrete", Color.Lerp(new Color(0.86f, 0.84f, 0.80f), new Color(0.80f, 0.85f, 0.92f), 0.4f), 0.1f, null, 1f);
            var glass = Lit("City_NeedleGlass", Color.Lerp(new Color(0.12f, 0.16f, 0.22f), new Color(0.80f, 0.85f, 0.92f), 0.4f), 0.6f, null, 1f);
            var roof = Lit("City_Roof", new Color(0.70f, 0.71f, 0.72f), 0.1f, null, 1f);
            Place(parent, "Смотровая башня", mesh, new[] { concrete, glass, roof }, new Vector3(at.x, 0f, at.y), 0f);
        }

        static void Place(Transform parent, string name, Mesh mesh, Material[] mats, Vector3 pos, float rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, rot, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
