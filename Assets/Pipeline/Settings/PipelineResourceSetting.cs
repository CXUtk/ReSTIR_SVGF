using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Assets.Pipeline.Settings
{
    [System.Serializable]
    public class PipelineResourceSetting
    {
        public ComputeShader RayGenerationShader;
        public RayTracingShader DirectRayTracingShader;
        public RayTracingShader IndirectRayTracingShader;
    }
}