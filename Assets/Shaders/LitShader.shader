Shader "Custom/Lit"
{
    Properties
    {
        _CubeMap ("CubeMap", CUBE) = "" {}
        _MainTex ("Texture", 2D) = "white" {}
        _TintColor ("TintColor", Color) = (1, 1, 1, 1)
        _Roughness ("Roughness", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque"  }
        LOD 100

        Pass
        {
            Tags {"LightMode" = "CustomLit"}

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
            #pragma target 5.0

            #include "Library/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : VAR_POSITION;
                float3 normalWS : VAR_NORMAL;
                float2 uv : VAR_BASE_UV;
            };
            samplerCUBE _CubeMap;
            float4 _CubeMap_ST;
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            
            float4 _TintColor;

            float  _Roughness;

            float4x4  _vpMatrix;
            float4x4  _vpMatrixPrev;
            Texture2D _ScreenTexture;
            Texture2D _ScreenTextureDepth;

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 pos = TransformObjectToWorld(v.positionOS);
                o.positionWS = pos;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            bool TraceSSR(float3 start, float3 dir, out float2 uv, out float t)
            {
                t = 0;
                for(int i = 1; i < 200; i++)
                {
                    float3 pos = start + dir * i * 0.01;
                    float4 posCS = mul(_vpMatrixPrev, float4(pos, 1));//TransformWorldToHClip(pos);
                    posCS.xyz /= posCS.w;
                    posCS.xy = posCS.xy * 0.5 + 0.5;
                    if(posCS.x < 0 || posCS.y < 0 || posCS.x > 1 || posCS.y > 1
                        || posCS.z < 0 || posCS.z > 1)
                    {
                        break;
                    }

                    float2 uvInv = float2(posCS.x, 1 - posCS.y);
                    float depth = _ScreenTextureDepth.SampleLevel(_my_point_clamp_sampler, uvInv, 0).r;

                    if(posCS.z < depth - 1e-3)
                    {
                        uv = uvInv;
                        return true;
                    }
                    t++;
                }
                uv = 0;
                return false;
            }

            float4 frag (Varyings i) : SV_Target
            {
                // sample the texture
                float4 col = tex2D(_MainTex, i.uv) * _TintColor;
                float3 N = normalize(i.normalWS);
                Surface surface;
                surface.worldPos = i.positionWS;
                surface.normal = N;
                surface.color = col.xyz;
                surface.alpha = col.a;
                if(_Roughness == 0)
                {
                    // float4 posCS = mul(_vpMatrixPrev, float4(i.positionWS, 1));//TransformWorldToHClip(pos);
                    // posCS.xyz /= posCS.w;
                    // posCS.xy = posCS.xy * 0.5 + 0.5;
                    // return float4(posCS.z, 0, 0, 1);
                    
                    float2 uv;
                    float3 I = normalize(surface.worldPos - _WorldSpaceCameraPos);
                    float3 R = normalize(reflect(I, N));
                    float t;
                    // return float4(R*0.5+0.5, 1);

                    if(TraceSSR(surface.worldPos + N * 0.01, R, uv, t))
                    {
                        //t /= 200;
                        //return float4(t, t, t, 1);
                        return _ScreenTexture.SampleLevel(_my_point_clamp_sampler, uv, 0);
                    }
                    return texCUBE(_CubeMap, R);
                    return float4(Shading(surface), surface.alpha);
                }
                return float4(Shading(surface), surface.alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Tags {
                "LightMode" = "ShadowCaster"
            }

            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert_shadow
            #pragma fragment frag_shadow
            // make fog work
            #pragma multi_compile_fog
            #pragma target 4.0
            #pragma shader_feature _CLIPPING

            #include "Library/Common.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionWS : VAR_POSITION;
                float3 normalWS : VAR_NORMAL;
                float2 uv : VAR_BASE_UV;
            };

            Varyings vert_shadow (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.positionWS = mul(unity_ObjectToWorld, v.positionOS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            float4 frag_shadow (Varyings i) : SV_Target
            {
                return float4(0, 0, 0, 0);
            }

            ENDHLSL
        }
    }
}
