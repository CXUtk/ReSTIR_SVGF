using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Assets.Pipeline
{
    public enum TextureSize
    {
        _256 = 256, _512 = 512, _1024 = 1024,
        _2048 = 2048, _4096 = 4096, _8192 = 8192
    }

    [System.Serializable]
    public class ShadowSettings
    {

        [Min(0f)]
        public float maxDistance = 100f;

        [Range(0, 0.1f)]
        public float shadowBias = 0.001f;

        [System.Serializable]
        public struct Directional
        {

            public TextureSize atlasSize;
        }

        public Directional directional = new Directional
        {
            atlasSize = TextureSize._1024
        };
    }
    public class Shadows
    {
        private Camera m_camera;
        private ShadowSettings m_shadowSettings;
        private ScriptableRenderContext m_context;

        private static int m_depthTextureSwapBufferId = Shader.PropertyToID("_ShadowmapSwap");
        private static int ShadowAtlasSquareId = Shader.PropertyToID("_ShadowmapSquare");
        private static int dirShadowAtlasId = Shader.PropertyToID("_DirectionalShadowAtlas"),
            dirShadowAtlasSizeId = Shader.PropertyToID("_DirectionalShadowAtlasSize"),
            shadowBiasId = Shader.PropertyToID("_shadowBias"),
            dirShadowMatricesId = Shader.PropertyToID("_DirectionalShadowMatrices");

        struct ShadowedDirectionalLight
        {
            public int visibleLightIndex;
        }
        private List<ShadowedDirectionalLight> ShadowedDirectionalLights =
                new List<ShadowedDirectionalLight>();
        private List<Matrix4x4> ShadowedDirectionalLightMatrices =
                new List<Matrix4x4>();
        private CullingResults m_cullingResults;

        public Shadows()
        {
        }

        private void ExecuteCommands(CommandBuffer cmd)
        {
            m_context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        private void AddShadowDirectionalLight(int visibleLightIndex)
        {
            if (ShadowedDirectionalLights.Count < CameraRenderer.maxShadowedDirectionalLightCount)
            {
                ShadowedDirectionalLights.Add(new ShadowedDirectionalLight
                {
                    visibleLightIndex = visibleLightIndex
                });
            }
        }

        private void RenderShadow()
        {
            CommandBuffer cmd = new CommandBuffer();
            cmd.name = "Render Shadow";
            
            int atlasSize = (int)m_shadowSettings.directional.atlasSize;
            cmd.SetGlobalInt(dirShadowAtlasSizeId, atlasSize);
            cmd.SetGlobalFloat(shadowBiasId, m_shadowSettings.shadowBias);

            cmd.GetTemporaryRT(dirShadowAtlasId, atlasSize, atlasSize, 32,
                FilterMode.Bilinear, RenderTextureFormat.Depth);

            cmd.SetRenderTarget(
                dirShadowAtlasId,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
            );
            cmd.ClearRenderTarget(true, false, Color.clear);
            ExecuteCommands(cmd);

            for (int i = 0; i < ShadowedDirectionalLights.Count; i++)
            {
                ShadowedDirectionalLight light = ShadowedDirectionalLights[i];
                var shadowSettings =
                    new ShadowDrawingSettings(m_cullingResults, light.visibleLightIndex);
                m_cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                    light.visibleLightIndex, 0, 1, Vector3.zero, atlasSize, 0f,
                    out Matrix4x4 viewMatrix, out Matrix4x4 projectionMatrix,
                    out ShadowSplitData splitData
                );
                shadowSettings.splitData = splitData;
                ShadowedDirectionalLightMatrices.Add(ConvertToAtlasMatrix(projectionMatrix * viewMatrix));

                cmd.SetViewport(new Rect(
                    0, 0,
                    atlasSize, atlasSize
                ));
                cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
                cmd.SetGlobalMatrixArray(dirShadowMatricesId, ShadowedDirectionalLightMatrices);

                ExecuteCommands(cmd);
                m_context.DrawShadows(ref shadowSettings);
            }
            cmd.EndSample(cmd.name);
            ExecuteCommands(cmd);
        }

        private void AddLights()
        {
            ShadowedDirectionalLights.Clear();
            ShadowedDirectionalLightMatrices.Clear();
            NativeArray<VisibleLight> visibleLights = m_cullingResults.visibleLights;
            for (int i = 0; i < visibleLights.Length; i++)
            {
                VisibleLight vLight = visibleLights[i];
                Light light = vLight.light;
                if (vLight.lightType == LightType.Directional && light.shadows != LightShadows.None
                    && light.shadowStrength > 0f &&
                    m_cullingResults.GetShadowCasterBounds(i, out Bounds b)
                )
                {
                    AddShadowDirectionalLight(i);
                }

                if (ShadowedDirectionalLights.Count >= CameraRenderer.maxDirLightCount)
                {
                    break;
                }
            }
        }

        public void Clean()
        {
            CommandBuffer cmd = new CommandBuffer();
            cmd.name = "Clean Shadow Map";
            cmd.BeginSample(cmd.name);
            cmd.ReleaseTemporaryRT(dirShadowAtlasId);
            cmd.ReleaseTemporaryRT(m_depthTextureSwapBufferId);
            cmd.ReleaseTemporaryRT(ShadowAtlasSquareId);
            cmd.EndSample(cmd.name);
            ExecuteCommands(cmd);
        }


        private void ApplyBlurOn(CommandBuffer cmd, int renderTargetId)
        {
            int[] swapBuffers = new int[2] { renderTargetId, m_depthTextureSwapBufferId };

            var blurMat = new Material(Shader.Find("Utils/Blur"));
            for (int i = 0; i < 10; i++)
            {
                int cur = i % 2;
                int next = (i + 1) % 2;
                cmd.SetRenderTarget(swapBuffers[next], RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.ClearRenderTarget(true, true, Color.clear);

                cmd.Blit(swapBuffers[cur], swapBuffers[next], blurMat);
            }
        }

        public void Setup(Camera camera, ScriptableRenderContext context, ShadowSettings shadowSettings, CullingResults cullingResults)
        {
            this.m_camera = camera;
            this.m_context = context;
            this.m_shadowSettings = shadowSettings;
            this.m_cullingResults = cullingResults;

            AddLights();
            RenderShadow();

            CommandBuffer cmd = new CommandBuffer();
            cmd.name = "Process Shadow Map";
            cmd.BeginSample(cmd.name);
            int atlasSize = (int)m_shadowSettings.directional.atlasSize;

            cmd.GetTemporaryRT(ShadowAtlasSquareId, atlasSize, atlasSize, 32,
                FilterMode.Bilinear, RenderTextureFormat.Depth);
            cmd.GetTemporaryRT(m_depthTextureSwapBufferId, atlasSize, atlasSize, 32,
                FilterMode.Bilinear, RenderTextureFormat.Depth);

            cmd.SetRenderTarget(ShadowAtlasSquareId, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.Blit(dirShadowAtlasId, ShadowAtlasSquareId, new Material(Shader.Find("Utils/Square")));

            ApplyBlurOn(cmd, dirShadowAtlasId);
            ApplyBlurOn(cmd, ShadowAtlasSquareId);

            cmd.EndSample(cmd.name);
            ExecuteCommands(cmd);
        }

        Matrix4x4 ConvertToAtlasMatrix(Matrix4x4 m)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                m.m20 = -m.m20;
                m.m21 = -m.m21;
                m.m22 = -m.m22;
                m.m23 = -m.m23;
            }

            m.m00 = 0.5f * (m.m00 + m.m30);
            m.m01 = 0.5f * (m.m01 + m.m31);
            m.m02 = 0.5f * (m.m02 + m.m32);
            m.m03 = 0.5f * (m.m03 + m.m33);
            m.m10 = 0.5f * (m.m10 + m.m30);
            m.m11 = 0.5f * (m.m11 + m.m31);
            m.m12 = 0.5f * (m.m12 + m.m32);
            m.m13 = 0.5f * (m.m13 + m.m33);
            m.m20 = 0.5f * (m.m20 + m.m30);
            m.m21 = 0.5f * (m.m21 + m.m31);
            m.m22 = 0.5f * (m.m22 + m.m32);
            m.m23 = 0.5f * (m.m23 + m.m33);
            return m;
        }
    }
}
