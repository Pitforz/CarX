using System.Collections.Generic;
using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    public sealed class SleepingMeshBreathing : MonoBehaviour
    {
        const string BlendShape = "Sleeping Breath";
        const float BreathingPeriod = 5.4f;
        const float ChestRise = 0.007f;

        readonly List<SkinnedMeshRenderer> _renderers = new();

        void Awake()
        {
            foreach (var filter in GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.TryGetComponent<MeshRenderer>(out var original) || !original.enabled ||
                    filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                    continue;

                AddBreathingRenderer(filter, original);
            }
        }

        void AddBreathingRenderer(MeshFilter filter, MeshRenderer original)
        {
            var vertices = filter.sharedMesh.vertices;
            var displacement = new Vector3[vertices.Length];
            Matrix4x4 meshToSleeper = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Vector3 inhaleDirection = meshToSleeper.inverse.MultiplyVector(Vector3.up * ChestRise);
            bool movesWithChest = false;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 point = meshToSleeper.MultiplyPoint3x4(vertices[i]);
                float alongChest = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(point.z + 0.39f) / 0.24f);
                float aboveMattress = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.34f, point.y));
                float weight = alongChest * aboveMattress;
                displacement[i] = inhaleDirection * weight;
                movesWithChest |= weight > 0f;
            }

            if (!movesWithChest)
                return;

            var mesh = Instantiate(filter.sharedMesh);
            // A single stationary bone preserves the authored pose while the blend shape raises the chest.
            var weights = new BoneWeight[vertices.Length];
            System.Array.Fill(weights, new BoneWeight { weight0 = 1f });
            mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.boneWeights = weights;
            mesh.AddBlendShapeFrame(BlendShape, 100f, displacement, null, null);

            var animated = new GameObject(BlendShape) { layer = original.gameObject.layer };
            animated.transform.SetParent(filter.transform, false);
            var renderer = animated.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterials = original.sharedMaterials;
            renderer.rootBone = filter.transform;
            renderer.bones = new[] { filter.transform };
            renderer.shadowCastingMode = original.shadowCastingMode;
            renderer.receiveShadows = original.receiveShadows;
            renderer.renderingLayerMask = original.renderingLayerMask;
            renderer.lightProbeUsage = original.lightProbeUsage;
            renderer.reflectionProbeUsage = original.reflectionProbeUsage;
            renderer.probeAnchor = original.probeAnchor;
            Bounds bounds = mesh.bounds;
            bounds.Expand(ChestRise * 2f);
            renderer.localBounds = bounds;

            original.enabled = false;
            _renderers.Add(renderer);
        }

        void LateUpdate()
        {
            float phase = Mathf.Repeat(Time.time, BreathingPeriod) / BreathingPeriod;
            const float inhaleFraction = 0.4f;
            float breath = phase < inhaleFraction
                ? Mathf.SmoothStep(0f, 100f, phase / inhaleFraction)
                : Mathf.SmoothStep(100f, 0f, Mathf.InverseLerp(inhaleFraction, 1f, phase));
            foreach (var renderer in _renderers)
                renderer.SetBlendShapeWeight(0, breath);
        }

        void OnDestroy()
        {
            foreach (var renderer in _renderers)
                if (renderer != null)
                    Destroy(renderer.sharedMesh);
        }
    }
}
