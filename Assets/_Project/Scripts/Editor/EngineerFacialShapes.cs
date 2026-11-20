using System;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    sealed class EngineerFacialShapes : AssetPostprocessor
    {
        const string ModelPath = "Assets/_Project/Models/Engineer/engineer.fbx";
        const float Centimeters = 100f;

        public override uint GetVersion() => 2;

        void OnPostprocessModel(GameObject model)
        {
            bool danny = assetPath == "Assets/_Project/Models/Danny/danny.fbx";
            if (assetPath != ModelPath && !danny)
                return;

            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = renderer.sharedMesh;
                switch (renderer.name)
                {
                    case "Body":
                        AddShape(mesh, "MouthOpen", p => OpenMouth(MouthCoordinates(p, danny)));
                        AddShape(mesh, "LipRound", p => RoundLips(MouthCoordinates(p, danny)));
                        AddShape(mesh, "LipSpread", p => SpreadLips(MouthCoordinates(p, danny)));
                        AddShape(mesh, "LipPress", p => PressLips(MouthCoordinates(p, danny)));
                        AddShape(mesh, "Blink", p => CloseEyelid(danny ? p - new Vector3(0f, 3.1f, 0f) : p));
                        break;
                    case "Eyelashes":
                        AddShape(mesh, "Blink", p => CloseEyelid(danny ? p - new Vector3(0f, 3.1f, 0f) : p));
                        break;
                    case "Eyes":
                        AddEyeRotation(mesh, "LookLeft", Vector3.up, -18f, danny);
                        AddEyeRotation(mesh, "LookRight", Vector3.up, 18f, danny);
                        AddEyeRotation(mesh, "LookUp", Vector3.right, -12f, danny);
                        AddEyeRotation(mesh, "LookDown", Vector3.right, 12f, danny);
                        break;
                }
            }
        }

        static Vector3 MouthCoordinates(Vector3 point, bool danny) =>
            danny ? point + new Vector3(0f, -3.67f, 0.3f) : point;

        static void AddShape(Mesh mesh, string name, Func<Vector3, Vector3> deform)
        {
            var positions = mesh.vertices;
            var deltas = new Vector3[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                deltas[i] = deform(positions[i] * Centimeters) / Centimeters;
            mesh.AddBlendShapeFrame(name, 100f, deltas, null, null);
        }

        static void AddEyeRotation(Mesh mesh, string name, Vector3 axis, float angle, bool danny)
        {
            var rotation = Quaternion.AngleAxis(angle, axis);
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var deltaVertices = new Vector3[vertices.Length];
            var deltaNormals = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var center = new Vector3(vertices[i].x > 0f ? 0.02991f : -0.02994f, 1.6718f, 0.08933f);
                if (danny)
                    center = new Vector3(vertices[i].x > 0f ? 0.0297f : -0.0297f, 1.7025f, 0.0912f);
                var offset = vertices[i] - center;
                deltaVertices[i] = rotation * offset - offset;
                deltaNormals[i] = rotation * normals[i] - normals[i];
            }
            mesh.AddBlendShapeFrame(name, 100f, deltaVertices, deltaNormals, null);
        }

        // The sculpting coordinates match the neutral FBX in centimeters. Equal positions
        // get equal deltas across UV seams. The original model already includes teeth and gums.
        static Vector3 OpenMouth(Vector3 p)
        {
            float x = Mathf.Abs(p.x);
            float seam = 159.03f - 0.31f * Smooth(0.6f, 3.3f, x);
            float belowSeam = 1f - Smooth(seam - 0.15f, seam + 0.08f, p.y);
            float jaw = belowSeam * Smooth(150.8f, 156.7f, p.y) * Smooth(1.5f, 9.5f, p.z) *
                (1f - Smooth(2.0f, 7.8f, x));
            var fromHinge = p - new Vector3(0f, 164.6f, 1.65f);
            var delta = (Quaternion.AngleAxis(10f, Vector3.right) * fromHinge - fromHinge) * jaw;
            float upperLip = LipMask(p) * Smooth(seam, seam + 0.28f, p.y) *
                (1f - Smooth(159.6f, 160.45f, p.y));
            delta.y += 0.24f * upperLip;
            delta.x -= p.x * 0.022f * LipMask(p);
            return delta;
        }

        static Vector3 RoundLips(Vector3 p)
        {
            float mask = LipMask(p);
            return new Vector3(-p.x * 0.31f, (159.03f - p.y) * 0.12f, 0.55f) * mask;
        }

        static Vector3 SpreadLips(Vector3 p)
        {
            float mask = LipMask(p);
            return new Vector3(p.x * 0.22f, Mathf.Abs(p.x) * 0.087f, -0.27f) * mask;
        }

        static Vector3 PressLips(Vector3 p)
        {
            float mask = LipMask(p);
            float seam = 159.03f - 0.31f * Smooth(0.6f, 3.3f, Mathf.Abs(p.x));
            return new Vector3(p.x * 0.047f, (seam - p.y) * 0.28f, -0.11f) * mask;
        }

        static float LipMask(Vector3 p) =>
            (1f - Smooth(2.1f, 4.65f, Mathf.Abs(p.x))) *
            Smooth(157.4f, 158.65f, p.y) * (1f - Smooth(159.7f, 160.85f, p.y)) *
            Smooth(10.6f, 11.45f, p.z);

        static Vector3 CloseEyelid(Vector3 p)
        {
            float fromEye = Mathf.Abs(Mathf.Abs(p.x) - 3.05f);
            float centerWeight = (1f - Smooth(0.72f, 1.88f, fromEye)) * Smooth(9.0f, 10.3f, p.z);
            float upper = Smooth(166.42f, 166.85f, p.y) * (1f - Smooth(167.38f, 169.05f, p.y));
            float lower = Smooth(164.92f, 165.96f, p.y) * (1f - Smooth(166.30f, 166.65f, p.y));
            return new Vector3(0f, centerWeight * (-1.06f * upper + 0.2f * lower),
                centerWeight * (0.11f * upper + 0.04f * lower));
        }

        static float Smooth(float low, float high, float value)
        {
            float t = Mathf.InverseLerp(low, high, value);
            return t * t * (3f - 2f * t);
        }
    }
}
