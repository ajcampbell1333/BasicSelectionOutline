Shader "Hidden/MaskComposite"
{
	Properties
	{
		_MainTex ("Source", 2D) = "white" {}
		_MaskTex ("Mask", 2D) = "black" {}
		_FalloffDistance ("Falloff Distance", Float) = 0.1
		_FalloffPower ("Falloff Power", Float) = 2.0
		_ObjectScreenPos ("Object Screen Position", Vector) = (0.5, 0.5, 0, 0)
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
			float _FalloffDistance;
			float _FalloffPower;
			float4 _ObjectScreenPos;
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
				
				// Raymarch from object center to current pixel to find silhouette edge
				float2 objectScreenPos = _ObjectScreenPos.xy;
				float2 direction = normalize(i.uv - objectScreenPos);
				
				// Start from object center and march outward
				float2 currentPos = objectScreenPos;
				float stepSize = 0.002; // Balanced step size for smooth edges
				float maxDistance = 0.5; // Reduced max distance
				
				float edgeDistance = 0.0;
				bool foundEdge = false;
				
				[unroll(100)]
				for (int step = 0; step < 100; step++) // Max 100 steps
				{
					currentPos += direction * stepSize;
					
					// Check if we've gone too far (use simple distance check)
					float currentDist = length(currentPos - objectScreenPos);
					if (currentDist > maxDistance)
						break;
					
					// Sample the mask at current position
					fixed4 currentMask = tex2D(_MaskTex, currentPos);
					
					// If we've moved from inside (white) to outside (black), we found the edge
					if (currentMask.r < 0.5)
					{
						edgeDistance = currentDist;
						foundEdge = true;
						break;
					}
				}
				
				// Calculate distance from edge
				float2 delta = i.uv - objectScreenPos;
				float currentDistance = length(delta);
				float distanceFromEdge = currentDistance - edgeDistance;
				
				// Apply falloff based on distance from edge
				float falloff = saturate(pow(distanceFromEdge / _FalloffDistance, _FalloffPower));
				
				// Interpolate between edge color (close to edge) and transparent (far from edge)
				return lerp(_EdgeColor, src, falloff);
			}
			ENDCG
		}
	}
}
