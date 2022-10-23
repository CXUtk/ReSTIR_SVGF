using Assets.Pipeline;
using System.Collections;
using System.Collections.Generic;
using Assets.Pipeline.Settings;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class CustomRenderingPipeline : RenderPipeline
{
    private ShadowSettings m_shadowSettings;
    private CameraRenderer m_cameraRenderer;
    private LightingSetting m_lightingSetting;
    public CustomRenderingPipeline(ShadowSettings shadowSettings, LightingSetting lightingSetting)
    {
        this.m_shadowSettings = shadowSettings;
        this.m_lightingSetting = lightingSetting;
        
        GraphicsSettings.lightsUseLinearIntensity = true;
        m_cameraRenderer = new CameraRenderer(m_lightingSetting);
    }

    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        foreach (var camera in cameras)
        {
            m_cameraRenderer.Render(camera, context, m_shadowSettings);
        }
    }
}
