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
        private int m_frameCounter;
        private bool m_firstFrame;
        private Matrix4x4 m_vpMatrixPrev;
        
        private CullingResults m_cullResults;
        private RayTracingAccelerationStructure m_rayTracingAccelerationStructure;

        private SVGFPackage m_svgfDirect;
        private SVGFPackage m_svgfIndirect;

        private RenderTexture m_renderTexture;
        private RenderTexture m_swapTexture;
        // private RenderTexture m_temporalMeanTextureW, m_temporalMean2TextureW;
        private RenderTexture[] m_varianceTexture;
        // private RenderTexture[] m_colorRenderTargets;
        // private ComputeBuffer[] m_temporalBuffers;
        // private ComputeBuffer[] m_restirBuffers;
        //
        // private RenderTexture m_directReceiver;
        // private RenderTexture m_indirectReceiver;
        
        
        private GBufferPackage CurrentGBuffer => m_gBuffer[m_gbufferPointer];
        private GBufferPackage PrevGBuffer => m_gBuffer[1 - m_gbufferPointer];
        
        // private ComputeBuffer CurrentTemporalBuffer => m_temporalBuffers[m_gbufferPointer];
        // private ComputeBuffer PrevTemporalBuffer => m_temporalBuffers[1 - m_gbufferPointer];
        //
        // private ComputeBuffer CurrentReSTIRBuffer => m_restirBuffers[m_gbufferPointer];
        // private ComputeBuffer PrevReSTIRBuffer => m_restirBuffers[1 - m_gbufferPointer];
        // private RenderTexture CurrentColorTarget => m_colorRenderTargets[0];
        // private RenderTexture PrevColorTarget => m_colorRenderTargets[1];

        public RealtimeRayTracingPath()
        {
            m_camera = null;
            m_gbufferPointer = 0;
            m_frameCounter = 0;

            m_gBuffer = new GBufferPackage[2];
            for (int i = 0; i < 2; i++)
            {
                m_gBuffer[i] = new GBufferPackage();
            }

            m_svgfIndirect = new SVGFPackage();
            m_svgfDirect = new SVGFPackage();

            m_vpMatrixPrev = new Matrix4x4();

            m_renderTexture = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true
            };
            m_renderTexture.Create();
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

            // m_temporalBuffers = new ComputeBuffer[2];
            // for (int i = 0; i < 2; i++)
            // {
            //     m_temporalBuffers[i] = new ComputeBuffer(Screen.width * Screen.height, sizeof(float) * 13,
            //         ComputeBufferType.Structured);
            // }
            //
            //
            //
            //
            // m_directReceiver = new RenderTexture(Screen.width, Screen.height, 0,
            //     RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            // {
            //     enableRandomWrite = true
            // };
            // m_directReceiver.Create();
            //
            // m_indirectReceiver = new RenderTexture(Screen.width, Screen.height, 0,
            //     RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            // {
            //     enableRandomWrite = true
            // };
            // m_indirectReceiver.Create();
            //
            // // m_temporalMeanTextureR = new RenderTexture(Screen.width, Screen.height, 0,
            // //     RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            // // m_temporalMean2TextureR = new RenderTexture(Screen.width, Screen.height, 0,
            // //     RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            // //
            // // // temporal variance read/write buffer
            // // m_temporalMeanTextureW = new RenderTexture(Screen.width, Screen.height, 0,
            // //     RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear)
            // // {
            // //     enableRandomWrite = true
            // // };
            // // m_temporalMeanTextureW.Create();
            // // m_temporalMean2TextureW = new RenderTexture(Screen.width, Screen.height, 0,
            // //     RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear)
            // // {
            // //     enableRandomWrite = true
            // // };
            // // m_temporalMean2TextureW.Create();
            //
            // m_colorRenderTargets = new RenderTexture[2];
            // for (int i = 0; i < 2; i++)
            // {
            //     m_colorRenderTargets[i] = new RenderTexture(Screen.width, Screen.height, 0,
            //         RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            //     {
            //         enableRandomWrite = true
            //     };
            //     m_colorRenderTargets[i].Create();
            // }


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
            MeshRenderer[] allObjects = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            var defaultShader = Shader.Find("Standard");
            var replaceShader = Shader.Find("Custom Deferred/Default");
            int count = 0;
            foreach (var renderer in allObjects)
            {
                int numMaterials = renderer.sharedMaterials.Length;
                for (int i = 0; i < numMaterials; i++)
                {
                    var material = renderer.sharedMaterials[i];
                    if (material.shader.name.Equals("Standard"))
                    {
                        var color = material.color;
                        var mat2 = new Material(replaceShader)
                        {
                            name = "M" + material.name
                        };
                        renderer.sharedMaterials[i].shader = replaceShader;
                        renderer.sharedMaterials[i].SetTexture("_Albedo", material.mainTexture);
                        renderer.sharedMaterials[i].SetColor("_TintColor", color);
                    
                        //1.0f - material.GetFloat("_Glossiness")
                        renderer.sharedMaterials[i].SetFloat("_Roughness", 1 - material.GetFloat("_Glossiness"));
                        renderer.sharedMaterials[i].SetFloat("_Metallic", material.GetFloat("_Metallic"));
                    }
                }
                
            }
            Debug.Log($"number of invalid: {count}");
        }

        private void Filtering_PrepareData(CommandBuffer cmd, SVGFPackage inputTexture)
        {
            var filterShader = m_renderSettings.PipelineResourceSetting.SVGFFilterShader;
            cmd.SetComputeBufferParam(filterShader, 2, "_temporalBufferR", inputTexture.getPrevTemporalBuffer(m_gbufferPointer));
            cmd.SetComputeBufferParam(filterShader, 2, "_temporalBufferW", inputTexture.getCurrentTemporalBuffer(m_gbufferPointer));
            cmd.SetComputeIntParam(filterShader, "_screenWidth", m_renderTexture.width);
            cmd.SetComputeIntParam(filterShader, "_screenHeight", m_renderTexture.height);
            
            cmd.SetGlobalTexture("_prevColorTarget", inputTexture.getPrevTexture(m_gbufferPointer));
            cmd.SetGlobalTexture("_curColorTarget", inputTexture.getCurrentTexture(m_gbufferPointer));
            cmd.SetGlobalFloat("_temporalFactor", m_renderSettings.DenoisingSettings.TemporalFactor);
            cmd.SetGlobalInt("_useReSTIR",  m_renderSettings.DenoisingSettings.EnableReSTIR ? 1 : 0);
        }

        private int DivCeil(int n, int d)
        {
            return (n + d - 1) / d;
        }

        private void Reconstruction(CommandBuffer cmd)
        {
            Material miscShader = new Material(Shader.Find("Utils/Misc"));
            Material svgfShader = new Material(Shader.Find("RayTracing/SVGF"));

            if (m_renderSettings.DenoisingSettings.SeperateIndirect)
            {
                Filtering(cmd, m_svgfDirect, "Direct");
                Filtering(cmd, m_svgfIndirect, "Indirect");
                // Final apply albedo
                cmd.SetGlobalTexture("_MainTex2", m_svgfIndirect.getCurrentTexture(m_gbufferPointer));
                cmd.Blit(m_svgfDirect.getCurrentTexture(m_gbufferPointer), m_renderTexture, svgfShader, 5);
            }
            else
            {
                Filtering(cmd, m_svgfDirect, "All");
                // Final apply albedo
                cmd.Blit(m_svgfDirect.getCurrentTexture(m_gbufferPointer), m_renderTexture, svgfShader, 2);
            }
        }

        private void Filtering(CommandBuffer cmd, SVGFPackage inputTexture, string name)
        {
            using (var s = new ProfilingScope(cmd, new ProfilingSampler($"Filtering {name}")))
            {
                Filtering_PrepareData(cmd, inputTexture);

                Material miscShader = new Material(Shader.Find("Utils/Misc"));
                Material svgfShader = new Material(Shader.Find("RayTracing/SVGF"));
                var filterShader = m_renderSettings.PipelineResourceSetting.SVGFFilterShader;
                
                // cmd.Blit(m_svgfIndirect.getCurrentTexture(m_gbufferPointer), m_renderTexture);
                // return;

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
                        cmd.SetComputeBufferParam(filterShader, 1, "_temporalBufferR",
                            inputTexture.getCurrentTemporalBuffer(m_gbufferPointer));
                        cmd.SetComputeTextureParam(filterShader, 1, "_renderW", m_varianceTexture[0]);
                        cmd.DispatchCompute(filterShader, 1,
                            DivCeil(m_renderTexture.width, 16),
                            DivCeil(m_renderTexture.height, 16),
                            1);
                        // cmd.SetGlobalBuffer("_temporalBufferR", CurrentTemporalBuffer);
                        // cmd.Blit(CurrentColorTarget, m_varianceTexture[0], svgfShader, 3);
                        // cmd.Blit(m_varianceTexture[0], m_renderTexture);
                        // return;
                    }

                    if (m_renderSettings.DenoisingSettings.EnableReSTIR && name != "Direct")
                    {
                        using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Generate ReSTIR Frame")))
                        {
                            cmd.SetGlobalBuffer("_restirBuffer", inputTexture.getCurrentReSTIRBuffer(m_gbufferPointer));
                            // cmd.Blit(CurrentColorTarget, m_swapTexture);
                            cmd.Blit(m_swapTexture, inputTexture.getCurrentTexture(m_gbufferPointer), svgfShader, 4);
                            // cmd.Blit(m_swapTexture, m_renderTexture, svgfShader, 4);
                            // return;
                        }
                    }

                    using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Wavelet Transform Level 1")))
                    {
                        cmd.SetComputeTextureParam(filterShader, 0, "_varianceDataR", m_varianceTexture[0]);
                        cmd.SetComputeTextureParam(filterShader, 0, "_renderR",
                            inputTexture.getCurrentTexture(m_gbufferPointer));
                        cmd.SetComputeTextureParam(filterShader, 0, "_varianceDataW", m_varianceTexture[1]);
                        cmd.SetComputeTextureParam(filterShader, 0, "_renderW", m_swapTexture);

                        // Wavelet transform, Level 0
                        cmd.SetComputeIntParam(filterShader, "_filterLevel", 0);
                        cmd.DispatchCompute(filterShader, 0,
                            DivCeil(m_renderTexture.width, 16),
                            DivCeil(m_renderTexture.height, 16),
                            1);
                        cmd.Blit(m_swapTexture, inputTexture.getCurrentTexture(m_gbufferPointer));
                    }
                }

                using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Apply Temporal Filtering on Level 1")))
                {
                    cmd.SetGlobalTexture("_varianceTarget", m_varianceTexture[1]);
                    // Apply Temporal Filtering
                    cmd.SetComputeTextureParam(filterShader, 4, "_renderW", m_renderTexture);
                    cmd.DispatchCompute(filterShader, 4,
                        DivCeil(m_renderTexture.width, 16),
                        DivCeil(m_renderTexture.height, 16),
                        1);
                    // cmd.Blit(CurrentColorTarget, m_renderTexture, svgfShader, 0);
                    // cmd.Blit(CurrentColorTarget, m_renderTexture);

                    // Copy color with sample count
                    cmd.Blit(m_renderTexture, inputTexture.getPrevTexture(m_gbufferPointer));
                }

                using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Get Current Color")))
                {
                    cmd.Blit(m_renderTexture, inputTexture.getCurrentTexture(m_gbufferPointer), miscShader, 2);
                }

                if (!m_renderSettings.DenoisingSettings.GroundTruth)
                {
                    using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Wavelet Transform Level 2+")))
                    {
                        // Wavelet transform, Level 1+
                        for (int i = 0; i < 2; i++)
                        {
                            cmd.SetComputeTextureParam(filterShader, 0, "_varianceDataR", m_varianceTexture[1]);
                            cmd.SetComputeTextureParam(filterShader, 0, "_renderR",
                                inputTexture.getCurrentTexture(m_gbufferPointer));
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
                            cmd.SetComputeTextureParam(filterShader, 0, "_renderW",
                                inputTexture.getCurrentTexture(m_gbufferPointer));
                            cmd.DispatchCompute(filterShader, 0,
                                DivCeil(m_renderTexture.width, 8),
                                DivCeil(m_renderTexture.height, 8),
                                1);
                            // cmd.Blit(m_swapTexture, CurrentColorTarget, svgfShader, 1);
                        }
                    }
                    // Final apply albedo
                    //cmd.Blit(inputTexture, m_renderTexture, svgfShader, 2);
                }
            }
        }

        private void PathTracing(CommandBuffer cmd)
        {
            // cmd.SetRenderTarget(CurrentColorTarget);
            // cmd.ClearRenderTarget(true, true, Color.clear);
            //
            // Material gBufferPassShader = new Material(Shader.Find("Custom Deferred/Default"));
            // cmd.Blit(m_gBuffer[0].DepthBuffer, m_renderTexture, gBufferPassShader, 1);

            var directIlluminationShader = m_renderSettings.PipelineResourceSetting.DirectRayTracingShader;
            var pathTracingShader = m_renderSettings.PipelineResourceSetting.PathTracingShader;
            var indirectIlluminationShader = m_renderSettings.PipelineResourceSetting.IndirectRayTracingShader;
            
            // // Direct
            // cmd.SetRayTracingTextureParam(directIlluminationShader, "_renderTarget", CurrentColorTarget);
            // cmd.SetRayTracingAccelerationStructure(directIlluminationShader, 
            //     "_RaytracingAccelerationStructure", m_rayTracingAccelerationStructure);
            // cmd.SetRayTracingShaderPass(directIlluminationShader, "MyRaytraceShaderPass");
            // cmd.DispatchRays(directIlluminationShader, 
            //     "ShadowDirectIllumination", 
            //     (uint)CurrentGBuffer.Albedo.width,
            //     (uint)CurrentGBuffer.Albedo.height, 
            //     1u,
            //     m_camera);
            //     
            // // Indirect
            // cmd.SetRayTracingTextureParam(indirectIlluminationShader, "_renderTarget", CurrentColorTarget);
            // cmd.SetRayTracingBufferParam(indirectIlluminationShader, "_restirTemporalBuffer", m_restirBuffers[0]);
            // cmd.SetRayTracingAccelerationStructure(indirectIlluminationShader, 
            //     "_RaytracingAccelerationStructure", m_rayTracingAccelerationStructure);
            // cmd.SetRayTracingShaderPass(indirectIlluminationShader, "MyRaytraceShaderPass");
            // cmd.DispatchRays(indirectIlluminationShader, 
            //      m_renderSettings.DenoisingSettings.EnableReSTIR ? "MyRaygenShader_ReSTIR" : "MyRaygenShader", 
            //     (uint)CurrentGBuffer.Albedo.width,
            //     (uint)CurrentGBuffer.Albedo.height, 
            //     1u,
            //     m_camera);

            if (m_renderSettings.DenoisingSettings.SeperateIndirect)
            {
                cmd.SetGlobalInt("_PathBounces", m_renderSettings.LightingSetting.PathBounces);
                cmd.SetRayTracingTextureParam(pathTracingShader, "_directTarget", m_svgfDirect.getCurrentTexture(m_gbufferPointer));
                cmd.SetRayTracingTextureParam(pathTracingShader, "_indirectTarget", m_svgfIndirect.getCurrentTexture(m_gbufferPointer));
                cmd.SetRayTracingBufferParam(pathTracingShader, "_curRestirBuffer", m_svgfIndirect.getCurrentReSTIRBuffer(m_gbufferPointer));
                cmd.SetRayTracingBufferParam(pathTracingShader, "_prevRestirBuffer", m_svgfIndirect.getPrevReSTIRBuffer(m_gbufferPointer));
                cmd.SetRayTracingAccelerationStructure(pathTracingShader, 
                    "_RaytracingAccelerationStructure", m_rayTracingAccelerationStructure);
                cmd.SetRayTracingShaderPass(pathTracingShader, "MyPathtracingShaderPass");
                cmd.DispatchRays(pathTracingShader, 
                    m_renderSettings.DenoisingSettings.EnableReSTIR ? "SeperatePathTracing_ReSTIR" : "SeperatePathTracing", 
                    (uint)CurrentGBuffer.Albedo.width,
                    (uint)CurrentGBuffer.Albedo.height, 
                    1u,
                    m_camera);
            }
            else
            {
                cmd.SetGlobalInt("_PathBounces", m_renderSettings.LightingSetting.PathBounces);
                cmd.SetRayTracingTextureParam(pathTracingShader, "_renderTarget", m_svgfDirect.getCurrentTexture(m_gbufferPointer));
                cmd.SetRayTracingBufferParam(pathTracingShader, "_restirTemporalBuffer", m_svgfDirect.getCurrentReSTIRBuffer(m_gbufferPointer));
                cmd.SetRayTracingAccelerationStructure(pathTracingShader, 
                    "_RaytracingAccelerationStructure", m_rayTracingAccelerationStructure);
                cmd.SetRayTracingShaderPass(pathTracingShader, "MyPathtracingShaderPass");
                cmd.DispatchRays(pathTracingShader, 
                    "MyRaygenShader", 
                    (uint)CurrentGBuffer.Albedo.width,
                    (uint)CurrentGBuffer.Albedo.height, 
                    1u,
                    m_camera);
            }

            var filterShader = m_renderSettings.PipelineResourceSetting.SVGFFilterShader;
            Material svgfShader = new Material(Shader.Find("RayTracing/SVGF"));
            
            if (m_renderSettings.DenoisingSettings.EnableReSTIR && m_renderSettings.DenoisingSettings.EnableReSTIRSpatial)
            {
                using (var scope = new ProfilingScope(cmd, new ProfilingSampler("ReSTIR Spatial Filtering")))
                {
                    // cmd.SetComputeBufferParam(filterShader, 8, "_restirBuffer", m_svgfIndirect.getCurrentReSTIRBuffer(m_gbufferPointer));
                    // cmd.SetComputeBufferParam(filterShader, 8, "_restirBufferDest", m_svgfIndirect.getPrevReSTIRBuffer(m_gbufferPointer));
                    // cmd.DispatchCompute(filterShader, 8,
                    //     DivCeil(m_renderTexture.width, 16),
                    //     DivCeil(m_renderTexture.height, 16),
                    //     1);
                    cmd.SetRayTracingBufferParam(pathTracingShader, "_prevRestirBuffer", m_svgfIndirect.getCurrentReSTIRBuffer(m_gbufferPointer));
                    cmd.SetRayTracingBufferParam(pathTracingShader, "_curRestirBuffer", m_svgfIndirect.getPrevReSTIRBuffer(m_gbufferPointer));
                    cmd.SetRayTracingAccelerationStructure(pathTracingShader, 
                        "_RaytracingAccelerationStructure", m_rayTracingAccelerationStructure);
                    cmd.SetRayTracingShaderPass(pathTracingShader, "MyPathtracingShaderPass");
                    cmd.DispatchRays(pathTracingShader, 
                        "ReSTIR_SpatialReuse", 
                        (uint)m_renderTexture.width,
                        (uint)m_renderTexture.height, 
                        1u,
                        m_camera);
                    
                    cmd.SetComputeBufferParam(filterShader, 6, "_restirBuffer", m_svgfIndirect.getPrevReSTIRBuffer(m_gbufferPointer));
                    cmd.SetComputeBufferParam(filterShader, 6, "_restirBufferDest", m_svgfIndirect.getCurrentReSTIRBuffer(m_gbufferPointer));
                    // Copy to current
                    cmd.DispatchCompute(filterShader, 6,
                        DivCeil(m_renderTexture.width, 16),
                        DivCeil(m_renderTexture.height, 16),
                        1);
                }
            }
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
                InitialSetMaterials();
                InitializeBuffers(command);
                UpdateAccelStructure(command);
                ExecuteCommand(command);
                m_firstFrame = false;
            }


            var cmd = new CommandBuffer()
            {
                name = "Do Tracing"
            };
            
            UpdateAccelStructure(cmd);
            ExecuteCommand(cmd);

            var reconstruction = new CommandBuffer()
            {
                name = "Do Reconstruction"
            };

            m_context.SetupCameraProperties(m_camera);
            using (var scope = new ProfilingScope(cmd, new ProfilingSampler("Generate Rays")))
            {
                cmd.ClearRenderTarget(true, true, Color.clear);

                PathTracing(cmd);
            }

            ExecuteCommand(cmd);

            using (var scope = new ProfilingScope(reconstruction, new ProfilingSampler("Reconstruction")))
            {
                Reconstruction(reconstruction);
            }

            Material miscShader = new Material(Shader.Find("Utils/Misc"));
            reconstruction.Blit(m_renderTexture, BuiltinRenderTextureType.CameraTarget, miscShader, 1);
            ExecuteCommand(reconstruction);
        }

        private void InitializeBuffers(CommandBuffer cmd)
        {
            var filterShader = m_renderSettings.PipelineResourceSetting.SVGFFilterShader;
            cmd.SetComputeBufferParam(filterShader, 3, "_temporalBufferW", m_svgfDirect.getCurrentTemporalBuffer(m_gbufferPointer));
            cmd.DispatchCompute(filterShader, 3,
                DivCeil(m_renderTexture.width, 16),
                DivCeil(m_renderTexture.height, 16),
                1);
            cmd.SetComputeBufferParam(filterShader, 3, "_temporalBufferW", m_svgfDirect.getPrevTemporalBuffer(m_gbufferPointer));
            cmd.DispatchCompute(filterShader, 3,
                DivCeil(m_renderTexture.width, 16),
                DivCeil(m_renderTexture.height, 16),
                1);
            
            cmd.SetComputeBufferParam(filterShader, 5, "_restirBufferDest",
                m_svgfDirect.getCurrentReSTIRBuffer(m_gbufferPointer));
            cmd.DispatchCompute(filterShader, 5,
                DivCeil(m_renderTexture.width, 16),
                DivCeil(m_renderTexture.height, 16),
                1);

            if (m_svgfIndirect != null)
            {
                cmd.SetComputeBufferParam(filterShader, 3, "_temporalBufferW",
                    m_svgfIndirect.getCurrentTemporalBuffer(m_gbufferPointer));
                cmd.DispatchCompute(filterShader, 3,
                    DivCeil(m_renderTexture.width, 16),
                    DivCeil(m_renderTexture.height, 16),
                    1);
                cmd.SetComputeBufferParam(filterShader, 3, "_temporalBufferW",
                    m_svgfIndirect.getPrevTemporalBuffer(m_gbufferPointer));
                cmd.DispatchCompute(filterShader, 3,
                    DivCeil(m_renderTexture.width, 16),
                    DivCeil(m_renderTexture.height, 16),
                    1);
                
                cmd.SetComputeBufferParam(filterShader, 5, "_restirBufferDest",
                    m_svgfIndirect.getCurrentReSTIRBuffer(m_gbufferPointer));
                cmd.DispatchCompute(filterShader, 5,
                    DivCeil(m_renderTexture.width, 16),
                    DivCeil(m_renderTexture.height, 16),
                    1);
                
                cmd.SetComputeBufferParam(filterShader, 5, "_restirBufferDest",
                    m_svgfIndirect.getPrevReSTIRBuffer(m_gbufferPointer));
                cmd.DispatchCompute(filterShader, 5,
                    DivCeil(m_renderTexture.width, 16),
                    DivCeil(m_renderTexture.height, 16),
                    1);
            }
        }

        private void DoPostProcessing()
        {
            
        }

        private void FinalPass()
        {
            //深度值，渲染到纹理。Y要翻转
            Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(m_camera.projectionMatrix, true);
            m_vpMatrixPrev = projectionMatrix * m_camera.worldToCameraMatrix;
            m_frameCounter++;
        }

        private void SetInitialParameters()
        {
            var cmd = new CommandBuffer()
            {
                name = "Init params"
            };

            cmd.SetGlobalInt("_screenWidth", Screen.width);
            cmd.SetGlobalInt("_screenHeight", Screen.height);
            cmd.SetGlobalInt("_uGlobalFrames", m_frameCounter);
            cmd.SetGlobalMatrix("_vpMatrixPrev", m_vpMatrixPrev);
            cmd.SetGlobalVector("_invScreenSize", 
                new Vector4(1.0f / m_renderTexture.width, 1.0f / m_renderTexture.height,  
                    m_renderTexture.width,  m_renderTexture.height));
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

                MeshRenderer[] allObjects = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                int globalObjectID = 0;
                foreach (var renderer in allObjects)
                {
                   var mesh = renderer.GetComponentInParent<MeshFilter>();
                   // cmd.DrawMesh(mesh.mesh, renderer.transform.localToWorldMatrix, renderer.sharedMaterial, renderer.subMeshStartIndex, 0);
                   for (int i = 0; i < mesh.sharedMesh.subMeshCount; i++)
                   {
                       var submesh = mesh.sharedMesh.GetSubMesh(i);
                       cmd.SetGlobalInt("_ObjectId", globalObjectID++);
                       cmd.DrawMesh(mesh.sharedMesh, renderer.transform.localToWorldMatrix, renderer.sharedMaterials[i],
                           i, 0);
                   }
                   
                   
                    // var renderer = obj.GetComponent<MeshRenderer>();

                        // cmd.SetGlobalInt("_ObjectId", globalObjectID++);
                        // // cmd.DrawMesh(mesh, obj.transform.localToWorldMatrix, renderer.sharedMaterial, renderer.subMeshStartIndex, 0);
                        // cmd.DrawRenderer(renderer, renderer.sharedMaterial, renderer.subMeshStartIndex, 0);
                        //

                    // foreach (var r in obj.GetComponentsInChildren<MeshRenderer>())
                    // {
                    //     cmd.SetGlobalInt("_ObjectId", globalObjectID++);
                    //     // cmd.DrawMesh(mesh, obj.transform.localToWorldMatrix, renderer.sharedMaterial, renderer.subMeshStartIndex, 0);
                    //     cmd.DrawRenderer(r, r.sharedMaterial, r.subMeshStartIndex, 0);
                    // }
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
            cmd.SetGlobalTexture("_emission", CurrentGBuffer.GBuffers[4]);
            
            cmd.SetGlobalTexture("_gdepth_prev", PrevGBuffer.DepthBuffer);
            cmd.SetGlobalTexture("_albedoR_prev", PrevGBuffer.GBuffers[0]);
            cmd.SetGlobalTexture("_normalM_prev", PrevGBuffer.GBuffers[1]);
            cmd.SetGlobalTexture("_motionVector_prev", PrevGBuffer.GBuffers[2]);
            cmd.SetGlobalTexture("_worldPos_prev", PrevGBuffer.GBuffers[3]);
            cmd.SetGlobalTexture("_emission_prev", PrevGBuffer.GBuffers[4]);
            
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
            m_svgfDirect?.Dispose();
            m_svgfIndirect?.Dispose();
        }
    }
}
