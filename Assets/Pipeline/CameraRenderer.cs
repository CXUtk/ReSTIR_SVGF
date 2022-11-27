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
using UnityEngine.SceneManagement;
using Scene = UnityEditor.SearchService.Scene;

namespace Assets.Pipeline
{
    internal class CameraRenderer : IDisposable
    {
        private Camera m_camera;
        private RenderingSettings m_renderingSettings;

        private ScriptableRenderContext m_context;
        private CullingResults m_cullingResults;
        public const int maxDirLightCount = 4;
        public const int MAX_AREALIGHT_COUNT = 8;
        public const int maxShadowedDirectionalLightCount = 1;
        private bool m_firstFrame;

        private static int
            dirLightCountId = Shader.PropertyToID("_DirectionalLightCount"),
            dirLightColorsId = Shader.PropertyToID("_DirectionalLightColors"),
            dirLightDirectionsId = Shader.PropertyToID("_DirectionalLightDirections");

        private static Vector4[]
            dirLightColors,
            dirLightDirections;

        private static int dirLightColorsIndex, dirLightDirIndex;


        private static int
            areaLightCountId = Shader.PropertyToID("_AreaLightCount"),
            areaLightEmissionId = Shader.PropertyToID("_AreaLightEmission"),
            areaLightVAId = Shader.PropertyToID("_AreaLightVA"),
            areaLightVBId = Shader.PropertyToID("_AreaLightVB"),
            areaLightVCId = Shader.PropertyToID("_AreaLightVC");
        
        private static Vector4[]
            areaLightEmissions,
            areaLightVA,
            areaLightVB,
            areaLightVC;

        private static int areaLightIndex;

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

            areaLightEmissions = new Vector4[MAX_AREALIGHT_COUNT];
            areaLightVA = new Vector4[MAX_AREALIGHT_COUNT];
            areaLightVB = new Vector4[MAX_AREALIGHT_COUNT];
            areaLightVC = new Vector4[MAX_AREALIGHT_COUNT];

            m_firstFrame = true;
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
            PrepareAreaLights();
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

            m_firstFrame = false;
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

        private void PrepareAreaLights()
        {
            areaLightIndex = 0;
            
            MeshRenderer[] allObjects = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            foreach (var renderer in allObjects)
            {
                if (renderer.sharedMaterial.shader.name.Equals("Custom Deferred/Default"))
                {
                    Color color = renderer.sharedMaterial.GetColor("_Emission");
                    color *= renderer.sharedMaterial.GetFloat("_EmissionIntensity");
                    if (color.r > 0 && color.g > 0 && color.b > 0)
                    {
                        var meshFilter = renderer.GetComponentInParent<MeshFilter>();
                        var mesh = meshFilter.sharedMesh;
                        int[] triangles = mesh.GetTriangles(0);
                        for (int i = 0; i < triangles.Length; i += 3)
                        {
                            Vector3 A = meshFilter.transform.TransformPoint(mesh.vertices[triangles[i]]);
                            Vector3 B = meshFilter.transform.TransformPoint(mesh.vertices[triangles[i + 1]]);
                            Vector3 C = meshFilter.transform.TransformPoint(mesh.vertices[triangles[i + 2]]);

                            AddAreaLight(color, A, B, C);
                            if (areaLightIndex >= MAX_AREALIGHT_COUNT)
                            {
                                break;
                            }
                        }

                        if (areaLightIndex >= MAX_AREALIGHT_COUNT)
                        {
                            break;
                        }
                    }
                }
            }

            m_commandBuffer.SetGlobalInt(areaLightCountId, areaLightIndex);
            m_commandBuffer.SetGlobalVectorArray(areaLightEmissionId, areaLightEmissions);
            m_commandBuffer.SetGlobalVectorArray(areaLightVAId, areaLightVA);
            m_commandBuffer.SetGlobalVectorArray(areaLightVBId, areaLightVB);
            m_commandBuffer.SetGlobalVectorArray(areaLightVCId, areaLightVC);
            ExecuteBuffer();
        }

        private void AddDirectionalLight(in VisibleLight light)
        {
            dirLightColors[dirLightColorsIndex++] = light.finalColor;
            dirLightDirections[dirLightDirIndex++] = -light.localToWorldMatrix.GetColumn(2);
        }
        
        private void AddAreaLight(Color color, Vector3 A, Vector3 B, Vector3 C)
        {
            if (areaLightIndex >= MAX_AREALIGHT_COUNT) return;
            areaLightEmissions[areaLightIndex] = new Vector4(color.r, color.g, color.b, color.a);
            areaLightVA[areaLightIndex] = A;
            areaLightVB[areaLightIndex] = B;
            areaLightVC[areaLightIndex] = C;
            areaLightIndex++;
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
