#pragma once
#include "Common.hlsl"
#include "Shadow.hlsl"

struct Surface
{
    float3 worldPos;
    float3 normal;
    float3 color;
    float alpha;
};

struct Light
{
    float3 dir;
    float3 color;
};


#define MAX_DIRECTIONAL_LIGHTS 4
int         _DirectionalLightCount;
float3      _DirectionalLightColors[MAX_DIRECTIONAL_LIGHTS];
float3      _DirectionalLightDirections[MAX_DIRECTIONAL_LIGHTS];
float4x4    _DirectionalShadowMatrices[MAX_DIRECTIONAL_LIGHTS];

float3 GetLighting (Surface surface, Light light)
{
    return saturate(dot(surface.normal, light.dir)) * light.color * surface.color; 
}

float3 Shading(Surface surface)
{
    float3 radiance = 0.0;
    for(int i = 0; i < _DirectionalLightCount; i++)
    {
        Light light;
        light.dir = _DirectionalLightDirections[i];
        light.color = _DirectionalLightColors[i];

        float4 v = mul(_DirectionalShadowMatrices[i], float4(surface.worldPos, 1));
        radiance += GetLighting(surface, light) * GetLightAttenuationVSM(v.xyz);
    }
    return radiance;
}