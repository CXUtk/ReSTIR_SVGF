using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Assets.Pipeline.Settings;
using Assets.Pipeline.SubModules;
using Assets.Pipeline.SubModules.Rendering;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Assets.Pipeline.Paths
{
    internal class DeferredPath
    {
        private ScreenSpaceReflection m_ssrModule;
        private Camera m_camera;
        private ScriptableRenderContext m_context;
        private GBufferPackage[] m_GBuffer;
        private LightingSetting m_lightingSetting;

        private CullingResults m_cullResults;
        private CommandBuffer m_gbufferCommand;
        private int m_gbufferPointer;

        private GBufferPackage CurrentGBuffer => m_GBuffer[m_gbufferPointer];

        public DeferredPath()
        {
            m_camera = null;
            m_gbufferPointer = 0;

            m_GBuffer = new GBufferPackage[2];
            for (int i = 0; i < 2; i++)
            {
                m_GBuffer[i] = new GBufferPackage();
            }
            m_ssrModule = new ScreenSpaceReflection();
            m_ssrModule.OnInitialize();

            m_gbufferCommand = new CommandBuffer();
            m_gbufferCommand.name = "GBuffer";
        }

        private void ExecuteGBufferCommand()
        {
            m_context.ExecuteCommandBuffer(m_gbufferCommand);
            m_gbufferCommand.Clear();
        }

        private void RenderLitPass()
        {
            m_context.SetupCameraProperties(m_camera);
            m_gbufferCommand.ClearRenderTarget(true, true, Color.clear);

            Material litMaterial = new Material(Shader.Find("Custom Deferred/Default"));
            m_gbufferCommand.Blit(CurrentGBuffer.DepthBuffer, m_ssrModule.CurrentFrameTarget, litMaterial, 1);
            ExecuteGBufferCommand();
        }

        public void RenderGeometry(Camera camera, ScriptableRenderContext context, LightingSetting lightingSetting)
        {
            m_lightingSetting = lightingSetting;
            m_gbufferPointer = 1 - m_gbufferPointer;
            m_ssrModule.SetRenderingContext(new RenderingContext()
            {
                Camera = camera,
                SRPContext = context
            });
            m_camera = camera;
            m_context = context;

            m_context.SetupCameraProperties(m_camera);


            if (m_camera.TryGetCullingParameters(out var cullingParameters))
            {
                m_cullResults = m_context.Cull(ref cullingParameters);
            }
            else
            {
                return;
            }

            ShaderTagId shaderTagId = new ShaderTagId("GBuffer_Generate");   // 使用 LightMode 为 gbuffer 的 shader
            SortingSettings sortingSettings = new SortingSettings(m_camera)
            {
                criteria = SortingCriteria.CommonOpaque
            };
            DrawingSettings drawingSettings = new DrawingSettings(shaderTagId, sortingSettings);
            FilteringSettings filteringSettings = FilteringSettings.defaultValue;

            m_gbufferCommand.BeginSample(m_gbufferCommand.name);
            m_gbufferCommand.SetRenderTarget(CurrentGBuffer.GBufferIds, CurrentGBuffer.DepthBuffer);
            m_gbufferCommand.ClearRenderTarget(true, true, Color.clear);
            ExecuteGBufferCommand();

            m_context.DrawRenderers(m_cullResults, ref drawingSettings, ref filteringSettings);

            m_gbufferCommand.EndSample(m_gbufferCommand.name);
            ExecuteGBufferCommand();


            // 设置 gbuffer 为全局纹理
            m_gbufferCommand.SetGlobalTexture("_gdepth", CurrentGBuffer.DepthBuffer);
            m_gbufferCommand.SetGlobalTexture("_albedoR", CurrentGBuffer.GBuffers[0]);
            m_gbufferCommand.SetGlobalTexture("_normalM", CurrentGBuffer.GBuffers[1]);
            m_gbufferCommand.SetGlobalTexture("_motionVector", CurrentGBuffer.GBuffers[2]);
            m_gbufferCommand.SetGlobalTexture("_worldPos", CurrentGBuffer.GBuffers[3]);
            m_gbufferCommand.SetGlobalTexture("_lastFrameScreen", m_ssrModule.LastFrameTarget);
            m_gbufferCommand.SetGlobalTexture("_CubeMap", m_lightingSetting.EnvironmentMap);
            ExecuteGBufferCommand();

            
            m_ssrModule.BeforeRenderScene();
            RenderLitPass();
            // render skybox
            m_context.DrawSkybox(m_camera);
            m_ssrModule.AfterPostProcessing();
        }
    }
}
