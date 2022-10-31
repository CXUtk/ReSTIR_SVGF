using System;
using Assets.Pipeline;
using System.Collections;
using System.Collections.Generic;
using Assets.Pipeline.Settings;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class CustomRenderingPipeline : RenderPipeline
{
    private CameraRenderer m_cameraRenderer;
    private RenderingSettings m_renderingSettings;
    public CustomRenderingPipeline(RenderingSettings renderingSettings)
    {
        this.m_renderingSettings = renderingSettings;
        
        GraphicsSettings.lightsUseLinearIntensity = true;
        m_cameraRenderer = new CameraRenderer(m_renderingSettings);
        Debug.Log($"Support Ray Tracing: {SystemInfo.supportsRayTracing}");
    }

    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        foreach (var camera in cameras)
        {
            m_cameraRenderer.Render(camera, context, m_renderingSettings);
        }
    }
}
