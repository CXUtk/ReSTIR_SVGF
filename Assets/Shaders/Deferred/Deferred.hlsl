#pragma once
#include "../Library/Common.hlsl"
#include "../Library/Lighting.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD;
    float3 normalOS : NORMAL;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : VAR_POSITION;
    float3 normalWS : VAR_NORMAL;
    float2 uv : VAR_BASE_UV;
};

sampler2D   _Albedo;
float4      _Albedo_ST;

samplerCUBE _CubeMap;

float4      _TintColor;
float       _Metallic;
float       _Roughness;
float      _ScreenBufferSizeX;
float      _ScreenBufferSizeY;

float4x4 _vpMatrix;
float4x4 _viewMatrix;
float4x4 _inverseViewMatrix;
float4x4 _projectionMatrix;
float4x4 _vpMatrixPrev;

float _nearPlaneZ;
float4 _ZBufferParams;

Varyings vert_gbuffer (Attributes v)
{
    Varyings o;
    float4 worldPos = mul(unity_ObjectToWorld, v.positionOS);
    o.positionWS = worldPos.xyz / worldPos.w;
    o.positionCS = TransformWorldToHClip(worldPos);
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.uv = TRANSFORM_TEX(v.uv, _Albedo);
    return o;
}

void frag_gbuffer (Varyings i, 
out float4 albedoR : SV_Target0,
out float4 normalM : SV_Target1,
out float2 motionVector : SV_Target2,
out float4 worldPos : SV_Target3
)
{
    float4 albedo = tex2D(_Albedo, i.uv) * _TintColor;

    albedoR.rgb = albedo.rgb;
    albedoR.a   = _Roughness;

    normalM.rgb = normalize(i.normalWS) * 0.5 + 0.5;
    normalM.a   = _Metallic;

    float4 prevPos = mul(_vpMatrixPrev, float4(i.positionWS, 1));
    prevPos.xyz /= prevPos.w;
    motionVector = prevPos.xy - i.positionCS.xy;

    worldPos = float4(i.positionWS, 1);
}


Texture2D _gdepth;
Texture2D _albedoR;
Texture2D _normalM;
Texture2D _motionVector;
Texture2D _worldPos;

Texture2D _lastFrameScreen;

SamplerState my_point_clamp_sampler;

Varyings vert_lit (Attributes v)
{
    Varyings o;
    o.positionCS = TransformObjectToHClip(v.positionOS);
    o.positionWS = float3(0, 0, 0);
    o.normalWS = float3(0, 0, 0);
    o.uv = v.uv;
    return o;
}

bool TraceScreenSpace(float3 start, float3 dir, out float3 hitPoint)
{
    hitPoint = float3(-1, -1, -1);
    
    float3 origViewSpace = mul(_viewMatrix, float4(start, 1));
    float3 dirViewSpace = normalize(mul(_viewMatrix, float4(dir, 0)));

    const float maxDistance = 20;
    float maxSteps = 400;
    
    // Clip to the near plane
    float rayLength = ((origViewSpace.z + dirViewSpace.z * maxDistance) > _nearPlaneZ)
        ? (_nearPlaneZ - origViewSpace.z) / dirViewSpace.z : maxDistance;
    float3 endPointViewSpace = origViewSpace + dirViewSpace * rayLength;

    // Project into screen space
    float4 H0 = mul(_projectionMatrix, float4(origViewSpace, 1)),
    H1 = mul(_projectionMatrix, float4(endPointViewSpace, 1));
    float k0 = 1.0 / H0.w, k1 = 1.0 / H1.w;
    float3 Q0 = origViewSpace * k0, Q1 = endPointViewSpace * k1;
    
    // Screen space end points
    float2 P0 = H0.xy * k0, P1 = H1.xy * k1;

    // Edge case
    P1 += (dot(P0 - P1, P0 - P1) < 1e-4) ? float3(0.01, 0.01, 0.01) : float3(0, 0, 0);

    P0 = (P0 * 0.5 + 0.5) * float2(_ScreenBufferSizeX, _ScreenBufferSizeY);
    P1 = (P1 * 0.5 + 0.5) * float2(_ScreenBufferSizeX, _ScreenBufferSizeY);
    float2 delta = P1 - P0;
    
    bool permute = false;
    if (abs(delta.x) < abs(delta.y))
    {
        permute = true;
        delta = delta.yx;
        P0 = P0.yx;
        P1 = P1.yx;
    }

    float stepDir = sign(delta.x), invdx = stepDir / delta.x;

    // Q and k for each step
    float3 dQ = (Q1 - Q0) * invdx;
    float dk = (k1 - k0) * invdx;
    float2 dP = float2(stepDir, delta.y * invdx);

    // Do line rasterization
    float3 Q = Q0;
    float k = k0, stepCount = 0.0, end = P1.x * stepDir;
    float2 curScreenCoord;
    for(float2 P = P0; P.x * stepDir <= end && stepCount < maxSteps; P += dP)
    {
        curScreenCoord = (permute ? P.yx : P) / float2(_ScreenBufferSizeX, _ScreenBufferSizeY);
        curScreenCoord.y = 1 - curScreenCoord.y;
        
        if(curScreenCoord.x < 0 || curScreenCoord.y < 0
            || curScreenCoord.x > 1 || curScreenCoord.y > 1)
        {
            break;
        }
        
        float depth = _gdepth.SampleLevel(_my_point_clamp_sampler, curScreenCoord, 0).r;
        float depth2 = LinearEyeDepth(depth, _ZBufferParams);

        float rayZMax = -(dQ.z * 0.5 + Q.z) / (dk * 0.5 + k);
        
        if(rayZMax > depth2 + 0.05 && rayZMax < depth2 + 0.22)
        {
            hitPoint = mul(_inverseViewMatrix, float4(float3((Q.xy + dQ.xy * stepCount), Q.z) * (1.0 / k), 1));
            // hitPoint = float3(depth, 0, 0);
            return true;
        }

        
        Q.z += dQ.z;
        k += dk;
        stepCount++;
    }
    return false;
}

bool TraceSSR(float3 start, float3 dir, out float3 worldPos)
{
    for(int i = 1; i < 200; i++)
    {
        float3 pos = start + dir * i * 0.02;
        float4 posCS = mul(_vpMatrix, float4(pos, 1));
        posCS.xyz /= posCS.w;
        posCS.xy = posCS.xy * 0.5 + 0.5;
        if(posCS.x < 0 || posCS.y < 0 || posCS.x > 1 || posCS.y > 1
            || posCS.z < 0 || posCS.z > 1)
        {
            break;
        }

        float2 uvInv = float2(posCS.x, 1 - posCS.y);
        float depth = _gdepth.SampleLevel(_my_point_clamp_sampler, uvInv, 0).r;

        if(posCS.z < depth && posCS.z > depth - 0.001)
        {
            worldPos = pos;
            return true;
        }
    }
    worldPos = 0;
    return false;
}

float4 frag_lit (Varyings i,  out float depthValue : SV_DEPTH) : SV_TARGET
{
    depthValue = _gdepth.Sample(my_point_clamp_sampler, i.uv).r;
    float4 albedo = _albedoR.Sample(my_point_clamp_sampler, i.uv);
    float roughness = albedo.a;
    float3 col = albedo.xyz * _TintColor.xyz;
    float4 posWS = _worldPos.Sample(my_point_clamp_sampler, i.uv);
    float4 normalM = _normalM.Sample(my_point_clamp_sampler, i.uv);
    if(normalM.x == 0 && normalM.y == 0 && normalM.z == 0)
    {
        return float4(0, 0, 0, 0);
    }
    
    float3 N = normalize(normalM.xyz * 2 - 1);
    Surface surface;
    surface.worldPos = posWS.xyz;
    surface.normal = N;
    surface.color = col.xyz;
    surface.alpha = 1;

    if(roughness == 0)
    {
        // float4 posCS = mul(_vpMatrix, float4(posWS.xyz, 1));//TransformWorldToHClip(pos);
        // posCS.xyz /= posCS.w;
        // posCS.xy = posCS.xy * 0.5 + 0.5;
        // return float4(posCS.z, 0, 0, 1);
                    
        float3 hitPos;
        float3 I = normalize(posWS.xyz - _WorldSpaceCameraPos);
        float3 R = normalize(reflect(I, N));
        float t;
        // return float4(R*0.5+0.5, 1);

        if(TraceScreenSpace(posWS.xyz + N * 0.01, R, hitPos))
        {
            float4 posCS = mul(_vpMatrixPrev, float4(hitPos, 1));
            posCS.xyz /= posCS.w;
            posCS.xy = posCS.xy * 0.5 + 0.5;
            posCS.y = 1 - posCS.y;
            
            return _lastFrameScreen.SampleLevel(_my_point_clamp_sampler, posCS.xy, 0);
        }
        return texCUBE(_CubeMap, R);
        return float4(Shading(surface), surface.alpha);
    }
    
    return float4(Shading(surface), surface.alpha);
}


Varyings vert_shadowcaster (Attributes v)
{
    Varyings o;
    o.positionCS = TransformObjectToHClip(v.positionOS);
    o.positionWS = mul(unity_ObjectToWorld, v.positionOS);
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    return o;
}

float4 frag_shadowcaster (Varyings i) : SV_Target
{
    return float4(0, 0, 0, 0);
}