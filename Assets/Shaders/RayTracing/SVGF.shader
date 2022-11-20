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
		
		Pass
		{
			Name "Edge-avoiding A-trous wavelet transform"
			HLSLPROGRAM
			#pragma vertex vert_tex2D
			#pragma fragment main_filter
			
			#include "SVGF.hlsl"
			ENDHLSL
		}
		
		Pass
		{
			Name "Final Gather"
			HLSLPROGRAM
			#pragma vertex vert_tex2D
			#pragma fragment final_gather
			
			#include "SVGF.hlsl"
			ENDHLSL
		}
		
		Pass
		{
			Name "Variance Estimation [3]"
			HLSLPROGRAM
			#pragma vertex vert_tex2D
			#pragma fragment variance_estimation
			
			#include "SVGF.hlsl"
			ENDHLSL
		}
		
		Pass
		{
			Name "[Check] ReSTIR Color Check [4]"
			HLSLPROGRAM
			#pragma vertex vert_tex2D
			#pragma fragment restir_color_check
			
			#include "SVGF.hlsl"
			ENDHLSL
		}
	}
}