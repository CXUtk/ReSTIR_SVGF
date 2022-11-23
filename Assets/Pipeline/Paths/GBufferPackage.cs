using UnityEngine;
using UnityEngine.Rendering;

namespace Assets.Pipeline.Paths
{
    public class GBufferPackage
    {
        public RenderTexture DepthBuffer => m_gDepth;
        public RenderTexture[] GBuffers => m_gBuffers;
        public RenderTargetIdentifier[] GBufferIds => m_gBufferIds;

        public RenderTexture Albedo => m_gBuffers[0];

        private const int GBUFFER_COUNT = 5;
        
        private RenderTexture m_gDepth;
        private RenderTexture[] m_gBuffers = new RenderTexture[GBUFFER_COUNT];
        private RenderTargetIdentifier[] m_gBufferIds = new RenderTargetIdentifier[GBUFFER_COUNT];

        public GBufferPackage()
        {
            // 创建纹理
            m_gDepth = new RenderTexture(Screen.width, Screen.height, 32,
                RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);
            // Albedo (RGB) Roughness (A)
            m_gBuffers[0] = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            // Normal (RGB) Metallic (A)
            m_gBuffers[1] = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.ARGB2101010, RenderTextureReadWrite.Linear);
            // Motion Vector (RG)
            m_gBuffers[2] = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear);
            // World Position (RGB)
            m_gBuffers[3] = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            // Emission
            m_gBuffers[4] = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);

            // 给纹理 ID 赋值
            for (int i = 0; i < GBUFFER_COUNT; i++)
            {
                m_gBufferIds[i] = m_gBuffers[i];
            }
        }
    }
}