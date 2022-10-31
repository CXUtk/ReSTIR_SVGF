using UnityEngine;

namespace Assets.Pipeline.Settings
{
    [System.Serializable]
    public class RenderingSettings
    {
        [SerializeField] public ShadowSettings ShadowSettings;
        [SerializeField] public LightingSetting LightingSetting;
        [SerializeField] public PipelineResourceSetting PipelineResourceSetting;
    }
}