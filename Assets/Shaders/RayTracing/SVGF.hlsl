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

Texture2D _prevColorTarget;
Texture2D _curColorTarget;

Texture2D _gdepth;
Texture2D _albedoR;
Texture2D _normalM;
Texture2D _motionVector;
Texture2D _worldPos;

float4 temporal_filter (v2f i) : SV_TARGET
{
    float2 motion = _motionVector.SampleLevel(my_point_clamp_sampler, i.uv, 0).xy;
    float4 prev = _prevColorTarget.SampleLevel(my_point_clamp_sampler, i.uv + motion, 0);
    float4 cur = _curColorTarget.SampleLevel(my_point_clamp_sampler, i.uv, 0);
    return lerp(prev, cur, 0.05);
}