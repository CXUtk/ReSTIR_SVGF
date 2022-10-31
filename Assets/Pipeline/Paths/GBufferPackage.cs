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
        
        private RenderTexture m_gDepth;
        private RenderTexture[] m_gBuffers = new RenderTexture[4];
        private RenderTargetIdentifier[] m_gBufferIds = new RenderTargetIdentifier[4];

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

            // 给纹理 ID 赋值
            for (int i = 0; i < 4; i++)
            {
                m_gBufferIds[i] = m_gBuffers[i];
            }
        }
    }
}