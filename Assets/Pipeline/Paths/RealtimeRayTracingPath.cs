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
using UnityEngine.Diagnostics;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Assets.Pipeline.Paths
{
    internal class RealtimeRayTracingPath : IDisposable
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
        private RenderTexture m_swapTexture;
        // private RenderTexture m_temporalMeanTextureW, m_temporalMean2TextureW;
        private RenderTexture[] m_varianceTexture;
        private RenderTexture[] m_colorRenderTargets;
        private ComputeBuffer[] m_temporalBuffers;
        
        private GBufferPackage CurrentGBuffer => m_gBuffer[m_gbufferPointer];
        private GBufferPackage PrevGBuffer => m_gBuffer[1 - m_gbufferPointer];
        
        private ComputeBuffer CurrentTemporalBuffer => m_temporalBuffers[m_gbufferPointer];
        private ComputeBuffer PrevTemporalBuffer => m_temporalBuffers[1 - m_gbufferPointer];
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
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            m_swapTexture = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true
            };
            m_swapTexture.Create();

            m_varianceTexture = new RenderTexture[2];
            for (int i = 0; i < 2; i++)
            {
                m_varianceTexture[i] = new RenderTexture(Screen.width, Screen.height, 0,
                    RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                {
                    enableRandomWrite = true
                };
                m_varianceTexture[i].Create();
            }

            // temporal variance 

            m_temporalBuffers = new ComputeBuffer[2];
            for (int i = 0; i < 2; i++)
            {
                m_temporalBuffers[i] = new ComputeBuffer(Screen.width * Screen.height, sizeof(float) * 13,
                    ComputeBufferType.Structured);
            }
            // m_temporalMeanTextureR = new RenderTexture(Screen.width, Screen.height, 0,
            //     RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            // m_temporalMean2TextureR = new RenderTexture(Screen.width, Screen.height, 0,
            //     RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            //
            // // temporal variance read/write buffer
            // m_temporalMeanTextureW = new RenderTexture(Screen.width, Screen.height, 0,
            //     RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear)
            // {
            //     enableRandomWrite = true
            // };
            // m_temporalMeanTextureW.Create();
            // m_temporalMean2TextureW = new RenderTexture(Screen.width, Screen.height, 0,
            //     RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear)
            // {
            //     enableRandomWrite = true
            // };
            // m_temporalMean2TextureW.Create();
            
            m_colorRenderTargets = new RenderTexture[2];
            for (int i = 0; i < 2; i++)
            {
                m_colorRenderTargets[i] = new RenderTexture(Screen.width, Screen.height, 0,
                    RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                {
                    enableRandomWrite = true
                };
                m_colorRenderTargets[i].Create();
            }
            

            var settings = new RayTracingAccelerationStructure.RASSettings
            {
                layerMask = -1,
                managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic,
                rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything
            };

            m_rayTracingAccelerationStructure = new RayTracingAccelerationStructure(settings);
            m_rayTracingAccelerationStructure.Build();
            m_firstFrame = true;
        }

        private void ExecuteCommand(CommandBuffer cmd)
        {
            m_context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        private void UpdateAccelStructure(CommandBuffer cmd)
        {
            cmd.BuildRayTracingAccelerationStructure(m_rayTracingAccelerationStructure);
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
                int numMaterials = meshRenderer.materials.Length;
                for (int i = 0; i < numMaterials; i++)
                {
                    var material = meshRenderer.sharedMaterials[i];
                    if (material.shader.name.Equals("Standard"))
                    {
                        var mat2 = new Material(replaceShader)
                        {
                            name = "M" + material.name
                        };
                        meshRenderer.materials[i].shader = replaceShader;
                        meshRenderer.materials[i].SetTexture("_Albedo", material.mainTexture);
                        meshRenderer.materials[i].SetColor("_TintColor", material.color);
                    
                        //1.0f - material.GetFloat("_Glossiness")
                        meshRenderer.materials[i].SetFloat("_Roughness", 1.0f);
                        meshRenderer.materials[i].SetFloat("_Metallic", material.GetFloat("_Metallic"));
                    }
                }
                
            }
            Debug.Log($"number of invalid: {count}");
        }

        private void Filtering_PrepareData(CommandBuffer cmd)
        {
            var filterShader = m_renderSettings.PipelineResourceSetting.SVGFFilterShader;
            cmd.SetComputeBufferParam(filterShader, 2, "_temporalBufferR", PrevTemporalBuffer);
            cmd.SetComputeBufferParam(filterShader, 2, "_temporalBufferW", CurrentTemporalBuffer);
            cmd.SetComputeIntParam(filterShader, "_screenWidth", m_renderTexture.width);
            cmd.SetComputeIntParam(filterShader, "_screenHeight", m_renderTexture.height);
            
            cmd.SetGlobalTexture("_prevColorTarget", PrevColorTarget);
            cmd.SetGlobalTexture("_curColorTarget", CurrentColorTarget);
            cmd.SetGlobalFloat("_temporalFactor", m_renderSettings.DenoisingSettings.TemporalFactor);
        }

        private int DivCeil(int n, int d)
        {
            return (n + d - 1) / d;
        }
        
        private void Filtering(CommandBuffer cmd)
        {
            Filtering_PrepareData(cmd);
            
            Material miscShader = new Material(Shader.Find("Utils/Misc"));
            Material svgfShader = new Material(Shader.Find("RayTracing/SVGF"));
            var filterShader = m_renderSettings.PipelineResourceSetting.SVGFFilterShader;

            if (!m_renderSettings.DenoisingSettings.GroundTruth)
            {
                using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Temporal Variance")))
                {
                    cmd.DispatchCompute(filterShader, 2,
                        DivCeil(m_renderTexture.width, 16),
                        DivCeil(m_renderTexture.height, 16),
                        1);
                }

                using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Variance Estimate")))
                {
                    cmd.SetGlobalInt("_sigmaN", m_renderSettings.DenoisingSettings.SigmaN);
                    cmd.SetGlobalFloat("_sigmaZ", m_renderSettings.DenoisingSettings.SigmaZ);
                    cmd.SetGlobalFloat("_sigmaC", m_renderSettings.DenoisingSettings.SigmaC);
                    cmd.SetGlobalFloat("_sigmaX", m_renderSettings.DenoisingSettings.SigmaX);

                    // Variance Estimate
                    cmd.SetGlobalBuffer("_temporalBufferR", CurrentTemporalBuffer);
                    cmd.Blit(CurrentColorTarget, m_varianceTexture[0], svgfShader, 3);
                    // cmd.Blit(m_varianceTexture[0], m_renderTexture);
                    // return;
                }

                using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Wavelet Transform Level 1")))
                {
                    cmd.SetComputeTextureParam(filterShader, 0, "_varianceDataR", m_varianceTexture[0]);
                    cmd.SetComputeTextureParam(filterShader, 0, "_renderR", CurrentColorTarget);
                    cmd.SetComputeTextureParam(filterShader, 0, "_varianceDataW", m_varianceTexture[1]);
                    cmd.SetComputeTextureParam(filterShader, 0, "_renderW", m_swapTexture);

                    // Wavelet transform, Level 0
                    cmd.SetComputeIntParam(filterShader, "_filterLevel", 0);
                    cmd.DispatchCompute(filterShader, 0,
                        DivCeil(m_renderTexture.width, 16),
                        DivCeil(m_renderTexture.height, 16),
                        1);
                    cmd.Blit(m_swapTexture, CurrentColorTarget);
                }
            }

            using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Apply Temporal Filtering on Level 1")))
            {
                cmd.SetGlobalTexture("_varianceTarget", m_varianceTexture[1]);
                // Apply Temporal Filtering
                cmd.Blit(CurrentColorTarget, m_renderTexture, svgfShader, 0);
                // cmd.Blit(CurrentColorTarget, m_renderTexture);

                // Copy color with sample count
                cmd.Blit(m_renderTexture, PrevColorTarget);

            }

            if (!m_renderSettings.DenoisingSettings.GroundTruth)
            {
                using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Wavelet Transform Level 2+")))
                {
                    cmd.Blit(m_renderTexture, CurrentColorTarget, miscShader, 2);
                    // Wavelet transform, Level 1+
                    for (int i = 0; i < 2; i++)
                    {
                        cmd.SetComputeTextureParam(filterShader, 0, "_varianceDataR", m_varianceTexture[1]);
                        cmd.SetComputeTextureParam(filterShader, 0, "_renderR", CurrentColorTarget);
                        cmd.SetComputeIntParam(filterShader, "_filterLevel", i * 2 + 1);
                        cmd.SetComputeTextureParam(filterShader, 0, "_varianceDataW", m_varianceTexture[0]);
                        cmd.SetComputeTextureParam(filterShader, 0, "_renderW", m_swapTexture);
                        cmd.DispatchCompute(filterShader, 0,
                            DivCeil(m_renderTexture.width, 8),
                            DivCeil(m_renderTexture.height, 8),
                            1);
                        //cmd.Blit(CurrentColorTarget, m_swapTexture, svgfShader, 1);

                        cmd.SetComputeTextureParam(filterShader, 0, "_varianceDataR", m_varianceTexture[0]);
                        cmd.SetComputeTextureParam(filterShader, 0, "_renderR", m_swapTexture);
                        cmd.SetComputeIntParam(filterShader, "_filterLevel", i * 2 + 2);
                        cmd.SetComputeTextureParam(filterShader, 0, "_varianceDataW", m_varianceTexture[1]);
                        cmd.SetComputeTextureParam(filterShader, 0, "_renderW", CurrentColorTarget);
                        cmd.DispatchCompute(filterShader, 0,
                            DivCeil(m_renderTexture.width, 8),
                            DivCeil(m_renderTexture.height, 8),
                            1);
                        // cmd.Blit(m_swapTexture, CurrentColorTarget, svgfShader, 1);
                    }
                }
                // Final apply albedo
                cmd.Blit(CurrentColorTarget, m_renderTexture, svgfShader, 2);
            }
            else
            {

                cmd.Blit(m_renderTexture, CurrentColorTarget, miscShader, 2);
                // cmd.Blit(CurrentGBuffer.GBuffers[2], m_renderTexture, miscShader, 2);
                // Final apply albedo
                cmd.Blit(CurrentColorTarget, m_renderTexture, svgfShader, 2);
            }


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
            cmd.SetRayTracingAccelerationStructure(directIlluminationShader, 
                "_RaytracingAccelerationStructure", m_rayTracingAccelerationStructure);
            cmd.SetRayTracingShaderPass(directIlluminationShader, "MyRaytraceShaderPass");
            cmd.DispatchRays(directIlluminationShader, 
                "ShadowDirectIllumination", 
                (uint)CurrentGBuffer.Albedo.width,
                (uint)CurrentGBuffer.Albedo.height, 
                1u,
                m_camera);
                
            var indirectIlluminationShader = m_renderSettings.PipelineResourceSetting.IndirectRayTracingShader;
            cmd.SetRayTracingTextureParam(indirectIlluminationShader, "_renderTarget", CurrentColorTarget);
            cmd.SetRayTracingAccelerationStructure(indirectIlluminationShader, 
                "_RaytracingAccelerationStructure", m_rayTracingAccelerationStructure);
            cmd.SetRayTracingShaderPass(indirectIlluminationShader, "MyRaytraceShaderPass");
            cmd.DispatchRays(indirectIlluminationShader, 
                "MyRaygenShader", 
                (uint)CurrentGBuffer.Albedo.width,
                (uint)CurrentGBuffer.Albedo.height, 
                1u,
                m_camera);
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
                InitializeBuffers(command);
                UpdateAccelStructure(command);
                m_firstFrame = false;
            }

            if (Time.frameCount % 1000 == 0)
            {
                var command = new CommandBuffer()
                {
                    name = "Accel"
                };
                UpdateAccelStructure(command);
            }

            var cmd = new CommandBuffer()
            {
                name = "Do ray tracing"
            };
            
            var cmdFiltering = new CommandBuffer()
            {
                name = "Do filter"
            };
            
            m_context.SetupCameraProperties(m_camera);
            using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Generate Rays")))
            {
                cmd.ClearRenderTarget(true, true, Color.clear);

                PathTracing(cmd);
            }
            ExecuteCommand(cmd);
            
            using (var scope = new ProfilingScope(cmdFiltering, new ProfilingSampler("Filters")))
            {
                Filtering(cmdFiltering);
            }
            Material miscShader = new Material(Shader.Find("Utils/Misc"));
            cmdFiltering.Blit(m_renderTexture, BuiltinRenderTextureType.CameraTarget, miscShader, 1);
            ExecuteCommand(cmdFiltering);

        }

        private void InitializeBuffers(CommandBuffer cmd)
        {
            var filterShader = m_renderSettings.PipelineResourceSetting.SVGFFilterShader;
            cmd.SetComputeBufferParam(filterShader, 3, "_temporalBufferW", CurrentTemporalBuffer);
            cmd.DispatchCompute(filterShader, 3,
                DivCeil(m_renderTexture.width, 16),
                DivCeil(m_renderTexture.height, 16),
                1);
            cmd.SetComputeBufferParam(filterShader, 3, "_temporalBufferW", PrevTemporalBuffer);
            cmd.DispatchCompute(filterShader, 3,
                DivCeil(m_renderTexture.width, 16),
                DivCeil(m_renderTexture.height, 16),
                1);
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
            cmd.SetGlobalVector("_invScreenSize", 
                new Vector4(1.0f / CurrentColorTarget.width, 1.0f / CurrentColorTarget.height,  
                    CurrentColorTarget.width,  CurrentColorTarget.height));
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

                GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
                int globalObjectID = 0;
                foreach (var obj in allObjects)
                {
                    var renderer = obj.GetComponent<MeshRenderer>();
                    if (obj.activeInHierarchy && renderer != null)
                    {
                        cmd.SetGlobalInt("_ObjectId", globalObjectID++);
                        // cmd.DrawMesh(mesh, obj.transform.localToWorldMatrix, renderer.sharedMaterial, renderer.subMeshStartIndex, 0);
                        cmd.DrawRenderer(renderer, renderer.sharedMaterial, renderer.subMeshStartIndex, 0);
                    }
                }

                ExecuteCommand(cmd);
            }
            // using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Swap buffers")))
            // {
            //
            // // m_context.DrawRenderers(m_cullResults, ref drawingSettings, ref filteringSettings);
            //
            //
            //     cmd.Blit(CurrentGBuffer.DepthBuffer, PrevGBuffer.DepthBuffer);
            //     cmd.Blit(CurrentGBuffer.GBuffers[0], PrevGBuffer.GBuffers[0]);
            //     cmd.Blit(CurrentGBuffer.GBuffers[1], PrevGBuffer.GBuffers[1]);
            //     cmd.Blit(CurrentGBuffer.GBuffers[2], PrevGBuffer.GBuffers[2]);
            //     cmd.Blit(CurrentGBuffer.GBuffers[3], PrevGBuffer.GBuffers[3]);
            // }

            // 设置 gbuffer 为全局纹理
            cmd.SetGlobalTexture("_gdepth", CurrentGBuffer.DepthBuffer);
            cmd.SetGlobalTexture("_albedoR", CurrentGBuffer.GBuffers[0]);
            cmd.SetGlobalTexture("_normalM", CurrentGBuffer.GBuffers[1]);
            cmd.SetGlobalTexture("_motionVector", CurrentGBuffer.GBuffers[2]);
            cmd.SetGlobalTexture("_worldPos", CurrentGBuffer.GBuffers[3]);
            
            cmd.SetGlobalTexture("_gdepth_prev", PrevGBuffer.DepthBuffer);
            cmd.SetGlobalTexture("_albedoR_prev", PrevGBuffer.GBuffers[0]);
            cmd.SetGlobalTexture("_normalM_prev", PrevGBuffer.GBuffers[1]);
            cmd.SetGlobalTexture("_motionVector_prev", PrevGBuffer.GBuffers[2]);
            cmd.SetGlobalTexture("_worldPos_prev", PrevGBuffer.GBuffers[3]);
            
            cmd.SetGlobalTexture("_CubeMap", m_renderSettings.LightingSetting.EnvironmentMap);
            ExecuteCommand(cmd);

        }

        public void RenderGeometry(Camera camera, ScriptableRenderContext context, RenderingSettings renderingSettings)
        {
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

            m_gbufferPointer = 1 - m_gbufferPointer;
            SetInitialParameters();
            GenerateGBuffer();
            DoRayTracingRender();
            m_context.DrawSkybox(m_camera);
            DoPostProcessing();
            FinalPass();
        }

        public void Dispose()
        {
            m_rayTracingAccelerationStructure?.Dispose();
            for (int i = 0; i < 2; i++)
            {
                m_temporalBuffers[0]?.Dispose();
            }
        }
    }
}
