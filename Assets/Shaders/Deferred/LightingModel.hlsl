#include "../Library/BRDF.hlsl"
#include "UnityRayTracingMeshUtils.cginc"
// ------------------ Ray tracing ------------------

Texture2D _gdepth;
Texture2D _albedoR;
Texture2D _normalM;
Texture2D _motionVector;
Texture2D _worldPos;

Texture2D _lastFrameScreen;

SamplerState my_point_clamp_sampler;

RaytracingAccelerationStructure _RaytracingAccelerationStructure : register(t0);
Texture2D<float4>   _Albedo;
float4      _Albedo_ST;
float4      _TintColor;
float       _Metallic;
float       _Roughness;

#define     MAX_DIRECTIONAL_LIGHTS 4
int         _DirectionalLightCount;
float3      _DirectionalLightColors[MAX_DIRECTIONAL_LIGHTS];
float3      _DirectionalLightDirections[MAX_DIRECTIONAL_LIGHTS];

struct MyPayload
{
    float4 color;
    float T;
    float3 N;
};
                        
struct AttributeData
{
    float2 barycentrics; 
};

struct Vertex
{
    float2 texcoord;
    float3 position;
    float3 normal;
};

[shader("anyhit")] // Add to hit group #0
void ShadowAnyHit(inout MyPayload pay, BuiltInTriangleIntersectionAttributes attrib) 
{
    pay.color = 0;
}


[shader("closesthit")]
void MyHitShader(inout MyPayload payload : SV_RayPayload,
  AttributeData attributes : SV_IntersectionAttributes)
{
    uint primitiveIndex = PrimitiveIndex();
    uint3 triangleIndicies = UnityRayTracingFetchTriangleIndices(primitiveIndex);
    Vertex v0, v1, v2;
    v0.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.x, kVertexAttributeTexCoord0);
    v1.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.y, kVertexAttributeTexCoord0);
    v2.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.z, kVertexAttributeTexCoord0);

    // v0.position = UnityRayTracingFetchVertexAttribute3(triangleIndicies.x, kVertexAttributePosition);
    // v1.position = UnityRayTracingFetchVertexAttribute3(triangleIndicies.y, kVertexAttributePosition);
    // v2.position = UnityRayTracingFetchVertexAttribute3(triangleIndicies.z, kVertexAttributePosition);

    v0.normal = UnityRayTracingFetchVertexAttribute3(triangleIndicies.x, kVertexAttributeNormal);
    v1.normal = UnityRayTracingFetchVertexAttribute3(triangleIndicies.y, kVertexAttributeNormal);
    v2.normal = UnityRayTracingFetchVertexAttribute3(triangleIndicies.z, kVertexAttributeNormal);
    
    
    float3 barycentrics = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y, attributes.barycentrics.x, attributes.barycentrics.y);

    float2 texCoord = v0.texcoord * barycentrics.x + v1.texcoord * barycentrics.y + v2.texcoord * barycentrics.z;
    float3 posWS = WorldRayOrigin() + RayTCurrent() * WorldRayDirection();
    ////v0.position * barycentrics.x + v1.position * barycentrics.y + v2.position * barycentrics.z;
    // float3 posWS = mul(ObjectToWorld3x4(), v0.position * barycentrics.x + v1.position * barycentrics.y + v2.position * barycentrics.z);
    float3 normalWS = normalize(mul((float3x3)ObjectToWorld3x4(), v0.normal * barycentrics.x + v1.normal * barycentrics.y + v2.normal * barycentrics.z));

    // payload.color = float4(normalWS * 0.5 + 0.5, 1);
    // return;
    if(dot(normalWS, WorldRayDirection()) > 0)
    {
        normalWS = -normalWS;
    }
    
    float3 color = _Albedo.SampleLevel(my_point_clamp_sampler, texCoord, 0) * _TintColor;

    float3 wo = -WorldRayDirection();
    float3 wLight = _DirectionalLightDirections[0].xyz;
    
    Surface surface;
    surface.worldPos = posWS;
    surface.normal = normalWS;
    surface.color = color;
    surface.alpha = 1;
    surface.roughness = _Roughness;
    surface.metallic = _Metallic;

    MyPayload shadowPayLoad;
    shadowPayLoad.color = float4(0, 0, 0, 0);
    shadowPayLoad.T = 10000;
    shadowPayLoad.N = 0;
    
    RayDesc shadowRay;
    shadowRay.Origin = posWS + normalWS * 1e-2; 
    shadowRay.Direction = wLight;
    shadowRay.TMin = 0;
    shadowRay.TMax = 10000;
    
    TraceRay(_RaytracingAccelerationStructure, (RAY_FLAG_SKIP_CLOSEST_HIT_SHADER | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH), 
    0xFF, 0, 1, 0, shadowRay, shadowPayLoad);

    float NdotL = max(0, dot(normalWS, wLight));
    float3 BRDFCosTheta = shadowPayLoad.color.a * _DirectionalLightColors[0] * BRDF_GGX(surface,_DirectionalLightDirections[0].xyz, wo) * NdotL;
    payload.color = float4(BRDFCosTheta, 1);
    payload.T = RayTCurrent();
    payload.N = normalWS;
}