Shader "Hidden/MaskComposite"
{
	Properties
	{
		_MainTex ("Source", 2D) = "white" {}
		_MaskTex ("Mask", 2D) = "black" {}
		_SDFTex ("SDF", 2D) = "black" {}
		_EdgeMinTex ("Edge Min Distance", 2D) = "black" {}
        _FalloffDistance ("Falloff Distance", Float) = 0.1
        _FalloffPower ("Falloff Power", Float) = 2.0
        _RaymarchSteps ("Raymarch Steps (max 128)", Int) = 48
        _StepSize ("Ray Step Size (in pixels)", Float) = 1.25
        _CoarseMultiplier ("Coarse Step Multiplier", Float) = 4.0
        _RefineSteps ("Binary Refine Steps", Int) = 5
		_ObjectCount ("Object Count", Int) = 0
		_ObjectPos0 ("Object Position 0", Vector) = (0.5, 0.5, 0, 0)
		_ObjectPos1 ("Object Position 1", Vector) = (0.5, 0.5, 0, 0)
		_ObjectPos2 ("Object Position 2", Vector) = (0.5, 0.5, 0, 0)
		_ObjectPos3 ("Object Position 3", Vector) = (0.5, 0.5, 0, 0)
		_ObjectPos4 ("Object Position 4", Vector) = (0.5, 0.5, 0, 0)
		_ObjectPos5 ("Object Position 5", Vector) = (0.5, 0.5, 0, 0)
		_ObjectPos6 ("Object Position 6", Vector) = (0.5, 0.5, 0, 0)
		_ObjectPos7 ("Object Position 7", Vector) = (0.5, 0.5, 0, 0)
		_EdgeColor ("Edge Color", Color) = (1, 1, 0, 1)
	}
	SubShader
	{
		Tags { "RenderType"="Opaque" "Queue"="Overlay" }
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			sampler2D _MainTex;
			sampler2D _MaskTex;
			sampler2D _SDFTex;
			sampler2D _EdgeMinTex;
            float _FalloffDistance;
            float _FalloffPower;
            int _RaymarchSteps;
            float _StepSize;
            float _CoarseMultiplier;
            int _RefineSteps;
			int _ObjectCount;
			float4 _ObjectPos0, _ObjectPos1, _ObjectPos2, _ObjectPos3;
			float4 _ObjectPos4, _ObjectPos5, _ObjectPos6, _ObjectPos7;
			fixed4 _EdgeColor;

			v2f vert (appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				return o;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				fixed4 src = tex2D(_MainTex, i.uv);
				fixed4 mask = tex2D(_MaskTex, i.uv);
				
				// If this pixel is part of the target material, keep it
				if (mask.r > 0.5)
					return src;

				// Prefer origin-based edge distance if available
				float edgeMin = tex2D(_EdgeMinTex, i.uv).r;
				if (edgeMin < 1e5)
				{
					float falloffEdge = saturate(pow(edgeMin / _FalloffDistance, _FalloffPower));
					return lerp(_EdgeColor, src, falloffEdge);
					//return float4(edgeMin,edgeMin,edgeMin,1);
				}

				// Use SDF if provided (signed distance in UV units). Zero means unavailable.
				float sdf = tex2D(_SDFTex, i.uv).r;
				if (sdf != 0)
				{
					float d = abs(sdf);
					float falloffSDF = saturate(pow(d / _FalloffDistance, _FalloffPower));
					return lerp(_EdgeColor, src, falloffSDF);
				}
				
                // Fallback: no precomputed buffers available, just return source
                return src;
			}
			ENDCG
		}
	}
}
