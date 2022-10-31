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
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Assets.Pipeline.Paths
{
    internal class RealtimeRayTracingPath
    {
        private Camera m_camera;
        private ScriptableRenderContext m_context;
        private RenderingSettings m_renderSettings;
        
        private GBufferPackage[] m_gBuffer;
        private int m_gbufferPointer;
        private bool m_firstFrame;
        private Matrix4x4 m_vpMatrixPrev;
        
        private CullingResults m_cullResults;
        private RayTracingAccelerationStructure m_rayTracingAccelerationStructure;

        private RenderTexture m_renderTexture;
        private RenderTexture[] m_colorRenderTargets;
        
        private GBufferPackage CurrentGBuffer => m_gBuffer[m_gbufferPointer];
        private RenderTexture CurrentColorTarget => m_colorRenderTargets[0];
        private RenderTexture PrevColorTarget => m_colorRenderTargets[1];

        public RealtimeRayTracingPath()
        {
            m_camera = null;
            m_gbufferPointer = 0;

            m_gBuffer = new GBufferPackage[2];
            for (int i = 0; i < 2; i++)
            {
                m_gBuffer[i] = new GBufferPackage();
            }

            m_vpMatrixPrev = new Matrix4x4();

            m_renderTexture = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            m_colorRenderTargets = new RenderTexture[2];
            for (int i = 0; i < 2; i++)
            {
                m_colorRenderTargets[i] = new RenderTexture(Screen.width, Screen.height, 0,
                    RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear)
                {
                    enableRandomWrite = true
                };
            }
            

            var settings = new RayTracingAccelerationStructure.RASSettings
            {
                layerMask = -1,
                managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic,
                rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything
            };

            m_rayTracingAccelerationStructure = new RayTracingAccelerationStructure(settings);
            m_firstFrame = true;
        }

        private void ExecuteCommand(CommandBuffer cmd)
        {
            m_context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        private void UpdateAccelStructure()
        {
            m_rayTracingAccelerationStructure.Build();
        }

        private void InitialSetMaterials()
        {
            // Turn all standard material to custom material
            GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            var defaultShader = Shader.Find("Standard");
            var replaceShader = Shader.Find("Custom Deferred/Default");
            int count = 0;
            foreach (var gameObject in allObjects)
            {
                var meshRenderer = gameObject.GetComponent<MeshRenderer>();
                if (meshRenderer == null || meshRenderer.material == null) continue;
                var material = meshRenderer.material;
                if (material.shader.Equals(defaultShader))
                {
                    meshRenderer.material = new Material(replaceShader);
                    meshRenderer.material.SetTexture("_Albedo", material.mainTexture);
                    meshRenderer.material.SetColor("_TintColor", material.color);
                    
                    //1.0f - material.GetFloat("_Glossiness")
                    meshRenderer.material.SetFloat("_Roughness", 1.0f);
                    meshRenderer.material.SetFloat("_Metallic", material.GetFloat("_Metallic"));
                }
            }
            Debug.Log($"number of invalid: {count}");
        }

        private void PathTracing(CommandBuffer cmd)
        {
            cmd.SetRenderTarget(CurrentColorTarget);
            cmd.ClearRenderTarget(true, true, Color.clear);
            //
            // Material gBufferPassShader = new Material(Shader.Find("Custom Deferred/Default"));
            // cmd.Blit(m_gBuffer[0].DepthBuffer, m_renderTexture, gBufferPassShader, 1);

            var directIlluminationShader = m_renderSettings.PipelineResourceSetting.DirectRayTracingShader;
            cmd.SetRayTracingTextureParam(directIlluminationShader, "_renderTarget", CurrentColorTarget);
            cmd.SetRayTracingAccelerationStructure(directIlluminationShader, "_RaytracingAccelerationStructure", m_rayTracingAccelerationStructure);
            cmd.SetRayTracingShaderPass(directIlluminationShader, "MyRaytraceShaderPass");
            cmd.DispatchRays(directIlluminationShader, 
                "ShadowDirectIllumination", 
                (uint)CurrentGBuffer.Albedo.width,
                (uint)CurrentGBuffer.Albedo.height, 
                1u,
                m_camera);
                
            var indirectIlluminationShader = m_renderSettings.PipelineResourceSetting.IndirectRayTracingShader;
            cmd.SetRayTracingTextureParam(indirectIlluminationShader, "_renderTarget", CurrentColorTarget);
            // cmd.SetRayTracingAccelerationStructure(indirectIlluminationShader, "_RaytracingAccelerationStructure", m_rayTracingAccelerationStructure);
            cmd.SetRayTracingShaderPass(indirectIlluminationShader, "MyRaytraceShaderPass");
            cmd.DispatchRays(indirectIlluminationShader, 
                "MyRaygenShader", 
                (uint)CurrentGBuffer.Albedo.width,
                (uint)CurrentGBuffer.Albedo.height, 
                1u,
                m_camera);
            
            
            
            cmd.SetGlobalTexture("_prevColorTarget", PrevColorTarget);
            cmd.SetGlobalTexture("_curColorTarget", CurrentColorTarget);
            Material svgfShader = new Material(Shader.Find("RayTracing/SVGF"));
            Material miscShader = new Material(Shader.Find("Utils/Misc"));
            // Apply Temporal Filtering
            cmd.Blit(CurrentColorTarget, m_renderTexture, svgfShader, 0);
            // Copy color
            cmd.Blit(m_renderTexture, PrevColorTarget, miscShader, 2);
        }

        private void DoRayTracingRender()
        {
            if (m_firstFrame)
            {
                var command = new CommandBuffer()
                {
                    name = "Init"
                };
                command.SetRenderTarget(m_renderTexture);
                command.ClearRenderTarget(true, true, Color.clear);
                ExecuteCommand(command);

                // Replace materials
                // InitialSetMaterials();
                UpdateAccelStructure();
                m_firstFrame = false;
            }

            if (Time.frameCount % 1000 == 0)
            {
                UpdateAccelStructure();
            }

            var cmd = new CommandBuffer()
            {
                name = "Do ray tracing"
            };

            m_context.SetupCameraProperties(m_camera);
            using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Generate Rays")))
            {
                cmd.ClearRenderTarget(true, true, Color.clear);

                PathTracing(cmd);

                Material miscShader = new Material(Shader.Find("Utils/Misc"));
                cmd.Blit(m_renderTexture, BuiltinRenderTextureType.CameraTarget, miscShader, 1);
            }
            ExecuteCommand(cmd);
        }

        private void DoPostProcessing()
        {
            
        }

        private void FinalPass()
        {
            //深度值，渲染到纹理。Y要翻转
            Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(m_camera.projectionMatrix, true);
            m_vpMatrixPrev = projectionMatrix * m_camera.worldToCameraMatrix;
        }

        private void SetInitialParameters()
        {
            var cmd = new CommandBuffer()
            {
                name = "Init params"
            };

            cmd.SetGlobalInt("_screenWidth", Screen.width);
            cmd.SetGlobalInt("_screenHeight", Screen.height);
            cmd.SetGlobalInt("_uGlobalFrames", Time.frameCount);
            cmd.SetGlobalMatrix("_vpMatrixPrev", m_vpMatrixPrev);
            ExecuteCommand(cmd);
        }

        private void InitializeStates(Camera camera, ScriptableRenderContext context, RenderingSettings renderingSettings)
        {
            m_camera = camera;
            m_context = context;
            m_renderSettings = renderingSettings;
        }

        private void GenerateGBuffer()
        {
            ShaderTagId shaderTagId = new ShaderTagId("GBuffer_Generate"); // 使用 LightMode 为 gbuffer 的 shader
            SortingSettings sortingSettings = new SortingSettings(m_camera)
            {
                criteria = SortingCriteria.CommonOpaque
            };
            DrawingSettings drawingSettings = new DrawingSettings(shaderTagId, sortingSettings);
            FilteringSettings filteringSettings = FilteringSettings.defaultValue;

            var cmd = new CommandBuffer()
            {
                name = "Generate GBuffer"
            };
            using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Generate")))
            {
                cmd.SetRenderTarget(CurrentGBuffer.GBufferIds, CurrentGBuffer.DepthBuffer);
                cmd.ClearRenderTarget(true, true, Color.clear);
                ExecuteCommand(cmd);
                
                m_context.DrawRenderers(m_cullResults, ref drawingSettings, ref filteringSettings);
            }

            // 设置 gbuffer 为全局纹理
            cmd.SetGlobalTexture("_gdepth", CurrentGBuffer.DepthBuffer);
            cmd.SetGlobalTexture("_albedoR", CurrentGBuffer.GBuffers[0]);
            cmd.SetGlobalTexture("_normalM", CurrentGBuffer.GBuffers[1]);
            cmd.SetGlobalTexture("_motionVector", CurrentGBuffer.GBuffers[2]);
            cmd.SetGlobalTexture("_worldPos", CurrentGBuffer.GBuffers[3]);
            cmd.SetGlobalTexture("_CubeMap", m_renderSettings.LightingSetting.EnvironmentMap);
            ExecuteCommand(cmd);

        }

        public void RenderGeometry(Camera camera, ScriptableRenderContext context, RenderingSettings renderingSettings)
        {
            m_gbufferPointer = 1 - m_gbufferPointer;
            InitializeStates(camera, context, renderingSettings);
            
            m_context.SetupCameraProperties(m_camera);
            
            if (m_camera.TryGetCullingParameters(out var cullingParameters))
            {
                m_cullResults = m_context.Cull(ref cullingParameters);
            }
            else
            {
                return;
            }

            SetInitialParameters();
            GenerateGBuffer();
            DoRayTracingRender();
            m_context.DrawSkybox(m_camera);
            DoPostProcessing();
            FinalPass();
        }
    }
}
