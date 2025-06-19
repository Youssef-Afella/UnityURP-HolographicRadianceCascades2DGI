using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class HolographicRadianceCascades2DGI : ScriptableRendererFeature
{
    [SerializeField] private LayerMask lightElementLayerMask;

    private Material screenUVMat;
    private Material jumpFloodMat;
    private Material distanceFieldMat;
    private ComputeShader giCompute;
    private ComputeShader mergeCompute;

    RC2DGIPass rc2dgiPass;
    public override void Create()
    {
        //Loading materials from Resources folder
        screenUVMat = (Material)Resources.Load("Hidden_RC2DGI_ScreenUV", typeof(Material));
        jumpFloodMat = (Material)Resources.Load("Hidden_RC2DGI_JumpFlood", typeof(Material));
        distanceFieldMat = (Material)Resources.Load("Hidden_RC2DGI_DistanceField", typeof(Material));
        giCompute = (ComputeShader)Resources.Load("HRCCompute", typeof(ComputeShader));
        mergeCompute = (ComputeShader)Resources.Load("MergeCompute", typeof(ComputeShader));

        rc2dgiPass = new RC2DGIPass(lightElementLayerMask, screenUVMat, jumpFloodMat, distanceFieldMat, giCompute, mergeCompute);
        rc2dgiPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(rc2dgiPass);
    }

    class RC2DGIPass : ScriptableRenderPass
    {
        private LayerMask lightLayerMask;

        private Material screenUVMat;
        private Material jumpFloodMat;
        private Material distanceFieldMat;
        private ComputeShader giCompute;
        private ComputeShader mergeCompute;

        private int cascadeCount;
        private float renderScale;

        //private static Vector2Int cascadeResolution;

        private List<ShaderTagId> shaderTagsList = new List<ShaderTagId>();

        public RC2DGIPass(LayerMask lightLayerMask, Material screenUVMat, Material jumpFloodMat, Material distanceFieldMat, ComputeShader giCompute, ComputeShader mergeCompute)
        {
            this.lightLayerMask = lightLayerMask;
            this.screenUVMat = screenUVMat;
            this.jumpFloodMat = jumpFloodMat;
            this.distanceFieldMat = distanceFieldMat;
            this.mergeCompute = mergeCompute;
            this.giCompute = giCompute;

            shaderTagsList.Add(new ShaderTagId("SRPDefaultUnlit"));
            shaderTagsList.Add(new ShaderTagId("UniversalForward"));
            shaderTagsList.Add(new ShaderTagId("UniversalForwardOnly"));

            SetupParameters();
        }

        public void SetupParameters() {
            var volume = VolumeManager.instance.stack.GetComponent<HolographicRadianceCascades2DGIVolume>();

            cascadeCount = volume.cascadeCount.overrideState ? volume.cascadeCount.value : 6;
            renderScale = volume.renderScale.overrideState ? volume.renderScale.value : 1;

            int cascadeWidth = Mathf.CeilToInt((Screen.width * renderScale) / Mathf.Pow(2, cascadeCount)) * (int)Mathf.Pow(2, cascadeCount);
            int cascadeHeight = Mathf.CeilToInt((Screen.height * renderScale) / Mathf.Pow(2, cascadeCount)) * (int)Mathf.Pow(2, cascadeCount);
            //cascadeResolution = new Vector2Int(cascadeWidth, cascadeHeight);
        }

        private class PassData
        {
            internal RendererListHandle rendererListHandle;
            internal TextureHandle colorRT;
            internal TextureHandle distanceRT;
            internal TextureHandle jumpFloodRT1;
            internal TextureHandle jumpFloodRT2;
            internal TextureHandle giRT1;
            internal TextureHandle giRT2;
            internal TextureHandle activeColorTexture;

            internal Material screenUVMat;
            internal Material jumpFloodMat;
            internal Material distanceFieldMat;
            internal Material blitterMat;

            internal ComputeShader giCompute;
            internal ComputeShader mergeCompute;

            internal HolographicRadianceCascades2DGIVolume volume;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var volume = VolumeManager.instance.stack.GetComponent<HolographicRadianceCascades2DGIVolume>();

            if (!volume.isActive.value) return;

            SetupParameters();

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            using (var builder = renderGraph.AddUnsafePass<PassData>("HRC 2D Global Illumination", out var passData))
            {
                Vector2Int textureSize = new Vector2Int(Screen.width, Screen.height);

                RenderTextureDescriptor colorRTDescriptor = new RenderTextureDescriptor(textureSize.x, textureSize.y, RenderTextureFormat.ARGBFloat, 0);
                colorRTDescriptor.enableRandomWrite = true;
                RenderTextureDescriptor distanceRTDescriptor = new RenderTextureDescriptor(textureSize.x, textureSize.y, RenderTextureFormat.RHalf, 0);
                RenderTextureDescriptor jumpFloodRTDescriptor = new RenderTextureDescriptor(textureSize.x, textureSize.y, RenderTextureFormat.RGHalf, 0);
                RenderTextureDescriptor giRTDescriptor = new RenderTextureDescriptor()
                {
                    width = textureSize.x,
                    height = textureSize.y,
                    colorFormat = RenderTextureFormat.ARGBHalf,
                    dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                    volumeDepth = 2,
                    msaaSamples = 1,
                    enableRandomWrite = true,
                    autoGenerateMips = false
                };

                TextureHandle colorRT = UniversalRenderer.CreateRenderGraphTexture(renderGraph, colorRTDescriptor, "_ColorTex", true, FilterMode.Point);
                TextureHandle distanceRT = UniversalRenderer.CreateRenderGraphTexture(renderGraph, distanceRTDescriptor, "_DistanceTex", true, FilterMode.Point);
                TextureHandle jumpFloodRT1 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, jumpFloodRTDescriptor, "_JumpFloodTex1", true, FilterMode.Point);
                TextureHandle jumpFloodRT2 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, jumpFloodRTDescriptor, "_JumpFloodTex2", true, FilterMode.Point);
                TextureHandle giRT1 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, giRTDescriptor, "_GiTex1", true, FilterMode.Bilinear);
                TextureHandle giRT2 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, giRTDescriptor, "_GiTex2", true, FilterMode.Bilinear);

                passData.colorRT = colorRT;
                passData.distanceRT = distanceRT;
                passData.jumpFloodRT1 = jumpFloodRT1;
                passData.jumpFloodRT2 = jumpFloodRT2;
                passData.giRT1 = giRT1;
                passData.giRT2 = giRT2;
                passData.activeColorTexture = resourceData.activeColorTexture;

                passData.screenUVMat = screenUVMat;
                passData.jumpFloodMat = jumpFloodMat;
                passData.distanceFieldMat = distanceFieldMat;

                passData.mergeCompute = mergeCompute;
                passData.giCompute = giCompute;

                passData.volume = volume;

                builder.UseTexture(passData.colorRT, AccessFlags.ReadWrite);
                builder.UseTexture(passData.distanceRT, AccessFlags.ReadWrite);
                builder.UseTexture(passData.jumpFloodRT1, AccessFlags.ReadWrite);
                builder.UseTexture(passData.jumpFloodRT2, AccessFlags.ReadWrite);
                builder.UseTexture(passData.giRT1, AccessFlags.ReadWrite);
                builder.UseTexture(passData.giRT2, AccessFlags.ReadWrite);

                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(shaderTagsList, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
                FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.all, lightLayerMask);

                RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);
                passData.rendererListHandle = renderGraph.CreateRendererList(rendererListParams);

                builder.UseRendererList(passData.rendererListHandle);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }

        static void ExecutePass(PassData passData, UnsafeGraphContext context)
        {
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            cmd.SetRenderTarget(passData.colorRT);
            cmd.DrawRendererList(passData.rendererListHandle);

            Vector2 screen = new Vector2(Screen.width, Screen.height);
            cmd.SetGlobalVector("_Aspect", screen / Mathf.Max(screen.x, screen.y));

            cmd.Blit(passData.colorRT, passData.jumpFloodRT1, passData.screenUVMat, 0);

            int max = Mathf.Max(Screen.width, Screen.height);
            int steps = Mathf.CeilToInt(Mathf.Log(max));
            float stepSize = 1;

            bool flip = false;
            for (int n = 0; n < steps; n++)
            {
                stepSize *= 0.5f;
                cmd.SetGlobalFloat("_StepSize", stepSize);

                cmd.Blit(flip ? passData.jumpFloodRT2 : passData.jumpFloodRT1, flip ? passData.jumpFloodRT1 : passData.jumpFloodRT2, passData.jumpFloodMat);
                flip = !flip;
            }

            cmd.Blit(flip ? passData.jumpFloodRT2 : passData.jumpFloodRT1, passData.distanceRT, passData.distanceFieldMat);

            ComputeShader giCompute = passData.giCompute;
            HolographicRadianceCascades2DGIVolume volume = passData.volume;

            cmd.SetComputeTextureParam(giCompute, 0, "ColorTex", passData.colorRT);
            cmd.SetComputeTextureParam(giCompute, 0, "DistanceTex", passData.distanceRT);
            cmd.SetComputeIntParam(giCompute, "CascadeCount", volume.cascadeCount.value);
            cmd.SetComputeFloatParam(giCompute, "SkyRadiance", volume.skyRadiance.value ? 1 : 0);
            cmd.SetComputeVectorParam(giCompute, "SkyColor", volume.skyColor.value);
            cmd.SetComputeVectorParam(giCompute, "SunColor", volume.sunColor.value);
            cmd.SetComputeFloatParam(giCompute, "SunAngle", volume.sunAngle.value);
            cmd.SetComputeVectorParam(giCompute, "Aspect", screen / Mathf.Max(screen.x, screen.y));
            cmd.SetComputeVectorParam(giCompute, "Resolution", screen);
            //cmd.SetComputeVectorParam(giCompute, "CascadeResolution", (Vector2)cascadeResolution);

            for (int i = volume.cascadeCount.value - 1; i >= 0; i--)
            {
                cmd.SetComputeIntParam(giCompute, "CascadeLevel", i);
                cmd.SetComputeTextureParam(giCompute, 0, "UpperCascade", i % 2 == 0 ? passData.giRT2 : passData.giRT1);
                cmd.SetComputeTextureParam(giCompute, 0, "LowerCascade", i % 2 == 0 ? passData.giRT1 : passData.giRT2);
                cmd.DispatchCompute(giCompute, 0, Mathf.CeilToInt(screen.x / 8), Mathf.CeilToInt(screen.y / 8), 2);
            }

            ComputeShader mergeCompute = passData.mergeCompute;
            cmd.SetComputeTextureParam(mergeCompute, 0, "Array", passData.giRT1);
            cmd.SetComputeTextureParam(mergeCompute, 0, "Result", passData.colorRT);
            cmd.SetComputeVectorParam(mergeCompute, "ScreenResolution", screen);
            cmd.DispatchCompute(mergeCompute, 0, Mathf.CeilToInt(screen.x / 8), Mathf.CeilToInt(screen.y / 8), 1);

            cmd.Blit(passData.colorRT, passData.activeColorTexture);
        }

    }

}
