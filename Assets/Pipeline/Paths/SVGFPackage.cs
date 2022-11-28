using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Assets.Pipeline.Paths
{
    public class SVGFPackage : IDisposable
    {
        public RenderTexture getCurrentTexture(int gid) => m_renderTexture[0];
        public RenderTexture getPrevTexture(int gid) => m_renderTexture[1];
        
        public ComputeBuffer getCurrentTemporalBuffer(int gid) => m_temporalBuffers[gid];
        public ComputeBuffer getPrevTemporalBuffer(int gid) => m_temporalBuffers[1 - gid];
        
        public ComputeBuffer getCurrentReSTIRBuffer(int gid) => m_restirBuffers[gid];
        public ComputeBuffer getPrevReSTIRBuffer(int gid) => m_restirBuffers[1 - gid];

        private RenderTexture[] m_renderTexture;
        private ComputeBuffer[] m_temporalBuffers;
        private ComputeBuffer[] m_restirBuffers;
        private const int MAX_BUFFERS = 2;
        
        public SVGFPackage()
        {
            m_renderTexture = new RenderTexture[MAX_BUFFERS];
            for (int i = 0; i < MAX_BUFFERS; i++)
            {
                m_renderTexture[i] = new RenderTexture(Screen.width, Screen.height, 0,
                    RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                {
                    enableRandomWrite = true
                };
                m_renderTexture[i].Create();
            }
            
            m_temporalBuffers = new ComputeBuffer[MAX_BUFFERS];
            for (int i = 0; i < MAX_BUFFERS; i++)
            {
                m_temporalBuffers[i] = new ComputeBuffer(Screen.width * Screen.height, sizeof(float) * 13,
                    ComputeBufferType.Structured);
            }
            
            m_restirBuffers = new ComputeBuffer[MAX_BUFFERS];
            for (int i = 0; i < MAX_BUFFERS; i++)
            {
                m_restirBuffers[i] = new ComputeBuffer(Screen.width * Screen.height, sizeof(float) * 18,
                    ComputeBufferType.Structured);
            }
        }


        public void Dispose()
        {
            for (int i = 0; i < MAX_BUFFERS; i++)
            {
                m_temporalBuffers[i].Dispose();
                m_restirBuffers[i].Dispose();
            }
        }
    }
}