using System.IO;
using CarX.Level;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarX.EditorTools
{
    /// <summary>
    /// «Culvert» look from the two reference photos (Las Vegas storm drains): clean board-formed concrete,
    /// slab joints that read as rings, a clean floor, daylight at the tunnel exits, cool grey grading.
    /// Everything is stored in assets (library, types, material set, volume profiles), so every graph
    /// in every scene picks it up when it rebuilds: nothing has to be redone by hand.
    /// </summary>
    public static partial class TunnelAssets
    {
        public const string CulvertSetPath = "Assets/_Project/Settings/TunnelMaterials_Culvert.asset";
        public const string ExitFxPath = "Assets/_Project/Settings/TunnelExitFX.asset";

        [MenuItem("CarX/Tunnels/Apply Culvert Look (reference photos)", priority = 41)]
        static void MenuCulvert()
        {
            Debug.Log(ApplyCulvertLook(true));
        }

        /// <summary>Idempotent: run again after changing the numbers here.</summary>
        public static string ApplyCulvertLook(bool regenerate)
        {
            var old = Get(false);   // the old set keeps metal, water, light materials we reuse
            if (regenerate || !File.Exists(TexDir + "/T_Culvert_A.png") || !File.Exists(TexDir + "/T_CulvertFloor_A.png"))
            {
                try
                {
                    EditorUtility.DisplayProgressBar("CarX", "Генерирую бетон тоннелей…", 0.3f);
                    Build("T_Culvert", 1024, CulvertWall, 5f, true);
                    EditorUtility.DisplayProgressBar("CarX", "Генерирую бетон тоннелей…", 0.7f);
                    Build("T_CulvertFloor", 1024, CulvertFloor, 2f, true);
                }
                finally { EditorUtility.ClearProgressBar(); }
            }

            var set = AssetDatabase.LoadAssetAtPath<TunnelMaterials>(CulvertSetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<TunnelMaterials>();
                AssetDatabase.CreateAsset(set, CulvertSetPath);
            }
            // matte: smoothness ~0.12 (texture 0.21 × 0.55); a glossier wall glitters near the camera.
            // Weak relief: under grazing light a strong normal map lights up only the near walls (far away its mips go flat)
            const float WallBump = 0.35f, WallGloss = 0.55f;
            set.concrete = Lit("M_CulvertWall", "T_Culvert", WallBump, WallGloss, 0f, new Color(0.92f, 0.925f, 0.94f));
            set.concreteVariants = new[]
            {
                Lit("M_CulvertWall_Dark", "T_Culvert", WallBump, WallGloss, 0f, new Color(0.80f, 0.805f, 0.82f)),
                Lit("M_CulvertWall_Light", "T_Culvert", WallBump, WallGloss, 0f, new Color(1f, 1f, 1f)),
            };
            set.concreteDirty = set.concrete;   // no mud band on the walls
            set.floor = Lit("M_CulvertFloor", "T_CulvertFloor", 0.4f, 0.8f, 0f, Color.white);
            set.soil = old.soil;
            set.sand = old.sand;
            set.water = old.water;
            set.iron = old.iron;
            set.lid = old.lid;
            set.shaft = old.shaft;
            set.ray = old.ray;
            set.dust = old.dust;
            set.sky = old.sky;
            EditorUtility.SetDirty(set);

            var lib = TunnelGraphAssets.GetLibrary();
            lib.materials = set;
            if (lib.exitExposure == null) lib.exitExposure = ExitProfile();
            // light, atmosphere and post FX numbers are NOT touched here any more: they are tuned by hand in
            // TunnelTypes.asset / TunnelPostFX.asset (see the instruction), this menu must not reset them
            int types = UpdateTypes(lib);
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            TunnelNetwork.MarkAllDirty();
            return "CarX culvert look applied: material set " + CulvertSetPath + ", types updated " + types;
        }

        /// <summary>Only the two concrete textures (walls, floor): materials, light and post FX stay as tuned.</summary>
        public static string RegenerateCulvertTextures()
        {
            try
            {
                EditorUtility.DisplayProgressBar("CarX", "Генерирую бетон тоннелей…", 0.3f);
                Build("T_Culvert", 1024, CulvertWall, 5f, true);
                EditorUtility.DisplayProgressBar("CarX", "Генерирую бетон тоннелей…", 0.7f);
                Build("T_CulvertFloor", 1024, CulvertFloor, 2f, true);
            }
            finally { EditorUtility.ClearProgressBar(); }
            AssetDatabase.SaveAssets();
            return "CarX culvert textures regenerated";
        }

        [MenuItem("CarX/Tunnels/Regenerate Culvert Textures (keeps settings)", priority = 42)]
        static void MenuCulvertTextures() { Debug.Log(RegenerateCulvertTextures()); }

        /// <summary>Exit to daylight: the eye adapts, so the picture gets darker near the light.</summary>
        static VolumeProfile ExitProfile()
        {
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ExitFxPath);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(p, ExitFxPath);
            }
            var ca = Add<ColorAdjustments>(p);
            ca.postExposure.Override(-3.0f);
            EditorUtility.SetDirty(p);
            return p;
        }

        /// <summary>Octagon becomes the 12×8 ft Vegas box, a 10×6 ft one is added; no mud band on any type.</summary>
        static int UpdateTypes(TunnelTypeLibrary lib)
        {
            int n = 0;
            var big = AssetDatabase.LoadAssetAtPath<TunnelType>(TunnelGraphAssets.Dir + "/Types/Octagon.asset");
            if (big != null) { Culvert(big, "Вегас 12×8 фт (восьмиугольник)", 3.66f, 2.44f, 0.35f, 0.5f, 0.45f); n++; }

            string smallPath = TunnelGraphAssets.Dir + "/Types/Vegas10x6.asset";
            var small = AssetDatabase.LoadAssetAtPath<TunnelType>(smallPath);
            if (small == null)
            {
                small = ScriptableObject.CreateInstance<TunnelType>();
                small.color = new Color(1f, 0.82f, 0.25f);
                small.maxJointAngle = 2f;
                AssetDatabase.CreateAsset(small, smallPath);
            }
            Culvert(small, "Вегас 10×6 фт (восьмиугольник)", 3.05f, 1.83f, 0.25f, 0.4f, 0.35f);
            n++;
            if (!lib.types.Contains(small)) lib.types.Add(small);   // at the end: indexes of the old types stay

            foreach (var t in lib.types)
                if (t != null && t.dirtLine > 0f) { t.dirtLine = 0f; EditorUtility.SetDirty(t); }
            return n;
        }

        static void Culvert(TunnelType t, string label, float w, float h, float top, float bw, float bh)
        {
            t.displayName = label;
            var s = t.section;
            s.profile = TunnelProfile.Octagon;
            s.width = w;
            s.height = h;
            s.chamfer = top;
            s.chamferTop = top;
            s.bevelWidth = bw;
            s.bevelHeight = bh;
            t.segmentLength = 1.83f;   // 6 ft precast sections
            t.slabThickness = 0.25f;
            t.minJointGap = 0.012f;    // joints visible as rings (the photos), floor still one plane
            t.jointSink = 0.001f;
            t.edgeBevel = 0.02f;
            t.dirtLine = 0f;
            t.seamless = false;
            t.channelWater = false;
            EditorUtility.SetDirty(t);
        }

        // ---------- textures ----------

        /// <summary>Wall: u = along the tunnel (one tile = one 1.83 m slab), v = around the profile (one tile = 2 m).
        /// Board-formed concrete: ~15 cm boards across the slab, wood grain running around the profile (vertical on the walls),
        /// a few bug holes, faint water streaks, a slightly darker band at the slab ends.</summary>
        static S CulvertWall(float u, float v)
        {
            var s = new S { a = 1f };
            float big = Fbm(u, v, 3, 4, 301);
            float mid = Fbm(u, v, 10, 4, 302);
            float fine = Fbm(u, v, 64, 3, 303);

            const int Boards = 12;
            float bu = u * Boards;
            int bi = Mathf.FloorToInt(bu);
            float bf = bu - bi;
            int bh = Hash(Mod(bi, Boards), 7, 304);
            float tone = R01(bh, 0) - 0.5f;
            float lift = R01(bh, 8) - 0.5f;
            float dEdge = Mathf.Min(bf, 1f - bf) / Boards * 1.83f;
            float seam = Sat(1f - dEdge / 0.005f) * Sat(0.55f + Fbm(u, v, 6, 3, 2, 305) * 1.5f);

            float grain = Fbm(u, v, 220, 5, 2, 306);
            float grainFine = Fbm(u, v, 380, 11, 1, 307);

            float L = 0.50f + big * 0.07f + mid * 0.045f + fine * 0.01f + tone * 0.012f + grain * 0.012f + grainFine * 0.004f;
            // formwork relief kept only as a hint (the real board marks are 10-20x fainter than before)
            float h = 0.5f + (grain * 0.14f + grainFine * 0.05f + lift * 0.10f + seam * 0.35f) * 0.08f + mid * 0.10f + big * 0.10f + fine * 0.02f;
            float sm = 0.21f + mid * 0.05f - grain * 0.03f;

            float f1, f2; int id;
            Worley(u, v, 80, 308, out f1, out f2, out id);
            float pore = R01(id, 8) > 0.985f ? Sat((0.10f + R01(id, 16) * 0.05f - f1) / 0.05f) : 0f;
            L *= 1f - 0.12f * pore;
            h -= pore * 0.08f;

            float streak = Sat((Fbm(u, v, 26, 2, 3, 309) - 0.10f) * 3f) * Sat(0.25f + Fbm(u, v, 3, 2, 3, 310) * 1.4f);
            L *= 1f - streak * 0.22f;
            sm = Mathf.Lerp(sm, 0.32f, streak * 0.5f);


            s.c = new Color(L * 0.975f, L * 0.985f, L);
            s.h = h;
            s.sm = Sat(sm);
            return s;
        }

        /// <summary>Floor: world-planar UV, one tile = 2 m. Clean, slightly worn concrete: smoother lanes, hairline cracks, no mud.</summary>
        static S CulvertFloor(float u, float v)
        {
            var s = new S { a = 1f };
            float big = Fbm(u, v, 2, 4, 401);
            float mid = Fbm(u, v, 8, 4, 402);
            float fine = Fbm(u, v, 48, 3, 403);
            float L = 0.47f + big * 0.06f + mid * 0.04f + fine * 0.025f;
            float wear = Sat(0.5f + Fbm(u, v, 4, 3, 404) * 1.6f);
            float sm = 0.22f + wear * 0.12f + mid * 0.04f;
            float h = 0.5f + mid * 0.12f + fine * 0.10f + big * 0.05f;

            float mark = Sat((Fbm(u, v, 5, 4, 408) - 0.28f) * 5f);
            L = Mathf.Lerp(L, L * 0.9f, mark * 0.5f);

            s.c = new Color(L * 0.98f, L * 0.985f, L);
            s.h = h;
            s.sm = Sat(sm);
            return s;
        }
    }
}
