using System.Collections.Generic;
using UnityEngine;

namespace CarX.DarkOrientation
{
    /// <summary>Adult arm proportions in metres for a 19 cm hand. Scaled as a whole by <see cref="DarkOrientationSettings.handScale"/>.</summary>
    public static class HandProportions
    {
        public const float UpperArm = 0.30f;
        public const float Forearm = 0.26f;
        public const float PalmLength = 0.095f;
        public const float PalmWidth = 0.085f;
        public const float PalmThickness = 0.027f;

        // Proximal, middle, distal phalanx lengths; for the thumb: metacarpal, proximal, distal.
        public static readonly float[] Thumb = { 0.045f, 0.033f, 0.028f };
        public static readonly float[] Index = { 0.043f, 0.025f, 0.022f };
        public static readonly float[] Middle = { 0.047f, 0.029f, 0.024f };
        public static readonly float[] Ring = { 0.044f, 0.027f, 0.023f };
        public static readonly float[] Little = { 0.035f, 0.019f, 0.020f };

        // Knuckle positions on the palm for a right hand (thumb side is -X), hand-local, palm facing -Y.
        public static readonly Vector3 IndexBase = new(-0.027f, 0f, 0.093f);
        public static readonly Vector3 MiddleBase = new(-0.009f, 0f, 0.097f);
        public static readonly Vector3 RingBase = new(0.009f, 0f, 0.094f);
        public static readonly Vector3 LittleBase = new(0.026f, 0f, 0.086f);
        public static readonly Vector3 ThumbBase = new(-0.024f, -0.008f, 0.022f);
        public static readonly Vector3 ThumbDirection = new Vector3(-0.50f, -0.32f, 0.80f).normalized;
    }

    /// <summary>
    /// Builds a cheap stand-in arm at runtime: rigid tapered prisms per bone (about 800 triangles),
    /// bones named like a Humanoid skeleton, all rest rotations identity with bones laid along +Z.
    /// </summary>
    public static class ProceduralHandBuilder
    {
        static readonly float[] RingT = { 0f, 0.14f, 0.86f, 1f };
        static readonly float[] RingScale = { 0.78f, 1f, 1f, 0.8f };

        public static HandRig Build(HandSide side, float scale, Material material, Transform parent = null)
        {
            string suffix = side == HandSide.Left ? "L" : "R";
            float sx = side == HandSide.Right ? 1f : -1f;   // mirrors the right-hand layout for the left hand
            var root = new GameObject("DarkOrientation Arm " + suffix);
            if (parent != null) root.transform.SetParent(parent, false);
            var rig = root.AddComponent<HandRig>();
            rig.side = side;
            var renderers = new List<Renderer>();
            float s = scale;

            var upper = Bone("UpperArm", root.transform, Vector3.zero);
            var fore = Bone("Forearm", upper, new Vector3(0f, 0f, HandProportions.UpperArm * s));
            var hand = Bone("Hand", fore, new Vector3(0f, 0f, HandProportions.Forearm * s));
            Segment(upper, fore.localPosition, 0.095f * s, 0.090f * s, 0.080f * s, 0.075f * s, material, renderers);
            Segment(fore, hand.localPosition, 0.075f * s, 0.065f * s, 0.058f * s, 0.042f * s, material, renderers);
            Segment(hand, new Vector3(0f, 0f, HandProportions.PalmLength * s), 0.062f * s, HandProportions.PalmThickness * s,
                HandProportions.PalmWidth * s, HandProportions.PalmThickness * 0.95f * s, material, renderers);

            rig.upperArm = upper;
            rig.forearm = fore;
            rig.hand = hand;
            rig.index = MakeFinger(hand, "Index", Mirror(HandProportions.IndexBase, sx) * s, Vector3.forward, HandProportions.Index, 1f, s, material, renderers);
            rig.middle = MakeFinger(hand, "Middle", Mirror(HandProportions.MiddleBase, sx) * s, Vector3.forward, HandProportions.Middle, 1.04f, s, material, renderers);
            rig.ring = MakeFinger(hand, "Ring", Mirror(HandProportions.RingBase, sx) * s, Vector3.forward, HandProportions.Ring, 0.97f, s, material, renderers);
            rig.little = MakeFinger(hand, "Little", Mirror(HandProportions.LittleBase, sx) * s, Vector3.forward, HandProportions.Little, 0.85f, s, material, renderers);
            rig.thumb = MakeFinger(hand, "Thumb", Mirror(HandProportions.ThumbBase, sx) * s, Mirror(HandProportions.ThumbDirection, sx), HandProportions.Thumb, 1.12f, s, material, renderers);
            rig.renderers = renderers.ToArray();
            rig.Bind();
            return rig;
        }

        static Vector3 Mirror(Vector3 v, float sx) => new(v.x * sx, v.y, v.z);

        static Transform Bone(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = Quaternion.identity;
            return t;
        }

        static FingerChain MakeFinger(Transform hand, string name, Vector3 basePos, Vector3 dir, float[] lengths, float thickness,
            float s, Material material, List<Renderer> renderers)
        {
            var chain = new FingerChain { joints = new Transform[lengths.Length] };
            Transform parent = hand;
            Vector3 local = basePos;
            float w = 0.019f * thickness * s;
            for (int i = 0; i < lengths.Length; i++)
            {
                var joint = Bone(name + (i + 1), parent, local);
                Vector3 to = dir * (lengths[i] * s);
                float w1 = w * (i == lengths.Length - 1 ? 0.78f : 0.92f);
                Segment(joint, to, w, w * 0.9f, w1, w1 * 0.88f, material, renderers);
                chain.joints[i] = joint;
                parent = joint;
                local = to;
                w = w1;
            }
            chain.tip = Bone(name + "Tip", parent, local);
            return chain;
        }

        /// <summary>Tapered hexagonal prism from the bone origin to <paramref name="to"/>, flattened along the palm normal (-Y).</summary>
        static void Segment(Transform bone, Vector3 to, float w0, float h0, float w1, float h1, Material material, List<Renderer> renderers)
        {
            float length = to.magnitude;
            if (length < 1e-5f) return;
            Vector3 fwd = to / length;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, fwd);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.right, fwd);
            up.Normalize();
            Vector3 right = Vector3.Cross(up, fwd);

            const int sides = 6;
            var verts = new List<Vector3>(RingT.Length * sides + 2);
            var tris = new List<int>(RingT.Length * sides * 6 + sides * 6);
            for (int r = 0; r < RingT.Length; r++)
            {
                float t = RingT[r];
                float w = Mathf.Lerp(w0, w1, t) * 0.5f * RingScale[r];
                float h = Mathf.Lerp(h0, h1, t) * 0.5f * RingScale[r];
                Vector3 c = fwd * (length * t);
                for (int k = 0; k < sides; k++)
                {
                    float a = k * Mathf.PI * 2f / sides;
                    verts.Add(c + right * (Mathf.Cos(a) * w) + up * (Mathf.Sin(a) * h));
                }
            }
            for (int r = 0; r < RingT.Length - 1; r++)
            {
                for (int k = 0; k < sides; k++)
                {
                    int a = r * sides + k, b = r * sides + (k + 1) % sides;
                    int c = a + sides, d = b + sides;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }
            int start = verts.Count;
            verts.Add(Vector3.zero);
            int end = verts.Count;
            verts.Add(to);
            int last = (RingT.Length - 1) * sides;
            for (int k = 0; k < sides; k++)
            {
                tris.Add(start); tris.Add(k); tris.Add((k + 1) % sides);
                tris.Add(end); tris.Add(last + (k + 1) % sides); tris.Add(last + k);
            }

            var mesh = new Mesh { name = bone.name + " Segment" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            bone.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = bone.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            mr.allowOcclusionWhenDynamic = false;
            renderers.Add(mr);
        }

        public static void DestroyMeshes(HandRig rig)
        {
            if (rig == null) return;
            foreach (var mf in rig.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) Object.Destroy(mf.sharedMesh);
        }

        public static void DestroyMeshesImmediate(HandRig rig)
        {
            if (rig == null) return;
            foreach (var mf in rig.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) Object.DestroyImmediate(mf.sharedMesh);
        }
    }
}
