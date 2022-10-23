#pragma once
Texture2D   _DirectionalShadowAtlas;
Texture2D   _ShadowmapSquare;
SamplerState _my_point_clamp_sampler;
SamplerState _my_linear_clamp_sampler;
int         _DirectionalShadowAtlasSize;
float       _shadowBias;

float GetLightAttenuation(float3 posSTS)
{
    float s = _DirectionalShadowAtlas.Sample(_my_point_clamp_sampler, posSTS.xy).r - _shadowBias > posSTS.z ? 0 : 1;
    float s1 = _DirectionalShadowAtlas.Sample(_my_point_clamp_sampler, posSTS.xy - float2(1.0 / _DirectionalShadowAtlasSize, 0)).r 
    - _shadowBias > posSTS.z ? 0 : 1;
    float s2 = _DirectionalShadowAtlas.Sample(_my_point_clamp_sampler, posSTS.xy + float2(1.0 / _DirectionalShadowAtlasSize, 0)).r 
    - _shadowBias > posSTS.z ? 0 : 1;
    float s3 = _DirectionalShadowAtlas.Sample(_my_point_clamp_sampler, posSTS.xy - float2(0, 1.0 / _DirectionalShadowAtlasSize)).r 
    - _shadowBias > posSTS.z ? 0 : 1;
    float s4 = _DirectionalShadowAtlas.Sample(_my_point_clamp_sampler, posSTS.xy + float2(0, 1.0 / _DirectionalShadowAtlasSize)).r 
    - _shadowBias > posSTS.z ? 0 : 1;
    return (s + s1 + s2+ s3 + s4) / 5;
}

float GetLightAttenuationVSM(float3 posSTS)
{
    float mu = _DirectionalShadowAtlas.Sample(_my_linear_clamp_sampler, posSTS.xy).r;
    float sigma2 = _ShadowmapSquare.Sample(_my_linear_clamp_sampler, posSTS.xy).r - mu * mu;
    float p = sigma2 / (sigma2 + (posSTS.z - mu) * (posSTS.z - mu));
    p = saturate(p);
    return max(p, mu - _shadowBias <= posSTS.z);
}