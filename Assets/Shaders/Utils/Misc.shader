Shader "Utils/Misc"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader
	{
		// No culling or depth
		Pass
		{
			Name "DepthCopy"
			ColorMask 0
			ZTest Always
			
			HLSLPROGRAM
			#pragma vertex vert_tex2D
			#pragma fragment frag_depth_copy
			
			#include "Misc.hlsl"
			ENDHLSL
		}
		
		// No culling or depth
		Pass
		{
			Name "Copy color with depth"
			ZWrite On
			ZTest Always
			
			HLSLPROGRAM
			#pragma vertex vert_tex2D
			#pragma fragment frag_copy_with_depth
			
			#include "Misc.hlsl"
			ENDHLSL
		}
		
		Pass
		{
			Name "Color Copy Sampled"
			ZTest Always
			
			HLSLPROGRAM
			#pragma vertex vert_tex2D
			#pragma fragment frag_color_copy_sample
			
			#include "Misc.hlsl"
			ENDHLSL
		}
	}
}