#pragma once
#include "../Library/Common.hlsl"
#include "../Library/Lighting.hlsl"

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

Texture2D _prevColorTarget;
Texture2D _curColorTarget;
Texture2D _varianceTarget;

Texture2D _gdepth;
Texture2D _albedoR;
Texture2D _normalM;
Texture2D _motionVector;
Texture2D _worldPos;

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
    if (WSpos.w != WSpos_prev.w || dot(Nprev, Ncur) < 0.9)
    {
        return cur;
    }
    float3 variance = sqrt(_varianceTarget.SampleLevel(my_point_clamp_sampler, i.uv, 0).xyz);
    
    // prev.xyz = clamp(prev.xyz, cur - variance, cur + variance);
    float3 c = prev.rgb / prev.a;
    float sigma = abs(dot(float3(0.2126, 0.7152, 0.0722), (cur.rgb - c) / variance));
    if(sigma > 2)
    {
        return float4(c + cur, 2);
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

Texture2D _temporalAccumulateMean;
Texture2D _temporalAccumulateMean2;
float4 variance_estimation (v2f V2F) : SV_TARGET
{
    float3 N = _normalM.SampleLevel(my_point_clamp_sampler, V2F.uv, 0).xyz;
    if(length(N) < 1e-5) return float4(0, 0, 0, 0);
    N = normalize(N * 2 - 1);

    // posSelf.w is object Id
    float4 posSelf = _worldPos.SampleLevel(my_point_clamp_sampler, V2F.uv, 0);
    
    float3 mean = 0;
    float3 mean2 = 0;
    float weight = 0;
    for (int i = -3; i <= 3; i++)
    {
        for (int j = -3; j <= 3; j++)
        {
            float2 offset = _invScreenSize.xy * float2(j, i);
            float2 uv = V2F.uv + offset;
            if(uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) continue;
            float3 nq = _normalM.SampleLevel(my_point_clamp_sampler, uv, 0).xyz;
            // If is empty then skip
            if(length(nq) < 1e-5) continue;
            nq = normalize(nq * 2 - 1);
            
            float3 C = _temporalAccumulateMean.SampleLevel(my_point_clamp_sampler, uv, 0).rgb;
            float3 C2 = _temporalAccumulateMean2.SampleLevel(my_point_clamp_sampler, uv, 0).rgb;

            // Xq.w is object Id
            float4 Xq = _worldPos.SampleLevel(my_point_clamp_sampler, uv, 0);
            float3 dir = Xq.xyz - posSelf.xyz;
            if (Length2(dir) > 1e-6)
            {
                dir = normalize(dir);
            }
            
            float wn = pow(max(0, dot(nq, N)), _sigmaN);
            // float wz = exp(-abs(Z - Zq) / (_sigmaZ * abs(dot(gradZ, -offset)) + 1e-5));
            float wz = -abs(dot(N, dir)) / _sigmaZ;
            float wx = -length(Xq.xyz - posSelf.xyz) / _sigmaX;
            float wid = (Xq.w == posSelf.w) ? 1 : 0;
            
            int k = (2 + i) * 5 + (2 + j);
            float w = h[k] * wn * exp(wz + wx) * wid;

            mean += w * C;
            mean2 += w * C2;
            weight += w;
        }
    }
    if (weight < 1e-5)
    {
        return float4(0, 0, 0, 1);
    }
    return float4(abs(mean2 / weight - mean * mean / (weight * weight)), 1);
}

float4 final_gather (v2f i) : SV_TARGET
{
    float3 light = _MainTex.SampleLevel(my_point_clamp_sampler, i.uv, 0).rgb;
    float3 colorSelf = _albedoR.SampleLevel(my_point_clamp_sampler, i.uv, 0).rgb;
    return float4(light * colorSelf, 1);
}