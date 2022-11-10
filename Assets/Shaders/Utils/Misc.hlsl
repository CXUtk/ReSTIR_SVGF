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
			
sampler2D _MainTex;
SamplerState my_point_clamp_sampler;

Texture2D _gdepth;

float frag_depth_copy (v2f i) : SV_Depth
{
    return tex2D(_MainTex, i.uv).r;
}

float4 frag_color_copy (v2f i) : SV_TARGET
{
    return float4((tex2D(_MainTex, i.uv).xy * 500) * 0.5 + 0.5, 0, 1);
}

float4 frag_copy_with_depth (v2f i, out float depthValue : SV_DEPTH) : SV_TARGET
{
    depthValue = _gdepth.SampleLevel(my_point_clamp_sampler, i.uv, 0).r;
    return tex2D(_MainTex, i.uv);
}