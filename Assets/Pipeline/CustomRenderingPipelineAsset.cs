using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Assets.Pipeline.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Assets.Pipeline
{
    [CreateAssetMenu(menuName = "Rendering/ToyRenderPipeline")]
    internal class CustomRenderingPipelineAsset : RenderPipelineAsset
    {
        [SerializeField]
        internal RenderingSettings RenderingSettings = default;
        
        protected override RenderPipeline CreatePipeline()
        {
            return new CustomRenderingPipeline(RenderingSettings);
        }
    }
}
