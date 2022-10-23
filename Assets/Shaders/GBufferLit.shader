Shader "Custom Deferred/GBufferLit"
{
    Properties
    {
        _Albedo ("Albedo", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {

            Tags{ "LightMode" = "GBuffer" }

            HLSLPROGRAM
            #pragma vertex vert_lit
            #pragma fragment frag_lit
            // make fog work
            #pragma multi_compile_fog

            // #include "Library/Deferred.hlsl"
            ENDHLSL
        }
    }
}
