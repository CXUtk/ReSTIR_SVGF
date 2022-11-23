#pragma once
#include "../Library/BRDF.hlsl"

struct MyPayload
{
    float4 color;
    float T;
    float3 N;
};

RaytracingAccelerationStructure _RaytracingAccelerationStructure : register(t0);

#define MAX_DIRECTIONAL_LIGHTS 4
int         _DirectionalLightCount;
float3      _DirectionalLightColors[MAX_DIRECTIONAL_LIGHTS];
float3      _DirectionalLightDirections[MAX_DIRECTIONAL_LIGHTS];

#define MAX_AREA_LIGHTS 4
int         _AreaLightCount;
float3      _AreaLightEmission[MAX_AREA_LIGHTS];
float3      _AreaLightVA[MAX_AREA_LIGHTS];
float3      _AreaLightVB[MAX_AREA_LIGHTS];
float3      _AreaLightVC[MAX_AREA_LIGHTS];


float3 Direct_DirectionalLight(in Surface surface, float3 wo)
{
    float3 finalColor = 0;
    for(int i = 0; i < min(_DirectionalLightCount, MAX_DIRECTIONAL_LIGHTS); i++)
    {
        MyPayload shadowPayLoad;
        shadowPayLoad.color = float4(0, 0, 0, 0);
        shadowPayLoad.T = 10000;
        shadowPayLoad.N = 0;
    
        RayDesc shadowRay;
        float3 wLight = _DirectionalLightDirections[i].xyz;
        shadowRay.Origin = surface.worldPos + surface.normal * 1e-3; 
        shadowRay.Direction = wLight;
        shadowRay.TMin = 0;
        shadowRay.TMax = 10000;

        TraceRay(_RaytracingAccelerationStructure, (RAY_FLAG_SKIP_CLOSEST_HIT_SHADER | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH), 
            0xFF, 0, 1, 0, shadowRay, shadowPayLoad);

        float3 BRDFCosTheta = _DirectionalLightColors[i] * BRDFNoL_GGX_NoAlbedo(surface, wLight, wo);

        finalColor += shadowPayLoad.color.a * BRDFCosTheta;
    }
    return finalColor;
}

float3 Direct_AreaLight(in Surface surface, float3 wo, float2 sample)
{
    float u0 = sqrt(sample.x);
    float2 S = float2(1 - u0, sample.y * u0);
    
    float3 finalColor = 0;
    for(int i = 0; i < min(_AreaLightCount, 1); i++)
    {
        MyPayload shadowPayLoad;
        shadowPayLoad.color = float4(0, 0, 0, 0);
        shadowPayLoad.T = 10000;
        shadowPayLoad.N = 0;
    
        RayDesc shadowRay;
        float3 lightPos = (1 - S.x - S.y) * _AreaLightVA[i] + S.x * _AreaLightVB[i] + S.y * _AreaLightVC[i];
        float3 wLight = normalize(lightPos - surface.worldPos);
        shadowRay.Origin = surface.worldPos + surface.normal * 1e-3; 
        shadowRay.Direction = wLight;
        shadowRay.TMin = 0;
        shadowRay.TMax = length(lightPos - surface.worldPos) - 1e-3;

        TraceRay(_RaytracingAccelerationStructure, (RAY_FLAG_SKIP_CLOSEST_HIT_SHADER | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH), 
            0xFF, 0, 1, 0, shadowRay, shadowPayLoad);

        float3 lightNormal = cross(_AreaLightVB[i] - _AreaLightVA[i], _AreaLightVC[i] - _AreaLightVA[i]);
        float areaOfLight = 0.5 * length(lightNormal);
        lightNormal = normalize(lightNormal);
        float theta = max(1e-5, dot(lightNormal, -wLight));
        float pdf = Length2(lightPos - surface.worldPos) / (theta * areaOfLight);

        float3 BRDFCosTheta = _AreaLightEmission[i] * BRDFNoL_GGX_NoAlbedo(surface, wLight, wo) / pdf;

        finalColor += shadowPayLoad.color.a * BRDFCosTheta;
    }
    return finalColor;
}