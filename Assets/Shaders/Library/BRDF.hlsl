#pragma once
#include "Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Sampling/Sampling.hlsl"

float3x3 BuildTNB(float3 N)
{
    float3 t = (abs(N.x) > 0.99) ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 T = normalize(cross(N, t));
    float3 B = normalize(cross(N, T));
    return float3x3(T, N, B);
}

float RoughnessToAlpha(float x)
{
    return max(0.01, x * x);
}

// 微表面模型的D项
float D_GGX(float3 N, float3 H, float alpha)
{
    float a2 = square(alpha);
    float NdotH = max(0.0, dot(N, H));
    return a2 / (PI * square(square(NdotH) * (a2 - 1.0) + 1.0));
}

// 微表面模型的V项
float V_SmithGGXCorrelated(float3 N, float3 V, float3 L, float alpha)
{
    float NdotL = max(0, dot(N, L));
    float NdotV = max(0, dot(N, V));

    float a2 = square(alpha);

    float a = max(0, (-NdotL * a2 + NdotL) * NdotL + a2);
    float b = max(0, (-NdotV * a2 + NdotV) * NdotV + a2);
    float GGXL = NdotV * sqrt(a);
    float GGXV = NdotL * sqrt(b);
    return 0.5 / max(EPS, GGXV + GGXL);
}

float3 F_Schlick(float3 F0, float3 H, float3 V)
{
    float HdotV = max(0.0, dot(H, V));
    return F0 + (1.0 - F0) * pow(1.0 - HdotV, 5.0);
}

float3 UniformUnitVectorUsingCos(float cosTheta, float phi)
{
    float r = sqrt(1 - cosTheta * cosTheta);
    return float3(r * cos(phi), cosTheta, -r * sin(phi));
}

float3 GGXImportanceSampleH(float2 sample, float alpha)
{
    float a2 = alpha * alpha;
    float cosTheta = sqrt((1 - sample.x) / (sample.x * (a2 - 1) + 1));
    float phi = TWO_PI * sample.y;
    return UniformUnitVectorUsingCos(cosTheta, phi);
}

float Pdf_GGX(in Surface surface, float3 wi, float3 wo)
{
    if(surface.roughness == 1)
    {
        return max(1e-7, dot(surface.normal, wi)) / PI;
    }
    float alpha = RoughnessToAlpha(surface.roughness);
    float3 H = normalize(wi + wo);
    float D = D_GGX(surface.normal, H, alpha);
    float VdotH = max(1e-7, dot(wo, H));
    float NdotH = max(1e-7, dot(surface.normal, H));
    return D * NdotH / (4 * VdotH);
}

float3 BRDF_Diffuse(in Surface surface, float3 wi, float3 wo)
{
    return surface.color / PI;
}


float3 BRDF_GGX(in Surface surface, float3 wi, float3 wo)
{
    if(surface.roughness == 1)
    {
        return BRDF_Diffuse(surface, wi, wo);
    }
    float alpha = RoughnessToAlpha(surface.roughness);
    float3 H = normalize(wi + wo);
    float D = D_GGX(surface.normal, H, alpha);
    float V = V_SmithGGXCorrelated(surface.normal, wi, wo, alpha);
    float3 F = F_Schlick(surface.color, H, wi);
    return D * V * F;
}

float3 BRDFNoL_GGX(in Surface surface, float3 wi, float3 wo)
{
    if(surface.roughness == 1)
    {
        return BRDF_Diffuse(surface, wi, wo) * max(1e-5, dot(surface.normal, wi)) / PI;
    }
    float alpha = RoughnessToAlpha(surface.roughness);
    float3 H = normalize(wi + wo);
    float D = D_GGX(surface.normal, H, alpha);
    float V = V_SmithGGXCorrelated(surface.normal, wi, wo, alpha);
    float3 F = F_Schlick(surface.color, H, wi);
    return D * V * F * max(1e-5, dot(surface.normal, wi));
}

float3 BRDFNoL_GGX_NoAlbedo(in Surface surface, float3 wi, float3 wo)
{
    if(surface.roughness == 1)
    {
        return max(1e-7, dot(surface.normal, wi)) / PI;
    }
    float alpha = RoughnessToAlpha(surface.roughness);
    float3 H = normalize(wi + wo);
    float D = D_GGX(surface.normal, H, alpha);
    float V = V_SmithGGXCorrelated(surface.normal, wi, wo, alpha);
    float3 F = F_Schlick(1, H, wo);
    return D * V * F * max(1e-7, dot(surface.normal, wi));
}

float3 BRDF_GGX_NoAlbedo(in Surface surface, float3 wi, float3 wo)
{
    if(surface.roughness == 1)
    {
        return 1 / PI;
    }
    float alpha = RoughnessToAlpha(surface.roughness);
    float3 H = normalize(wi + wo);
    float D = D_GGX(surface.normal, H, alpha);
    float V = V_SmithGGXCorrelated(surface.normal, wi, wo, alpha);
    float3 F = F_Schlick(1, H, wi);
    return D * V * F;
}


float3 GGXImportanceSample(float2 sample, in Surface surface, float3 wo, out float3 wi, out float pdf)
{
    if(surface.roughness == 1)
    {
        float3 wIn = SampleHemisphereCosine(sample.x, sample.y, surface.normal);
        pdf = Pdf_GGX(surface, wIn, wo);
        wi = wIn;
        return surface.color;
    }
    float alpha = RoughnessToAlpha(surface.roughness);
    float3x3 TNB = BuildTNB(surface.normal);
    
    float3 H = GGXImportanceSampleH(sample, alpha);
    H = mul(transpose(TNB), H);

    float3 wIn = reflect(-wo, H);
    float V = V_SmithGGXCorrelated(surface.normal, wIn, wo, alpha);
    float3 F = F_Schlick(surface.color, H, wIn);
    float VdotH = max(1e-7, dot(wo, H));
    float NdotH = max(1e-7, dot(surface.normal, H));
    float NdotL = max(1e-7, dot(surface.normal, wIn));
    
    wi = wIn;
    pdf = Pdf_GGX(surface, wIn, wo);
    return (4 * F * V * VdotH * NdotL) / NdotH;
}

float3 GGXImportanceSample_NoAlbedo(float2 sample, in Surface surface, float3 wo, out float3 wi, out float pdf)
{
    if(surface.roughness == 1)
    {
        float3 wIn = SampleHemisphereCosine(sample.x, sample.y, surface.normal);
        pdf = Pdf_GGX(surface, wIn, wo);
        wi = wIn;
        return 1;
    }
    float alpha = RoughnessToAlpha(surface.roughness);
    float3x3 TNB = BuildTNB(surface.normal);
    
    float3 H = GGXImportanceSampleH(sample, alpha);
    H = mul(transpose(TNB), H);

    float3 wIn = reflect(-wo, H);
    float V = V_SmithGGXCorrelated(surface.normal, wIn, wo, alpha);
    float3 F = F_Schlick(1, H, wIn);
    float VdotH = max(1e-7, dot(wo, H));
    float NdotH = max(1e-7, dot(surface.normal, H));
    float NdotL = max(1e-7, dot(surface.normal, wIn));
    
    wi = wIn;
    pdf = Pdf_GGX(surface, wIn, wo);
    //(4 * F * V * VdotH * NdotL) / NdotH
    return (4 * F * V * VdotH * NdotL) / NdotH; //BRDFNoL_GGX_NoAlbedo(surface, wIn, wo) / pdf;
}


float3 GGXImportanceSample_NoAlbedo_U(float2 sample, in Surface surface, float3 wo, out float3 wi, out float pdf)
{
    float alpha = RoughnessToAlpha(surface.roughness);
    float3x3 TNB = BuildTNB(surface.normal);

    if(surface.roughness == 1)
    {
        float3 wIn = SampleSphereUniform(sample.x, sample.y);
        wIn = mul(transpose(TNB), wIn.xzy);
        pdf = 1 / (2 * PI);
        wi = wIn;
        float NdotL = max(1e-7, dot(surface.normal, wIn));
        return 2 * NdotL;
    }
    
    float3 H = GGXImportanceSampleH(sample, alpha);
    H = mul(transpose(TNB), H);

    float3 wIn = reflect(-wo, H);
    float V = V_SmithGGXCorrelated(surface.normal, wIn, wo, alpha);
    float3 F = F_Schlick(1, H, wIn);
    float VdotH = max(1e-7, dot(wo, H));
    float NdotH = max(1e-7, dot(surface.normal, H));
    float NdotL = max(1e-7, dot(surface.normal, wIn));
    
    wi = wIn;
    pdf = Pdf_GGX(surface, wIn, wo);
    //(4 * F * V * VdotH * NdotL) / NdotH
    return (4 * F * V * VdotH * NdotL) / NdotH; //BRDFNoL_GGX_NoAlbedo(surface, wIn, wo) / pdf;
}