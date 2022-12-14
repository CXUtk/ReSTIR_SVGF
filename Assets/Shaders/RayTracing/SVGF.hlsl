#pragma once
#include "../Library/Common.hlsl"
#include "../Library/Lighting.hlsl"
#include "../Library/BRDF.hlsl"
#include "SVGFStructure.hlsl"

struct appdata
{
    float4 posOS : POSITION;
    float2 uv : TEXCOORD0;
};

struct v2f
{
    float2 uv : TEXCOORD0;
    float4 posOS : SV_POSITION;
};

v2f vert_tex2D (appdata v)
{
    v2f o;
    o.posOS = TransformObjectToHClip(v.posOS);
    o.uv = v.uv;
    return o;
}

SamplerState my_point_clamp_sampler;

Texture2D _MainTex;
Texture2D _MainTex2;

Texture2D _prevColorTarget;
Texture2D _curColorTarget;
Texture2D _varianceTarget;

Texture2D _gdepth;
Texture2D _albedoR;
Texture2D _normalM;
Texture2D _motionVector;
Texture2D _worldPos;
Texture2D _emission;

Texture2D _gdepth_prev;
Texture2D _albedoR_prev;
Texture2D _normalM_prev;
Texture2D _motionVector_prev;
Texture2D _worldPos_prev;


uint _filterLevel;
uint _sigmaN;
float _sigmaZ;
float _sigmaC;
float _sigmaX;
float _temporalFactor;

float4 _invScreenSize;

StructuredBuffer<temporal_data> _temporalBufferR;

static const float h[25] = { 1.0 / 256.0, 1.0 / 64.0, 3.0 / 128.0, 1.0 / 64.0, 1.0 / 256.0,
        1.0 / 64.0, 1.0 / 16.0, 3.0 / 32.0, 1.0 / 16.0, 1.0 / 64.0,
        3.0 / 128.0, 3.0 / 32.0, 9.0 / 64.0, 3.0 / 32.0, 3.0 / 128.0,
        1.0 / 64.0, 1.0 / 16.0, 3.0 / 32.0, 1.0 / 16.0, 1.0 / 64.0,
        1.0 / 256.0, 1.0 / 64.0, 3.0 / 128.0, 1.0 / 64.0, 1.0 / 256.0 };

static const float gaussian[9] = { 1.0 / 16.0, 1.0 / 8.0, 1.0 / 16.0,
        1.0 / 8.0,  1.0 / 4.0, 1.0 / 8.0,
        1.0 / 16.0, 1.0 / 8.0, 1.0 / 16.0 };


float4 temporal_filter (v2f i) : SV_TARGET
{
    float2 motion = _motionVector.SampleLevel(my_point_clamp_sampler, i.uv, 0).xy;
    float2 uv2 = i.uv + motion;
    float4 cur = _curColorTarget.SampleLevel(my_point_clamp_sampler, i.uv, 0);
    float4 WSpos = _worldPos.SampleLevel(my_point_clamp_sampler, i.uv, 0);
    float3 normalN = _normalM.SampleLevel(my_point_clamp_sampler, i.uv, 0);
    if(length(normalN) < 1e-5)
    {
        return cur;
    }
    float3 Ncur = normalize(normalN * 2 - 1);
    if (uv2.x < 0 || uv2.x > 1 || uv2.y < 0 || uv2.y > 1)
    {
        return cur;
    }
    float3 normalN_pre = _normalM_prev.SampleLevel(my_point_clamp_sampler, uv2, 0);
    if(length(normalN_pre) < 1e-5)
    {
        return cur;
    }
    float3 Nprev = normalize(normalN_pre * 2 - 1);
    float4 prev = _prevColorTarget.SampleLevel(my_point_clamp_sampler, uv2, 0);
    float4 WSpos_prev = _worldPos_prev.SampleLevel(my_point_clamp_sampler, uv2, 0);

    float3 c = prev.rgb / prev.a;
    if (WSpos.w != WSpos_prev.w || dot(Nprev, Ncur) < 0.9)
    {
        return float4(c + cur.rgb, 2);
    }
    float3 variance = sqrt(_varianceTarget.SampleLevel(my_point_clamp_sampler, i.uv, 0).xyz);
    
    // prev.xyz = clamp(prev.xyz, cur - variance, cur + variance);

    float sigma = abs(dot(float3(0.2126, 0.7152, 0.0722), (cur.rgb - c) / max(1e-5, variance)));
    if(sigma > 1)
    {
        float t = min(prev.a * exp(-sigma / 10), 10);
        return float4(c * t + cur.rgb, t + 1);
    }
    return float4(prev.rgb + cur.rgb, prev.a + 1);
}

float4 main_filter (v2f V2F) : SV_TARGET
{
    return _MainTex.SampleLevel(my_point_clamp_sampler, V2F.uv, 0);
    // float3 N = _normalM.SampleLevel(my_point_clamp_sampler, V2F.uv, 0).xyz;
    // if(length(N) < 1e-5) return float4(0, 0, 0, 0);
    // N = normalize(N * 2 - 1);
    //
    // // float Z = _gdepth.SampleLevel(my_point_clamp_sampler, V2F.uv, 0).r;
    // // float DzDx = (_gdepth.SampleLevel(my_point_clamp_sampler, V2F.uv + float2(_invScreenSize.x, 0), 0).r - Z) / _invScreenSize.x;
    // // float DzDy = (_gdepth.SampleLevel(my_point_clamp_sampler, V2F.uv + float2(0, _invScreenSize.y), 0).r - Z) / _invScreenSize.y;
    // // float2 gradZ = float2(DzDx, DzDy);
    //
    // float4 colorSelf = _MainTex.SampleLevel(my_point_clamp_sampler, V2F.uv, 0);
    //
    // // posSelf.w is object Id
    // float4 posSelf = _worldPos.SampleLevel(my_point_clamp_sampler, V2F.uv, 0);
    //
    //
    // float variance = 0;
    // float variance_weight = 0;
    // for (int i = -1; i <= 1; i++)
    // {
    //     for (int j = -1; j <= 1; j++)
    //     {
    //         float2 uv = V2F.uv + _invScreenSize.xy * float2(j, i);
    //         if(uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) continue;
    //         float3 v = _varianceTarget.SampleLevel(my_point_clamp_sampler, uv, 0).xyz;
    //         float lv = abs(dot(float3(0.2126, 0.7152, 0.0722), v));
    //         int k = (1 + i) * 3 + (1 + j);
    //         variance += lv;
    //         variance_weight += lv * gaussian[k];
    //     }
    // }
    // float stdev = sqrt(max(0, variance / variance_weight));
    //
    // float weight = 0;
    // float3 colorComponents = 0;
    // float len = (1 << _filterLevel);
    // for (int i = -2; i <= 2; i++)
    // {
    //     for (int j = -2; j <= 2; j++)
    //     {
    //         float2 offset = _invScreenSize.xy * float2(j, i) * len;
    //         float2 uv = V2F.uv + offset;
    //         if(uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) continue;
    //         float3 nq = _normalM.SampleLevel(my_point_clamp_sampler, uv, 0).xyz;
    //         // If is empty then skip
    //         if(length(nq) < 1e-5) continue;
    //         nq = normalize(nq * 2 - 1);
    //         
    //         float3 C = _MainTex.SampleLevel(my_point_clamp_sampler, uv, 0).rgb;
    //
    //         // Xq.w is object Id
    //         float4 Xq = _worldPos.SampleLevel(my_point_clamp_sampler, uv, 0);
    //         float3 dir = Xq.xyz - posSelf.xyz;
    //         if (Length2(dir) > 1e-6)
    //         {
    //             dir = normalize(dir);
    //         }
    //
    //         float Lumin = abs(dot(float3(0.2126, 0.7152, 0.0722), C - colorSelf));
    //         
    //         float wn = pow(max(0, dot(nq, N)), _sigmaN);
    //         // float wz = exp(-abs(Z - Zq) / (_sigmaZ * abs(dot(gradZ, -offset)) + 1e-5));
    //         float wz = -abs(dot(N, dir)) / _sigmaZ;
    //         float wc = -Lumin / (_sigmaC * stdev + 1e-4);
    //         float wx = -length(Xq.xyz - posSelf.xyz) / _sigmaX;
    //         float wid = (Xq.w == posSelf.w) ? 1.0 : 0.0;
    //         
    //         int k = (2 + i) * 5 + (2 + j);
    //         float w = h[k] * wn * exp(wz + wc + wx) * wid;
    //
    //         colorComponents += w * C;
    //         weight += w;
    //     }
    // }
    // if (weight < 1e-5)
    // {
    //     return colorSelf;
    // }
    // return float4(colorComponents / weight, 1);
}

float4 variance_estimation (v2f V2F) : SV_TARGET
{
    return float4(1, 1, 1, 1);
    // int2 imageCoord = round(V2F.uv * _invScreenSize.zw - 0.5);
    // int bufferId = imageCoord.y * (int)_invScreenSize.z + imageCoord.x;
    //
    // float3 N = _normalM.SampleLevel(my_point_clamp_sampler, V2F.uv, 0).xyz;
    // if(length(N) < 1e-5) return float4(0, 0, 0, 0);
    // N = normalize(N * 2 - 1);
    //
    // // posSelf.w is object Id
    // float4 posSelf = _worldPos.SampleLevel(my_point_clamp_sampler, V2F.uv, 0);
    //
    // const temporal_data data = _temporalBufferR[bufferId];
    //
    // // If we have less than 4 samples
    // if(data.count < 4)
    // {
    //     float3 mean = 0;
    //     float3 mean2 = 0;
    //     float weight = 0;
    //     for (int i = -3; i <= 3; i++)
    //     {
    //         for (int j = -3; j <= 3; j++)
    //         {
    //             float2 offset = _invScreenSize.xy * float2(j, i);
    //             float2 uv = V2F.uv + offset;
    //             if(uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) continue;
    //             float3 nq = _normalM.SampleLevel(my_point_clamp_sampler, uv, 0).xyz;
    //             // If is empty then skip
    //             if(length(nq) < 1e-5) continue;
    //             nq = normalize(nq * 2 - 1);
    //
    //             int2 imageCoord2 = round(uv * _invScreenSize.zw);
    //             int bufferId2 = imageCoord2.y * (int)_invScreenSize.z + imageCoord2.x;
    //             
    //             float3 C = _temporalBufferR[bufferId2].mean;
    //             float3 C2 = _temporalBufferR[bufferId2].mean2;
    //     
    //             // Xq.w is object Id
    //             float4 Xq = _worldPos.SampleLevel(my_point_clamp_sampler, uv, 0);
    //             float3 dir = Xq.xyz - posSelf.xyz;
    //             if (Length2(dir) > 1e-5)
    //             {
    //                 dir = normalize(dir);
    //             }
    //             float Lumin = abs(dot(float3(0.2126, 0.7152, 0.0722), C - data.mean));
    //             
    //             float wn = pow(max(0, dot(nq, N)), _sigmaN);
    //             // float wz = exp(-abs(Z - Zq) / (_sigmaZ * abs(dot(gradZ, -offset)) + 1e-5));
    //             float wz = -abs(dot(N, dir)) / _sigmaZ;
    //             float wx = -length(Xq.xyz - posSelf.xyz) / _sigmaX;
    //             float wid = (Xq.w == posSelf.w) ? 1 : 0;
    //             
    //             int k = (2 + i) * 5 + (2 + j);
    //             float w = wn * exp(wz + wx) * wid;
    //     
    //             mean += w * C;
    //             mean2 += w * C2;
    //             weight += w;
    //         }
    //     }
    //     if (weight < 1e-5)
    //     {
    //         return float4(1, 1, 1, 1);
    //     }
    //     float3 mean2W = mean2 / weight;
    //     float3 meanW = mean / weight;
    //     return float4(abs(mean2W - meanW * meanW) * 4 / data.count, 1);
    // }
    //
    // float3 mean = data.mean;
    // float3 mean2 = data.mean2;
    // return float4(abs(mean2 - mean * mean), 1);
}
// float4 variance_estimation (v2f V2F) : SV_TARGET
// {
//     float3 N = _normalM.SampleLevel(my_point_clamp_sampler, V2F.uv, 0).xyz;
//     if(length(N) < 1e-5) return float4(0, 0, 0, 0);
//     N = normalize(N * 2 - 1);
//
//     // posSelf.w is object Id
//     float4 posSelf = _worldPos.SampleLevel(my_point_clamp_sampler, V2F.uv, 0);
//     
//     float3 mean = 0;
//     float3 mean2 = 0;
//     float weight = 0;
//     for (int i = -3; i <= 3; i++)
//     {
//         for (int j = -3; j <= 3; j++)
//         {
//             float2 offset = _invScreenSize.xy * float2(j, i);
//             float2 uv = V2F.uv + offset;
//             if(uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) continue;
//             float3 nq = _normalM.SampleLevel(my_point_clamp_sampler, uv, 0).xyz;
//             // If is empty then skip
//             if(length(nq) < 1e-5) continue;
//             nq = normalize(nq * 2 - 1);
//             
//             float3 C = _temporalAccumulateMean.SampleLevel(my_point_clamp_sampler, uv, 0).rgb;
//             float3 C2 = _temporalAccumulateMean2.SampleLevel(my_point_clamp_sampler, uv, 0).rgb;
//
//             // Xq.w is object Id
//             float4 Xq = _worldPos.SampleLevel(my_point_clamp_sampler, uv, 0);
//             float3 dir = Xq.xyz - posSelf.xyz;
//             if (Length2(dir) > 1e-6)
//             {
//                 dir = normalize(dir);
//             }
//             
//             float wn = pow(max(0, dot(nq, N)), _sigmaN);
//             // float wz = exp(-abs(Z - Zq) / (_sigmaZ * abs(dot(gradZ, -offset)) + 1e-5));
//             float wz = -abs(dot(N, dir)) / _sigmaZ;
//             float wx = -length(Xq.xyz - posSelf.xyz) / _sigmaX;
//             float wid = (Xq.w == posSelf.w) ? 1 : 0;
//             
//             int k = (2 + i) * 5 + (2 + j);
//             float w = h[k] * wn * exp(wz + wx) * wid;
//
//             mean += w * C;
//             mean2 += w * C2;
//             weight += w;
//         }
//     }
//     if (weight < 1e-5)
//     {
//         return float4(0, 0, 0, 1);
//     }
//     return float4(abs(mean2 / weight - mean * mean / (weight * weight)), 1);
// }

float4 final_gather (v2f i) : SV_TARGET
{
    float3 light = _MainTex.SampleLevel(my_point_clamp_sampler, i.uv, 0).rgb;
    float3 colorSelf = _albedoR.SampleLevel(my_point_clamp_sampler, i.uv, 0).rgb;
    float3 emission = _emission.SampleLevel(my_point_clamp_sampler, i.uv, 0).rgb;
    return float4(emission + light * colorSelf, 1);
}

float4 final_gather2 (v2f i) : SV_TARGET
{
    float3 direct = _MainTex.SampleLevel(my_point_clamp_sampler, i.uv, 0).rgb;
    float3 indirect = _MainTex2.SampleLevel(my_point_clamp_sampler, i.uv, 0).rgb;
    float3 colorSelf = _albedoR.SampleLevel(my_point_clamp_sampler, i.uv, 0).rgb;
    float3 emission = _emission.SampleLevel(my_point_clamp_sampler, i.uv, 0).rgb;
    return float4(emission + (direct + indirect) * colorSelf, 1);
}

StructuredBuffer<restir_RESERVOIR> _restirBuffer;
float4 restir_color_check (v2f i) : SV_TARGET
{
    int2 imageCoord = floor(i.uv / _invScreenSize.xy);
    int bufferId = imageCoord.y * (int)_invScreenSize.z + imageCoord.x;
    
    float4 normalN = _normalM.SampleLevel(my_point_clamp_sampler, i.uv, 0);
    if(length(normalN.xyz) < 1e-5) return float4(0, 0, 0, 0);
    float3 N = normalize(normalN.xyz * 2 - 1);
    
    float4 posSelf = _worldPos.SampleLevel(my_point_clamp_sampler, i.uv, 0);
    float3 wo = normalize(_WorldSpaceCameraPos.xyz - posSelf);

    float4 albedoR = _MainTex.SampleLevel(my_point_clamp_sampler, i.uv, 0);
    
    Surface surface;
    surface.worldPos = posSelf.xyz;
    surface.normal = N;
    surface.color = 1;
    surface.alpha = 1;
    surface.roughness = albedoR.a;
    surface.metallic = normalN.a;
    surface.emission = 0;

    const restir_RESERVOIR R = _restirBuffer[bufferId];
    if(R.M == 0)
    {
        return float4(0, 0, 0, 1);
    }
    float3 WI = normalize(R.sample.Xs - R.sample.Xv);
    float3 newColor = BRDFNoL_GGX_NoAlbedo(surface, WI, wo) * R.sample.Lo * R.Wout;
    if (isnan(newColor.x) || isnan(newColor.y) || isnan(newColor.z))
    {
        return float4(1, 0, 1, 1);
    }
    if (isinf(newColor.x) || isinf(newColor.y) || isinf(newColor.z))
    {
        return float4(0, 1, 1, 1);
    }
    return float4(newColor, 1);
}