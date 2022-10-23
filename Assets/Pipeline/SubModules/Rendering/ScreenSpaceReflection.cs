using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Assets.Pipeline.SubModules.Rendering
{
    public class ScreenSpaceReflection : SubFunction
    {
        public RenderTexture CurrentFrameTarget => m_curFrameTarget;
        public RenderTexture LastFrameTarget => m_lastFrameTarget;
        
        private RenderTexture m_lastFrameTarget;
        // private RenderTexture m_lastFrameDepthTarget;
        private RenderTexture m_curFrameTarget;
        // private RenderTexture m_curFrameDepthTarget;

        private Matrix4x4 m_vpMatrixPrev;
        private Vector4 m_ZBufferParams;
        public override void OnInitialize()
        {
            m_lastFrameTarget = new RenderTexture(Screen.width, Screen.height, 32, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            m_curFrameTarget = new RenderTexture(Screen.width, Screen.height, 32, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            // m_lastFrameDepthTarget = new RenderTexture(Screen.width, Screen.height, 32, RenderTextureFormat.Depth);
            // m_curFrameDepthTarget = new RenderTexture(Screen.width, Screen.height, 32, RenderTextureFormat.Depth);
            m_vpMatrixPrev = Matrix4x4.identity;
            base.OnInitialize();
        }

        public override void BeforeRenderScene()
        {
            CommandBuffer buffer = new CommandBuffer();
            buffer.name = "Switch RT";
            buffer.SetRenderTarget(m_curFrameTarget);
            buffer.ClearRenderTarget(true, true, Color.clear);

            var camera = m_RenderingContext.Camera;
            Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            buffer.SetGlobalMatrix("_vpMatrix", projectionMatrix * camera.worldToCameraMatrix);
            buffer.SetGlobalMatrix("_vpMatrixPrev", m_vpMatrixPrev);
            buffer.SetGlobalMatrix("_viewMatrix", camera.worldToCameraMatrix);
            buffer.SetGlobalMatrix("_inverseViewMatrix", Matrix4x4.Inverse(camera.worldToCameraMatrix));
            buffer.SetGlobalMatrix("_projectionMatrix", projectionMatrix);
            buffer.SetGlobalFloat("_nearPlaneZ", -camera.nearClipPlane);
            
            Vector4 param = new Vector4(1 - camera.farClipPlane / camera.nearClipPlane, 
                camera.farClipPlane / camera.nearClipPlane, 0, 0);
            var farClipPlane = camera.farClipPlane;
            param.z = param.x / farClipPlane;
            param.w = param.y / farClipPlane;
            m_ZBufferParams = param;
            buffer.SetGlobalVector("_ZBufferParams", m_ZBufferParams);
            buffer.SetGlobalFloat("_ScreenBufferSizeX", Screen.width);
            buffer.SetGlobalFloat("_ScreenBufferSizeY", Screen.height);

            // buffer.SetGlobalTexture("_ScreenTextureDepth", m_lastFrameDepthTarget);
            ExecuteCommandBuffer(buffer);
        }

        public override void AfterPostProcessing()
        {
            CommandBuffer buffer = new CommandBuffer();
            buffer.name = "Switch RT";
            buffer.Blit(m_curFrameTarget, m_lastFrameTarget);
            // Material depthCopy = new Material(Shader.Find("Utils/Misc"));
            // buffer.Blit(m_curFrameDepthTarget, m_lastFrameDepthTarget, depthCopy, 0);
            buffer.Blit(m_curFrameTarget, BuiltinRenderTextureType.CameraTarget);
            ExecuteCommandBuffer(buffer);
            
            //深度值，渲染到纹理。Y要翻转
            Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(m_RenderingContext.Camera.projectionMatrix, true);
            m_vpMatrixPrev = projectionMatrix * m_RenderingContext.Camera.worldToCameraMatrix;
        }
    }
}