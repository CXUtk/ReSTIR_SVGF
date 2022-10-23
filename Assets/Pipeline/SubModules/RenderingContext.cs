using UnityEngine;
using UnityEngine.Rendering;

namespace Assets.Pipeline.SubModules
{
    public struct RenderingContext
    {
        public Camera Camera;
        public ScriptableRenderContext SRPContext;
    }
}