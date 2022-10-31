Shader "RayTracing/SVGF"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader
	{
		Pass
		{
			Name "Temporal Filtering"
			HLSLPROGRAM
			#pragma vertex vert_tex2D
			#pragma fragment temporal_filter
			
			#include "SVGF.hlsl"
			ENDHLSL
		}
	}
}