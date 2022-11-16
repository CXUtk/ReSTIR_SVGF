using Assets.Pipeline.Paths;
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

namespace Assets.Pipeline
{
    internal class CameraRenderer : IDisposable
    {
        private Camera m_camera;
        private RenderingSettings m_renderingSettings;

        private ScriptableRenderContext m_context;
        private CullingResults m_cullingResults;
        public const int maxDirLightCount = 4;
        public const int maxShadowedDirectionalLightCount = 1;

        private static int
            dirLightCountId = Shader.PropertyToID("_DirectionalLightCount"),
            dirLightColorsId = Shader.PropertyToID("_DirectionalLightColors"),
            dirLightDirectionsId = Shader.PropertyToID("_DirectionalLightDirections");

        private static Vector4[]
            dirLightColors,
            dirLightDirections;

        private static int dirLightColorsIndex, dirLightDirIndex;

        private CommandBuffer m_commandBuffer;

        // private DeferredPath m_deferredPath;
        private RealtimeRayTracingPath m_realtimeRayTracingPath;
        // private Shadows m_shadowRenderer;


        public CameraRenderer(RenderingSettings renderingSettings)
        {
            this.m_renderingSettings = renderingSettings;
            m_realtimeRayTracingPath = new RealtimeRayTracingPath();
            // m_shadowRenderer = new Shadows();

            dirLightColors = new Vector4[4];
            dirLightDirections = new Vector4[4];
        }

        public void Render(Camera camera, ScriptableRenderContext context, RenderingSettings shadowSettings)
        {
            this.m_camera = camera;
            this.m_context = context;

            if (!DoCulling())
            {
                return;
            }

            m_commandBuffer = new CommandBuffer();
            m_commandBuffer.name = "Camrea View";

            PrepareDirectionalLights();
            // RenderShadows();
            // m_realtimeRayTracingPath.RenderGeometry(camera, context, shadowSettings);
            RenderCameraView();
            
            if (Handles.ShouldRenderGizmos())
            {
                m_context.DrawGizmos(m_camera, GizmoSubset.PreImageEffects);
                m_context.DrawGizmos(m_camera, GizmoSubset.PostImageEffects);
            }
            // 提交绘制命令
            m_context.Submit();
        }

        private bool DoCulling()
        {
            if (m_camera.TryGetCullingParameters(out ScriptableCullingParameters p))
            {
                p.shadowDistance = Mathf.Min(m_renderingSettings.ShadowSettings.maxDistance, m_camera.farClipPlane);
                m_cullingResults = m_context.Cull(ref p);
                return true;
            }
            return false;
        }

        private void RenderCameraView()
        {
            m_context.SetupCameraProperties(m_camera);
            RenderVisibleGeometry();
            CleanUpShadow();
        }


        private void RenderVisibleGeometry()
        {
            m_realtimeRayTracingPath.RenderGeometry(m_camera, m_context, m_renderingSettings);
            // m_deferredPath.RenderGeometry(m_camera, m_context, m_renderingSettings);
            // m_camera.TryGetCullingParameters(out var cullingParameters);
            // var cullingResults = m_context.Cull(ref cullingParameters);
            //
            // ShaderTagId shaderTagId = new ShaderTagId("CustomLit");   // 使用 LightMode 为 gbuffer 的 shader
            // SortingSettings sortingSettings = new SortingSettings(m_camera);
            // DrawingSettings drawingSettings = new DrawingSettings(shaderTagId, sortingSettings);
            // FilteringSettings filteringSettings = FilteringSettings.defaultValue;
            //
            // m_context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
        }

        private void PrepareDirectionalLights()
        {
            dirLightColorsIndex = 0;
            dirLightDirIndex = 0;
            
            NativeArray<VisibleLight> visibleLights = m_cullingResults.visibleLights;
            for (int i = 0; i < visibleLights.Length; i++)
            {
                if (visibleLights[i].lightType == LightType.Directional)
                {
                    AddDirectionalLight(visibleLights[i]);
                }

                if (dirLightColorsIndex >= maxDirLightCount)
                {
                    break;
                }
            }

            m_commandBuffer.SetGlobalInt(dirLightCountId, visibleLights.Length);
            m_commandBuffer.SetGlobalVectorArray(dirLightColorsId, dirLightColors);
            m_commandBuffer.SetGlobalVectorArray(dirLightDirectionsId, dirLightDirections);
            ExecuteBuffer();
        }

        private void AddDirectionalLight(in VisibleLight light)
        {
            dirLightColors[dirLightColorsIndex++] = light.finalColor;
            dirLightDirections[dirLightDirIndex++] = -light.localToWorldMatrix.GetColumn(2);
        }

        // private void RenderShadows()
        // {
        //     m_shadowRenderer.Setup(m_camera, m_context, m_renderingSettings, m_cullingResults);
        // }

        private void ExecuteBuffer()
        {
            m_context.ExecuteCommandBuffer(m_commandBuffer);
            m_commandBuffer.Clear();
        }

        private void CleanUpShadow()
        {
            // m_shadowRenderer.Clean();
        }

        public void Dispose()
        {
            m_commandBuffer?.Dispose();
            m_realtimeRayTracingPath?.Dispose();
        }
    }
}
