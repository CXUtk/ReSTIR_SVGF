#include "../RayTracing/RayTraceLighting.hlsl"
#include "UnityRayTracingMeshUtils.cginc"
// ------------------ Ray tracing ------------------

Texture2D _gdepth;
Texture2D _albedoR;
Texture2D _normalM;
Texture2D _motionVector;
Texture2D _worldPos;
Texture2D _emission;

Texture2D _lastFrameScreen;

SamplerState my_point_clamp_sampler;
Texture2D<float4>   _Albedo;
float4      _Albedo_ST;
float4      _TintColor;
float       _Metallic;
float       _Roughness;
float4      _Emission;
float       _EmissionIntensity;

uint _uGlobalFrames;
                   
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

[shader("closesthit")]
void MyHitPathTracerShader(inout PathTracingPayload payload : SV_RayPayload,
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
    float3 normalWS = normalize(mul((float3x3)ObjectToWorld3x4(), v0.normal * barycentrics.x + v1.normal * barycentrics.y + v2.normal * barycentrics.z));
    float3 wo = -WorldRayDirection();
    
    float3 E =_Emission * _EmissionIntensity;
    if (dot(normalWS, wo) <= 0)
    {
        E = 0;
    }
    
    if(dot(normalWS, WorldRayDirection()) > 0)
    {
        normalWS = -normalWS;
    }
    
    float3 color = _Albedo.SampleLevel(my_point_clamp_sampler, texCoord, 0) * _TintColor;
    
    // Surface surface;
    // surface.worldPos = posWS;
    // surface.normal = normalWS;
    // surface.color = color;
    // surface.alpha = 1;
    // surface.roughness = _Roughness;
    // surface.metallic = _Metallic;
    // surface.emission = E;

    payload.L = 1;
    payload.T = RayTCurrent();
    payload.N = normalWS;
    payload.Albedo = color;
    payload.E = E;
    payload.Roughness = _Roughness;
    payload.Metallic = _Metallic;
}
