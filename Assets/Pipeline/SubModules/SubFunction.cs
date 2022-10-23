using UnityEngine.Rendering;

namespace Assets.Pipeline.SubModules
{
    public class SubFunction
    {
        protected RenderingContext m_RenderingContext;
        
        public virtual void AfterPostProcessing() { }
        public virtual void BeforeRenderScene() {}
        
        /// <summary>
        /// This will be happened in the very beginning of the pipeline.
        /// Contexts are not set in this stage.
        /// </summary>
        public virtual void OnInitialize() { }
        public virtual void FrameReset() { }

        public void SetRenderingContext(RenderingContext context)
        {
            m_RenderingContext = context;
        }

        public void ExecuteCommandBuffer(CommandBuffer buffer)
        {
            m_RenderingContext.SRPContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }
    }
}