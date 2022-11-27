#pragma once
#include "../Library/BRDF.hlsl"

struct MyPayload
{
    float4 color;
    float T;
    float3 N;
};

struct PathTracingPayload
{
    float3 L;
    float T;
    float3 N;
    float3 E;
    float3 Albedo;
    float Roughness;
    float Metallic;
};

RaytracingAccelerationStructure _RaytracingAccelerationStructure : register(t0);

#define MAX_DIRECTIONAL_LIGHTS 4
int         _DirectionalLightCount;
float4      _DirectionalLightColors[MAX_DIRECTIONAL_LIGHTS];
float4      _DirectionalLightDirections[MAX_DIRECTIONAL_LIGHTS];

#define MAX_AREA_LIGHTS 8
int         _AreaLightCount;
float4      _AreaLightEmission[MAX_AREA_LIGHTS];
float4      _AreaLightVA[MAX_AREA_LIGHTS];
float4      _AreaLightVB[MAX_AREA_LIGHTS];
float4      _AreaLightVC[MAX_AREA_LIGHTS];


float PowerHeuristic(int nf, float fPdf, int ng, float gPdf)
{
    float f = nf * fPdf, g = ng * gPdf;
    return (f * f) / (f * f + g * g);
}

PathTracingPayload CreatePathTracingPayload()
{
    PathTracingPayload payload;
    payload.L = 0;
    payload.T = 999999;
    payload.N = 0;
    payload.E = 0;
    payload.Albedo = 0;
    payload.Roughness = 1;
    payload.Metallic = 0;
    return payload;
}

uint wang_hash(inout uint seed)
{
    seed = (seed ^ 61) ^ (seed >> 16);
    seed *= 9;
    seed = seed ^ (seed >> 4);
    seed *= 0x27d4eb2d;
    seed = seed ^ (seed >> 15);
    return seed;
}

bool rayTriangleIntersect(float3 orig, float3 dir, float3 v0, float3 v1, float3 v2, out float t)
{
    float3 edge1, edge2, h, s, q;
    float a,f,u,v;
    edge1 = v1 - v0;
    edge2 = v2 - v0;
    h = cross(dir, edge2);//rayVector.crossProduct(edge2);
    a = dot(edge1, h);//edge1.dotProduct(h);
    if (abs(a) < 1e-5)
        return false;    // This ray is parallel to this triangle.
    f = 1.0/a;
    s = orig - v0;
    u = f * dot(s, h);//s.dotProduct(h);
    if (u < 0.0 || u > 1.0)
        return false;
    q = cross(s, edge1);//s.crossProduct(edge1);
    v = f * dot(dir, q);//rayVector.dotProduct(q);
    if (v < 0.0 || u + v > 1.0)
        return false;
    // At this stage we can compute t to find out where the intersection point is on the line.
    t = f * dot(edge2, q);//edge2.dotProduct(q);
    if (t > 0) // ray intersection
    {
        return true;
    }
    else
    {
        // This means that there is a line intersection but not a ray intersection.
        return false;
    }
    // // compute plane's normal
    // float3 v0v1 = v1 - v0; 
    // float3 v0v2 = v2 - v0; 
    // // no need to normalize
    // float3 N = cross(v0v1, v0v2);  //N 
    // float area2 = length(N); 
    //
    // // Step 1: finding P
    //
    // // check if ray and plane are parallel.
    // float NdotRayDirection = dot(N, dir); 
    // if (abs(NdotRayDirection) < 1e-5)  //almost 0 
    //     return false;  //they are parallel so they don't intersect ! 
    //
    // // compute d parameter using equation 2
    // float d = -dot(N, v0); 
    //
    // // compute t (equation 3)
    // t = -(dot(N, orig) + d) / NdotRayDirection;
    //
    // // check if the triangle is in behind the ray
    // if (t < 0) return false;  //the triangle is behind 
    //
    // // compute the intersection point using equation 1
    // float3 P = orig + t * dir; 
    //
    // // Step 2: inside-outside test
    // float3 C;  //vector perpendicular to triangle's plane 
    //
    // // edge 0
    // float3 edge0 = v1 - v0; 
    // float3 vp0 = P - v0; 
    // C = cross(edge0, vp0); 
    // if (dot(N, C) < 0) return false;  //P is on the right side 
    //
    // // edge 1
    // float3 edge1 = v2 - v1; 
    // float3 vp1 = P - v1; 
    // C = cross(edge1, vp1); 
    // if (dot(N, C) < 0)  return false;  //P is on the right side 
    //
    // // edge 2
    // float3 edge2 = v0 - v2; 
    // float3 vp2 = P - v2; 
    // C = cross(edge2, vp2); 
    // if (dot(N, C) < 0) return false;  //P is on the right side; 
    //
    // return true;  //this ray hits the triangle 
}


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


float3 Direct_AreaLight(in Surface surface, float3 wo, uint seed)
{
    float3 finalColor = 0;
    for(int i = 0; i < min(_AreaLightCount, MAX_AREA_LIGHTS); i++)
    {
        // Light
        float a = wang_hash(seed) / 4294967295.0;
        seed = seed * 2 + 773;
        float b = wang_hash(seed) / 4294967295.0;
        
        float u0 = sqrt(a);
        float2 S = float2(1 - u0, b * u0);
        
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
        shadowRay.TMax = length(lightPos - surface.worldPos) - 1e-2;

        TraceRay(_RaytracingAccelerationStructure, (RAY_FLAG_SKIP_CLOSEST_HIT_SHADER | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH), 
        0xFF, 0, 1, 0, shadowRay, shadowPayLoad);

        float3 lightNormal = cross(_AreaLightVB[i] - _AreaLightVA[i], _AreaLightVC[i] - _AreaLightVA[i]);
        float areaOfLight = 0.5 * length(lightNormal);
        lightNormal = normalize(lightNormal);
        float theta = max(1e-5, dot(lightNormal, -wLight));
        float pdfLight = Length2(lightPos - surface.worldPos) / (theta * areaOfLight);

        float3 BRDFCosTheta = _AreaLightEmission[i] * BRDFNoL_GGX_NoAlbedo(surface, wLight, wo) / pdfLight;
        float pdfBSDF = Pdf_GGX(surface, wLight, wo);

        float weightMIS = PowerHeuristic(1, pdfLight, 1, pdfBSDF);
        
        finalColor += shadowPayLoad.color.a * BRDFCosTheta * weightMIS;

        // BSDF
        a = wang_hash(seed) / 4294967295.0;
        seed = seed * 2 + 773;
        b = wang_hash(seed) / 4294967295.0;
    }
    return finalColor;
}


float3 Direct_DirectionalLight_alter(in Surface surface, float3 wo, float3 changeAlbedo)
{
    float3 finalColor = 0;
    for(int i = 0; i < min(_DirectionalLightCount, MAX_DIRECTIONAL_LIGHTS); i++)
    {
        PathTracingPayload shadowPayLoad = CreatePathTracingPayload();
        
        RayDesc shadowRay;
        float3 wLight = _DirectionalLightDirections[i].xyz;
        shadowRay.Origin = surface.worldPos + surface.normal * 1e-3; 
        shadowRay.Direction = wLight;
        shadowRay.TMin = 0;
        shadowRay.TMax = 10000;

        TraceRay(_RaytracingAccelerationStructure, (RAY_FLAG_SKIP_CLOSEST_HIT_SHADER | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH), 
        0xFF, 0, 1, 0, shadowRay, shadowPayLoad);

        float3 BRDFCosTheta = _DirectionalLightColors[i] * changeAlbedo * BRDFNoL_GGX_NoAlbedo(surface, wLight, wo);

        finalColor += shadowPayLoad.L * BRDFCosTheta;
    }
    return finalColor;
}


float3 Direct_AreaLight_alter(in Surface surface, float3 wo, uint seed, float3 changeAlbedo)
{
    float3 finalColor = 0;
    for(int i = 0; i < min(_AreaLightCount, MAX_AREA_LIGHTS); i++)
    {
        float3 orig = surface.worldPos + surface.normal * 1e-3;
        
        // Light ---------------------------------------
        float a = wang_hash(seed) / 4294967295.0;
        seed = seed + 734273;
        float b = wang_hash(seed) / 4294967295.0;
        
        float u0 = sqrt(a);
        float2 S = float2(1 - u0, b * u0);
        
        PathTracingPayload shadowPayLoad = CreatePathTracingPayload();
        
        RayDesc shadowRay;
        float3 lightPos = (1 - S.x - S.y) * _AreaLightVA[i] + S.x * _AreaLightVB[i] + S.y * _AreaLightVC[i];
        float3 wLight = normalize(lightPos - surface.worldPos);
        shadowRay.Origin = orig; 
        shadowRay.Direction = lightPos - orig;
        shadowRay.TMin = 0;
        shadowRay.TMax = 1 - 1e-3;

        TraceRay(_RaytracingAccelerationStructure, (RAY_FLAG_SKIP_CLOSEST_HIT_SHADER | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH), 
        0xFF, 0, 1, 0, shadowRay, shadowPayLoad);

        float3 lightNormal = cross(_AreaLightVB[i] - _AreaLightVA[i], _AreaLightVC[i] - _AreaLightVA[i]);
        float areaOfLight = 0.5 * length(lightNormal);
        lightNormal = normalize(lightNormal);
        float theta = max(1e-5, dot(lightNormal, -wLight));
        float pdfLight = Length2(lightPos - surface.worldPos) / (theta * areaOfLight);

        float3 BRDFCosThetaDivPdf = _AreaLightEmission[i] * changeAlbedo * BRDFNoL_GGX_NoAlbedo(surface, wLight, wo) / pdfLight;
        float pdfBSDF = Pdf_GGX(surface, wLight, wo);

        float weightMIS = PowerHeuristic(1, pdfLight, 1, pdfBSDF);
        
        finalColor += shadowPayLoad.L * BRDFCosThetaDivPdf * weightMIS;

        // BSDF ---------------------------------------

        a = wang_hash(seed) / 4294967295.0;
        seed = seed + 3214231;
        b = wang_hash(seed) / 4294967295.0;

        float3 wi;
        float3 brdfNoLDivPdf = changeAlbedo * GGXImportanceSample_NoAlbedo(float2(a, b), surface, wo, wi, pdfBSDF);
        float tt = 1;
        if(rayTriangleIntersect(orig, wi, _AreaLightVA[i], _AreaLightVB[i], _AreaLightVC[i], tt))
        {
            shadowRay.Origin = orig; 
            shadowRay.Direction = wi;
            shadowRay.TMin = 0;
            shadowRay.TMax = tt - 1e-2;

            shadowPayLoad = CreatePathTracingPayload();
            
            TraceRay(_RaytracingAccelerationStructure, (RAY_FLAG_SKIP_CLOSEST_HIT_SHADER | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH), 
            0xFF, 0, 1, 0, shadowRay, shadowPayLoad);

            theta = max(1e-5, dot(lightNormal, -wi));
            pdfLight = tt * tt / (theta * areaOfLight);

            weightMIS = PowerHeuristic(1, pdfBSDF, 1, pdfLight);
            finalColor += 0 * shadowPayLoad.L * brdfNoLDivPdf * weightMIS;
        }
    }
    return finalColor;
}