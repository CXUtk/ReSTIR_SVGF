Shader "Custom Deferred/Default"
{
    Properties
    {
        _Albedo ("Albedo", 2D) = "white" {}
        _TintColor ("TintColor", Color) = (1, 1, 1, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Roughness ("Roughness", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            // Generate GBuffer
            Tags{ "LightMode" = "GBuffer_Generate" }

            HLSLPROGRAM
            #pragma vertex vert_gbuffer
            #pragma fragment frag_gbuffer
            // make fog work
            #pragma multi_compile_fog

            #include "../Deferred/Deferred.hlsl"
            ENDHLSL
        }
        
        Pass
        {
            // Lit GBuffer
            Tags{ "LightMode" = "GBuffer" }

            HLSLPROGRAM
            #pragma vertex vert_lit
            #pragma fragment frag_lit
            // make fog work
            #pragma multi_compile_fog

            #include "../Deferred/Deferred.hlsl"
            ENDHLSL
        }
        
        Pass
        {
            Tags {
                "LightMode" = "ShadowCaster"
            }    
            ColorMask 0
            
            HLSLPROGRAM
            #pragma vertex vert_shadowcaster
            #pragma fragment frag_shadowcaster
            // make fog work
            #pragma multi_compile_fog

            #include "../Deferred/Deferred.hlsl"
            ENDHLSL
        }
    }
}
