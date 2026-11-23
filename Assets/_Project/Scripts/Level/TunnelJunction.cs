using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>
    /// Concrete junction chamber. Walls get openings shaped exactly like the tunnels connected to them.
    /// Sides: north = +Z, east = +X, south = -Z, west = -X (local space).
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, SelectionBase]
    [AddComponentMenu("CarX/Tunnel Junction")]
    public class TunnelJunction : MonoBehaviour
    {
        [Header("Размер камеры, м")]
        [Min(2f)] public float sizeX = 4.8f;
        [Min(2f)] public float sizeZ = 4.8f;
        [Min(2f)] public float height = 3.3f;

        [Header("Тоннели по сторонам (север = +Z, восток = +X)")]
        public TunnelPath north;
        public TunnelPath east;
        public TunnelPath south;
        public TunnelPath west;

        [Header("Подъём наверх в потолке")]
        public bool hasShaft;
        public ShaftSpec shaft = ShaftSpec.Preset(ShaftType.GrateSquare, 0f);

        [Header("Прочее")]
        [Range(0f, 1.5f)] public float dirtLine = 0.9f;
        public int seed = 1;
        [HideInInspector] public int drivenSignature;
        public TunnelMaterials materials;

        [SerializeField, HideInInspector] GameObject shaftObject;
        [SerializeField, HideInInspector] int shaftHash;

        public static readonly Vector3[] SideDirs = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
        public static readonly string[] SideNames = { "Север (+Z)", "Восток (+X)", "Юг (-Z)", "Запад (-X)" };

        [NonSerialized] bool dirty = true;
        [NonSerialized] Mesh mesh, skinMesh;
        [NonSerialized] int signature;
        [NonSerialized] public string warning;

        public TunnelPath GetSide(int i)
        {
            switch (i) { case 0: return north; case 1: return east; case 2: return south; default: return west; }
        }

        public void SetSide(int i, TunnelPath p)
        {
            switch (i) { case 0: north = p; break; case 1: east = p; break; case 2: south = p; break; default: west = p; break; }
            dirty = true;
        }

        public static int SideOf(Vector3 localDir)
        {
            if (Mathf.Abs(localDir.x) > Mathf.Abs(localDir.z)) return localDir.x > 0 ? 1 : 3;
            return localDir.z > 0 ? 0 : 2;
        }

        public float HalfDepth(int side) { return side % 2 == 0 ? sizeZ * 0.5f : sizeX * 0.5f; }
        public float HalfLength(int side) { return side % 2 == 0 ? sizeX * 0.5f : sizeZ * 0.5f; }

        void OnEnable() { Rebuild(true); }

        void OnValidate()
        {
            dirty = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void OnDestroy()
        {
            if (mesh != null) DestroyImmediate(mesh);
            if (skinMesh != null) DestroyImmediate(skinMesh);
            TunnelSkin.Remove(skinPart);
            if (skinPart != null && skinPart.mesh != null) DestroyImmediate(skinPart.mesh);
        }

        void OnDisable() { TunnelSkin.Remove(skinPart); }
        [NonSerialized] TunnelSkin.Part skinPart;

        int Signature()
        {
            unchecked
            {
                int h = transform.position.GetHashCode() * 31 + transform.rotation.GetHashCode();
                for (int i = 0; i < 4; i++)
                {
                    var p = GetSide(i);
                    h = h * 31 + (p != null ? p.GetHashCode() * 7 + p.buildVersion : 0);
                }
                return h;
            }
        }

        void Update()
        {
            if (!dirty && Signature() != signature) dirty = true;
            if (dirty) Rebuild(false);
        }

        public void MarkDirty() { dirty = true; }

        Vector2 FloorUV(Vector3 local)
        {
            var w = transform.TransformPoint(local);
            return new Vector2(w.x * 0.5f, w.z * 0.5f);
        }

        public void Rebuild(bool forceShaft)
        {
            dirty = false;
            warning = null;
            signature = Signature();
            var mats = TunnelMaterials.Resolve(materials);
            float hx = sizeX * 0.5f, hz = sizeZ * 0.5f, H = height;
            var kit = new MeshKit(5);
            var rnd = new System.Random(seed * 31 + 7);
            float line = dirtLine > 0.01f ? dirtLine : -1f;
            var skinHoles = new List<Vector2>[4];
            var skinCentres = new Vector2[4];

            for (int side = 0; side < 4; side++)
            {
                Vector3 d = SideDirs[side];
                Vector3 r = Vector3.Cross(Vector3.up, d);
                float halfDepth = HalfDepth(side), halfLen = HalfLength(side);
                var outer = new List<Vector2> { new Vector2(-halfLen, 0), new Vector2(halfLen, 0), new Vector2(halfLen, H), new Vector2(-halfLen, H) };
                List<Vector2> inner;
                Vector2 centre;
                List<Vector2> tris;
                var path = GetSide(side);
                if (path != null && Opening(path, d, r, halfDepth, halfLen, out inner, out centre))
                {
                    tris = MeshKit.RingFill(centre, inner, outer);
                    skinHoles[side] = inner;
                    skinCentres[side] = centre;
                }
                else tris = new List<Vector2> { outer[0], outer[1], outer[2], outer[0], outer[2], outer[3] };
                var below = new List<Vector2>();
                var above = new List<Vector2>();
                for (int t = 0; t + 2 < tris.Count; t += 3) MeshKit.SplitTri(tris[t], tris[t + 1], tris[t + 2], line, below, above);
                float uOff = (float)rnd.NextDouble();
                EmitWall(kit, 4, below, d, r, halfDepth, uOff, 0f);
                EmitWall(kit, 0, above, d, r, halfDepth, uOff, (float)rnd.NextDouble());
            }

            // floor
            // world-planar UV, the same as tunnels use, so the floor texture runs on through the doorways
            Vector3 f0 = new Vector3(-hx, 0, -hz), f1 = new Vector3(hx, 0, -hz), f2 = new Vector3(hx, 0, hz), f3 = new Vector3(-hx, 0, hz);
            kit.Quad(1, f0, f1, f2, f3, FloorUV(f0), FloorUV(f1), FloorUV(f2), FloorUV(f3), Vector3.up);

            // ceiling, optionally with a shaft opening
            var ceilOuter = new List<Vector2> { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(hx, hz), new Vector2(-hx, hz) };
            List<Vector2> ceilTris;
            ShaftOpening op = default(ShaftOpening);
            float cx = 0f;
            if (hasShaft && shaft != null)
            {
                op = TunnelShaftBuilder.MakeOpening(shaft, -hx, hx, hz - 0.25f, out cx);
                var c = new Vector2(cx, 0f);
                ceilTris = MeshKit.RingFill(c, op.Polygon(c), ceilOuter);
            }
            else ceilTris = new List<Vector2> { ceilOuter[0], ceilOuter[1], ceilOuter[2], ceilOuter[0], ceilOuter[2], ceilOuter[3] };
            for (int t = 0; t + 2 < ceilTris.Count; t += 3)
            {
                Vector2 a = ceilTris[t], b = ceilTris[t + 1], c = ceilTris[t + 2];
                kit.Tri(0, new Vector3(a.x, H, a.y), new Vector3(b.x, H, b.y), new Vector3(c.x, H, c.y), a * 0.5f, b * 0.5f, c * 0.5f, Vector3.down);
            }

            var gen = transform.Find("_Generated");
            if (gen == null)
            {
                gen = new GameObject("_Generated").transform;
                gen.SetParent(transform, false);
            }
            if (mesh == null) mesh = new Mesh { name = name + "_Chamber", hideFlags = HideFlags.DontSave };
            kit.Fill(mesh);
            var mf = gen.GetComponent<MeshFilter>();
            if (mf == null) mf = gen.gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = gen.GetComponent<MeshRenderer>();
            if (mr == null) mr = gen.gameObject.AddComponent<MeshRenderer>();
            if (mats != null) mr.sharedMaterials = mats.TunnelSet();
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            TunnelGI.Setup(mr);   // baked bounce light from probe volumes (lighting thread)
            var mc = gen.GetComponent<MeshCollider>();
            if (mc == null) mc = gen.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = null;
            mc.sharedMesh = mesh;

            BuildSkin(skinHoles, skinCentres, hasShaft && shaft != null ? op : (ShaftOpening?)null, cx, mats);

            // shaft
            if (hasShaft && shaft != null)
            {
                int hash;
                unchecked
                {
                    hash = shaft.Hash() * 31 + op.Hash();
                    hash = hash * 31 + cx.GetHashCode();
                    hash = hash * 31 + H.GetHashCode() + sizeX.GetHashCode() * 7;
                    hash = hash * 31 + (mats != null ? mats.GetHashCode() : 0);
                }
                if (shaftObject == null || forceShaft || hash != shaftHash)
                {
                    Kill(shaftObject);
                    float wall = hx;
                    shaftObject = TunnelShaftBuilder.Build(transform, "_Shaft", new Vector3(cx, 0f, 0f), Quaternion.identity, H, cx, op, shaft,
                                                           y => wall, x => 0f, mats);
                    shaftHash = hash;
                }
            }
            else if (shaftObject != null)
            {
                Kill(shaftObject);
                shaftObject = null;
            }
            var orphans = new List<GameObject>();
            foreach (Transform c in transform)
                if (c.name.StartsWith("_Shaft") && c.gameObject != shaftObject) orphans.Add(c.gameObject);
            foreach (var o in orphans) Kill(o);
        }

        bool Opening(TunnelPath path, Vector3 d, Vector3 r, float halfDepth, float halfLen, out List<Vector2> inner, out Vector2 centre)
        {
            inner = null;
            centre = Vector2.zero;
            Vector3 wa, wb;
            if (!path.EndsWorld(out wa, out wb)) return false;
            Vector3 wallCentre = d * halfDepth;
            Vector3 pa = transform.InverseTransformPoint(wa);
            Vector3 pb = transform.InverseTransformPoint(wb);
            Vector3 e = (pa - wallCentre).sqrMagnitude <= (pb - wallCentre).sqrMagnitude ? pa : pb;
            if (Mathf.Abs(Vector3.Dot(e - wallCentre, d)) > 0.6f)
            {
                warning = "Тоннель " + path.name + " не упирается в свою стену (кнопка «Пристыковать тоннели»).";
                return false;
            }
            float lateral = Vector3.Dot(e, r), level = e.y;
            List<int> kind; int ceil;
            var outline = path.section.BaseOutline(out kind, out ceil);
            inner = new List<Vector2>(outline.Count);
            foreach (var v in outline)
            {
                var p = new Vector2(lateral + v.x, level + v.y);
                if (p.x < -halfLen + 0.02f || p.x > halfLen - 0.02f || p.y < -0.001f || p.y > height - 0.02f)
                {
                    warning = "Тоннель " + path.name + " шире или выше стены камеры.";
                    inner = null;
                    return false;
                }
                inner.Add(p);
            }
            centre = new Vector2(lateral, level + path.section.Height * 0.5f);
            return true;
        }

        static void EmitWall(MeshKit kit, int sub, List<Vector2> tris, Vector3 d, Vector3 r, float halfDepth, float uOff, float vOff)
        {
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                Vector2 a = tris[t], b = tris[t + 1], c = tris[t + 2];
                kit.Tri(sub, W(a, d, r, halfDepth), W(b, d, r, halfDepth), W(c, d, r, halfDepth),
                        new Vector2(a.x * 0.5f + uOff, a.y * 0.5f + vOff), new Vector2(b.x * 0.5f + uOff, b.y * 0.5f + vOff),
                        new Vector2(c.x * 0.5f + uOff, c.y * 0.5f + vOff), -d);
            }
        }

        /// <summary>Light-tight black skin round the chamber, a little outside its walls: through a slit at a seam the eye
        /// meets black, and outside light is shadowed. Open only exactly at the tunnel openings and the shaft.</summary>
        void BuildSkin(List<Vector2>[] holes, Vector2[] centres, ShaftOpening? op, float cx, TunnelMaterials mats)
        {
            const float off = 0.15f;
            float hx = sizeX * 0.5f + off, hz = sizeZ * 0.5f + off, y0 = -off, y1 = height + off;
            var kit = new MeshKit(1);
            for (int side = 0; side < 4; side++)
            {
                Vector3 d = SideDirs[side];
                Vector3 r = Vector3.Cross(Vector3.up, d);
                float depth = HalfDepth(side) + off, len = HalfLength(side) + off;
                var outer = new List<Vector2> { new Vector2(-len, y0), new Vector2(len, y0), new Vector2(len, y1), new Vector2(-len, y1) };
                var tris = holes[side] != null ? MeshKit.RingFill(centres[side], holes[side], outer)
                                               : new List<Vector2> { outer[0], outer[1], outer[2], outer[0], outer[2], outer[3] };
                for (int t = 0; t + 2 < tris.Count; t += 3)
                    kit.Tri(0, W(tris[t], d, r, depth), W(tris[t + 1], d, r, depth), W(tris[t + 2], d, r, depth), Vector2.zero, Vector2.zero, Vector2.zero, -d);
            }
            kit.Quad(0, new Vector3(-hx, y0, -hz), new Vector3(hx, y0, -hz), new Vector3(hx, y0, hz), new Vector3(-hx, y0, hz),
                     Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
            var ceilOuter = new List<Vector2> { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(hx, hz), new Vector2(-hx, hz) };
            List<Vector2> ct;
            if (op.HasValue)
            {
                var c = new Vector2(cx, 0f);
                ct = MeshKit.RingFill(c, op.Value.Polygon(c), ceilOuter);
            }
            else ct = new List<Vector2> { ceilOuter[0], ceilOuter[1], ceilOuter[2], ceilOuter[0], ceilOuter[2], ceilOuter[3] };
            for (int t = 0; t + 2 < ct.Count; t += 3)
                kit.Tri(0, new Vector3(ct[t].x, y1, ct[t].y), new Vector3(ct[t + 1].x, y1, ct[t + 1].y), new Vector3(ct[t + 2].x, y1, ct[t + 2].y),
                        Vector2.zero, Vector2.zero, Vector2.zero, Vector3.down);

            var go = transform.Find("_Skin");
            if (go == null)
            {
                go = new GameObject("_Skin").transform;
                go.SetParent(transform, false);
            }
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.gameObject.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.gameObject.AddComponent<MeshRenderer>();
            TunnelMaterials.ApplySkin(mr, mats);
            if (skinPart == null) skinPart = new TunnelSkin.Part();
            skinPart.owner = transform;
            skinPart.filter = mf;
            TunnelSkin.TakeRaw(skinPart, kit);
            skinPart.volumes.Clear();
            float ihx = sizeX * 0.5f, ihz = sizeZ * 0.5f;
            skinPart.volumes.Add(new TunnelSkin.Volume
            {
                o = Vector3.zero, u = Vector3.right, v = Vector3.up, w = Vector3.forward,
                poly = new[] { new Vector2(-ihx, 0f), new Vector2(ihx, 0f), new Vector2(ihx, height), new Vector2(-ihx, height) }, w0 = -ihz, w1 = ihz
            });
            if (op.HasValue)
                skinPart.volumes.Add(new TunnelSkin.Volume
                {
                    o = Vector3.zero, u = Vector3.right, v = Vector3.forward, w = Vector3.up,
                    poly = op.Value.Polygon(new Vector2(cx, 0f)).ToArray(), w0 = height - 0.3f, w1 = height + 8f
                });
            TunnelSkin.Submit(skinPart);
        }

        static Vector3 W(Vector2 v, Vector3 d, Vector3 r, float halfDepth) { return d * halfDepth + r * v.x + Vector3.up * v.y; }

        static void Kill(GameObject g)
        {
            if (g == null) return;
            if (Application.isPlaying) Destroy(g); else DestroyImmediate(g);
        }
    }
}
