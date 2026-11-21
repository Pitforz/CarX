using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace CarX.DarkOrientation
{
    /// <summary>
    /// Enqueued from script for the player camera only while the effect is visible, so no renderer asset changes.
    /// Hand mask → Dual Kawase blur of the frame → composite (depth-map zones, dimmed blur, outlined hands).
    /// </summary>
    internal sealed class DarkOrientationRenderPass : ScriptableRenderPass
    {
        static class Ids
        {
            public static readonly int SceneDepth = Shader.PropertyToID("_DO_SceneDepth");
            public static readonly int ViewProj = Shader.PropertyToID("_DO_ViewProj");
            public static readonly int CamPos = Shader.PropertyToID("_DO_CamPos");
            public static readonly int CamFwd = Shader.PropertyToID("_DO_CamFwd");
            public static readonly int ZParams = Shader.PropertyToID("_DO_ZParams");
            public static readonly int Blur = Shader.PropertyToID("_DO_Blur");
            public static readonly int Mask = Shader.PropertyToID("_DO_Mask");
            public static readonly int ScreenSize = Shader.PropertyToID("_DO_ScreenSize");
            public static readonly int RayBL = Shader.PropertyToID("_DO_RayBL");
            public static readonly int RayBR = Shader.PropertyToID("_DO_RayBR");
            public static readonly int RayTL = Shader.PropertyToID("_DO_RayTL");
            public static readonly int RayTR = Shader.PropertyToID("_DO_RayTR");
            public static readonly int Zones = Shader.PropertyToID("_DO_Zones");
            public static readonly int ZoneWeights = Shader.PropertyToID("_DO_ZoneWeights");
            public static readonly int ZoneParams = Shader.PropertyToID("_DO_ZoneParams");
            public static readonly int RingParams = Shader.PropertyToID("_DO_RingParams");
            // Normals go as separate vectors, not an array: a second array on this material did not arrive.
            public static readonly int[] ZoneNormals =
            {
                Shader.PropertyToID("_DO_ZoneN0"), Shader.PropertyToID("_DO_ZoneN1"),
                Shader.PropertyToID("_DO_ZoneN2"), Shader.PropertyToID("_DO_ZoneN3"),
            };
            public static readonly int DepthNear = Shader.PropertyToID("_DO_DepthNear");
            public static readonly int DepthFar = Shader.PropertyToID("_DO_DepthFar");
            public static readonly int DepthRange = Shader.PropertyToID("_DO_DepthRange");
            public static readonly int ZoneMerge = Shader.PropertyToID("_DO_ZoneMerge");
            public static readonly int Look = Shader.PropertyToID("_DO_Look");
            public static readonly int HandFill = Shader.PropertyToID("_DO_HandFill");
            public static readonly int HandOutline = Shader.PropertyToID("_DO_HandOutline");
            public static readonly int HandInner = Shader.PropertyToID("_DO_HandInner");
            public static readonly int HandParams = Shader.PropertyToID("_DO_HandParams");
        }

        const int MaxBlurLevels = 6;
        static readonly string[] BlurNames = { "DarkOrientation_Blur0", "DarkOrientation_Blur1", "DarkOrientation_Blur2",
            "DarkOrientation_Blur3", "DarkOrientation_Blur4", "DarkOrientation_Blur5" };

        sealed class MaskPassData
        {
            public List<Renderer> renderers;
            public Material material;
            public TextureHandle depth;
        }

        sealed class CompositePassData
        {
            public TextureHandle source;
            public TextureHandle blur;
            public TextureHandle mask;
            public TextureHandle depth;
            public Material material;
        }

        readonly DarkOrientationController _owner;
        readonly TextureHandle[] _levels = new TextureHandle[MaxBlurLevels];

        public DarkOrientationRenderPass(DarkOrientationController owner)
        {
            _owner = owner;
            profilingSampler = new ProfilingSampler("CarX Dark Orientation");
            // Reads the frame it draws over, so the camera must render into an intermediate texture.
            requiresIntermediateTexture = true;
        }

        public void Setup(DarkOrientationSettings settings)
        {
            renderPassEvent = settings.renderEvent;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var owner = _owner;
            var s = owner.Settings;
            if (owner.MaskMaterial == null || owner.BlurMaterial == null || owner.CompositeMaterial == null) return;
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (resources.isActiveTargetBackBuffer) return;
            TextureHandle source = resources.activeColorTexture;
            TextureHandle depth = resources.cameraDepthTexture;
            if (!source.IsValid() || !depth.IsValid()) return;

            var sourceDesc = renderGraph.GetTextureDesc(source);
            int width = sourceDesc.width, height = sourceDesc.height;
            if (width <= 0 || height <= 0)
            {
                width = cameraData.cameraTargetDescriptor.width;
                height = cameraData.cameraTargetDescriptor.height;
            }
            SetParameters(owner, s, cameraData.camera, width, height);

            var maskDesc = new TextureDesc(width, height)
            {
                name = "DarkOrientation_HandMask",
                format = GraphicsFormat.R16G16_SFloat,
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            TextureHandle mask = renderGraph.CreateTexture(maskDesc);
            using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>("DarkOrientation Hand Mask", out var data, profilingSampler))
            {
                data.renderers = owner.HandDraws;
                data.material = owner.MaskMaterial;
                data.depth = depth;
                builder.UseTexture(depth);
                builder.SetRenderAttachment(mask, 0);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (MaskPassData d, RasterGraphContext ctx) =>
                {
                    d.material.SetTexture(Ids.SceneDepth, (Texture)d.depth);
                    foreach (var r in d.renderers)
                    {
                        // Mask meshes stay disabled so the camera does not draw the baked copy.
                        // DrawRenderer still submits them.
                        if (r == null || !r.gameObject.activeInHierarchy) continue;
                        int submeshes = SubmeshCount(r);
                        for (int i = 0; i < submeshes; i++) ctx.cmd.DrawRenderer(r, d.material, i, 0);
                    }
                });
            }

            int iterations = Mathf.Clamp(s.blurIterations, 1, MaxBlurLevels);
            TextureHandle current = source;
            int w = width, h = height;
            for (int i = 0; i < iterations; i++)
            {
                w = Mathf.Max(1, w / 2);
                h = Mathf.Max(1, h / 2);
                var desc = new TextureDesc(w, h)
                {
                    name = BlurNames[i],
                    format = sourceDesc.format,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = false,
                };
                _levels[i] = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(current, _levels[i], owner.BlurMaterial, 0), "DarkOrientation Blur Down");
                current = _levels[i];
            }
            for (int i = iterations - 2; i >= 0; i--)
            {
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(current, _levels[i], owner.BlurMaterial, 1), "DarkOrientation Blur Up");
                current = _levels[i];
            }

            var destDesc = sourceDesc;
            destDesc.name = "DarkOrientation_Color";
            destDesc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(destDesc);
            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("DarkOrientation Composite", out var data, profilingSampler))
            {
                data.source = source;
                data.blur = current;
                data.mask = mask;
                data.depth = depth;
                data.material = owner.CompositeMaterial;
                builder.UseTexture(source);
                builder.UseTexture(current);
                builder.UseTexture(mask);
                builder.UseTexture(depth);
                builder.SetRenderAttachment(destination, 0);
                builder.SetRenderFunc(static (CompositePassData d, RasterGraphContext ctx) =>
                {
                    d.material.SetTexture(Ids.Blur, (Texture)d.blur);
                    d.material.SetTexture(Ids.Mask, (Texture)d.mask);
                    d.material.SetTexture(Ids.SceneDepth, (Texture)d.depth);
                    Blitter.BlitTexture(ctx.cmd, (RTHandle)d.source, new Vector4(1f, 1f, 0f, 0f), d.material, 0);
                });
            }
            resources.cameraColor = destination;
        }

        static int SubmeshCount(Renderer r)
        {
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
            else if (r.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
            return mesh != null ? Mathf.Max(1, mesh.subMeshCount) : 1;
        }

        static Vector4 ZBufferParams(Camera cam)
        {
            float n = cam.nearClipPlane, f = cam.farClipPlane;
            if (SystemInfo.usesReversedZBuffer)
            {
                float x = -1f + f / n;
                return new Vector4(x, 1f, x / f, 1f / f);
            }
            float a = 1f - f / n, b = f / n;
            return new Vector4(a, b, a / f, b / f);
        }

        // The materials are drawn once per frame, so per-frame values can be set while recording.
        static void SetParameters(DarkOrientationController owner, DarkOrientationSettings s, Camera cam, int width, int height)
        {
            var t = cam.transform;
            Vector3 camPos = t.position;
            Vector3 camFwd = t.forward;
            Vector4 zParams = ZBufferParams(cam);
            Matrix4x4 viewProj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * cam.worldToCameraMatrix;

            var mask = owner.MaskMaterial;
            mask.SetMatrix(Ids.ViewProj, viewProj);
            mask.SetVector(Ids.CamPos, camPos);
            mask.SetVector(Ids.CamFwd, camFwd);
            mask.SetVector(Ids.ZParams, zParams);

            var c = owner.CompositeMaterial;
            c.SetVector(Ids.ScreenSize, new Vector4(width, height, 1f / width, 1f / height));
            c.SetVector(Ids.ZParams, zParams);
            c.SetVector(Ids.CamPos, camPos);
            c.SetVector(Ids.CamFwd, camFwd);
            // Rays to the far corners scaled so their forward component is 1: world = camPos + ray * eyeDepth.
            c.SetVector(Ids.RayBL, cam.ViewportToWorldPoint(new Vector3(0f, 0f, 1f)) - camPos);
            c.SetVector(Ids.RayBR, cam.ViewportToWorldPoint(new Vector3(1f, 0f, 1f)) - camPos);
            c.SetVector(Ids.RayTL, cam.ViewportToWorldPoint(new Vector3(0f, 1f, 1f)) - camPos);
            c.SetVector(Ids.RayTR, cam.ViewportToWorldPoint(new Vector3(1f, 1f, 1f)) - camPos);
            c.SetVectorArray(Ids.Zones, owner.ZoneCenters);
            c.SetVector(Ids.ZoneWeights, owner.ZoneWeights);
            for (int i = 0; i < Ids.ZoneNormals.Length && i < owner.ZoneNormals.Length; i++)
                c.SetVector(Ids.ZoneNormals[i], owner.ZoneNormals[i]);
            float wallEnd = Mathf.Max(s.wallZoneEnd, s.wallZoneCore + 0.01f);
            float objectEnd = Mathf.Max(s.objectZoneEnd, s.objectZoneCore + 0.01f);
            float k = Mathf.Max(0.01f, s.zoneScale);
            c.SetVector(Ids.ZoneParams, new Vector4(s.wallZoneCore * k, wallEnd * k, s.objectZoneCore * k, objectEnd * k));
            c.SetVector(Ids.RingParams, new Vector4(s.ringSpacing, s.depthRings, Mathf.Cos(s.faceCutoffAngle * Mathf.Deg2Rad), 0f));
            c.SetColor(Ids.DepthNear, s.depthNear);
            c.SetColor(Ids.DepthFar, s.depthFar);
            c.SetVector(Ids.DepthRange, new Vector4(s.depthNearDistance, Mathf.Max(s.depthNearDistance + 0.05f, s.depthFarDistance), s.depthCurve, s.depthSideShade));
            c.SetVector(Ids.ZoneMerge, new Vector4(s.zoneMerge, 0f, 0f, 0f));
            c.SetVector(Ids.Look, new Vector4(s.dim, s.desaturate, s.depthEdges, owner.Blend));
            bool solid = s.handStyle == HandStyle.SolidWhite;
            c.SetColor(Ids.HandFill, solid ? s.outlineColor * 0.85f : s.handFill);
            c.SetColor(Ids.HandOutline, s.outlineColor);
            c.SetColor(Ids.HandInner, solid ? new Color(0.25f, 0.25f, 0.27f, 1f) : s.outlineColor * 0.85f);
            c.SetVector(Ids.HandParams, new Vector4(s.outlineWidth * height / 1080f, s.innerLines, 0f, 0f));
        }
    }
}
