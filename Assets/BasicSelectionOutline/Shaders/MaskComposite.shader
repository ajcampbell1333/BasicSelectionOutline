Shader "Hidden/MaskComposite"
{
	Properties
	{
		_MainTex ("Source", 2D) = "white" {}
		_MaskTex ("Mask", 2D) = "black" {}
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
				
				// Find the closest object and calculate falloff from it
				float minFalloff = 1.0; // Start with full falloff (transparent)
				
                // Check each object position
                [unroll(8)]
                for (int obj = 0; obj < _ObjectCount; obj++)
				{
					float2 objectScreenPos;
					if (obj == 0) objectScreenPos = _ObjectPos0.xy;
					else if (obj == 1) objectScreenPos = _ObjectPos1.xy;
					else if (obj == 2) objectScreenPos = _ObjectPos2.xy;
					else if (obj == 3) objectScreenPos = _ObjectPos3.xy;
					else if (obj == 4) objectScreenPos = _ObjectPos4.xy;
					else if (obj == 5) objectScreenPos = _ObjectPos5.xy;
					else if (obj == 6) objectScreenPos = _ObjectPos6.xy;
					else objectScreenPos = _ObjectPos7.xy;
					
					// Raymarch from this object center to current pixel to find silhouette edge
					float2 direction = normalize(i.uv - objectScreenPos);
                    float2 currentPos = objectScreenPos;
                    // Convert pixel step to UV units using _ScreenParams (x = width, y = height)
                    float pixelUV = 1.0 / _ScreenParams.y;
                    float baseStep = _StepSize * pixelUV;
                    float coarseStep = baseStep * _CoarseMultiplier;
                    float maxDistance = 0.5;
					
					float edgeDistance = 0.0;
					bool foundEdge = false;
					
                    // Coarse march outward until we cross mask edge, then binary refine
                    float2 prevPos = currentPos;
                    fixed4 prevMask = tex2D(_MaskTex, prevPos);
                    [loop]
                    for (int step = 0; step < 128 && step < _RaymarchSteps; step++)
                    {
                        currentPos = currentPos + direction * coarseStep;
                        float currentDist = length(currentPos - objectScreenPos);
                        if (currentDist > maxDistance) break;
                        fixed4 currentMask = tex2D(_MaskTex, currentPos);
                        // Detect crossing from inside (white) to outside (black)
                        if (prevMask.r > 0.5 && currentMask.r < 0.5)
                        {
                            // Binary refine between prevPos (inside) and currentPos (outside)
                            float2 lo = prevPos;
                            float2 hi = currentPos;
                            [unroll(8)]
                            for (int r = 0; r < 8; r++)
                            {
                                if (r >= _RefineSteps) break;
                                float2 mid = (lo + hi) * 0.5;
                                fixed4 midMask = tex2D(_MaskTex, mid);
                                if (midMask.r > 0.5) lo = mid; else hi = mid;
                            }
                            float2 edgePos = hi;
                            edgeDistance = length(edgePos - objectScreenPos);
                            foundEdge = true;
                            break;
                        }
                        prevPos = currentPos;
                        prevMask = currentMask;
                    }
					
					// Calculate falloff for this object
					float2 delta = i.uv - objectScreenPos;
					float currentDistance = length(delta);
					float distanceFromEdge = currentDistance - edgeDistance;
					float falloff = saturate(pow(distanceFromEdge / _FalloffDistance, _FalloffPower));
					
					// Keep the minimum falloff (closest to any object)
					minFalloff = min(minFalloff, falloff);
				}
				
				// Interpolate between edge color (close to any edge) and transparent (far from all edges)
				return lerp(_EdgeColor, src, minFalloff);
			}
			ENDCG
		}
	}
}
