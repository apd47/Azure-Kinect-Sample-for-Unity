Shader "VolumeRendering/VolumeRenderingBuffer"
{
	Properties
	{
		_Volume("Volume", 3D) = "" {}
		_Intensity("Intensity", Range(1.0, 5.0)) = 1.2
		_BlackCutoff("Black Cutoff", Range(0.0,1.0)) = 0.02
		_RaymarchAlpha("Raymarch Alpha", Range(0.0,1.0)) = 0.5
		_StepRatio("Step Ratio", Range(0,3)) = 2
		_MatrixX("Matrix X", Range(0,512)) = 64
		_MatrixY("Matrix Y", Range(0,512)) = 64
		_MatrixZ("Matrix Z", Range(0,512)) = 64
	}

		CGINCLUDE
			half _Intensity, _Threshold, _BlackCutoff, _RaymarchAlpha;
		half _StepRatio;
		int _MatrixX, _MatrixY, _MatrixZ;
		uniform StructuredBuffer<float4> colors;

		struct Ray {
			half3 origin;
			half3 dir;
		};

		struct AABB {
			half3 min;
			half3 max;
		};

		bool intersect(Ray r, AABB aabb, out half t0, out half t1)
		{
			half3 invR = 1.0 / r.dir;
			half3 tbot = invR * (aabb.min - r.origin);
			half3 ttop = invR * (aabb.max - r.origin);
			half3 tmin = min(ttop, tbot);
			half3 tmax = max(ttop, tbot);
			half2 t = max(tmin.xx, tmin.yz);
			t0 = max(t.x, t.y);
			t = min(tmax.xx, tmax.yz);
			t1 = min(t.x, t.y);
			return t0 <= t1;
		}

		half3 localize(half3 p) {
			return mul(unity_WorldToObject, half4(p, 1)).xyz;
		}

		bool outside(half3 uv) {
			const half EPSILON = 0.01;
			half lower = -EPSILON;
			half upper = 1 + EPSILON;
			return (
				uv.x < lower || uv.y < lower || uv.z < lower ||
				uv.x > upper || uv.y > upper || uv.z > upper
				);
		}

		struct appdata
		{
			half4 vertex : POSITION;
			half2 uv : TEXCOORD0;
		};

		struct v2f
		{
			half4 vertex : SV_POSITION;
			half2 uv : TEXCOORD0;
			half3 world : TEXCOORD1;
		};

		v2f vert(appdata v)
		{
			v2f o;
			o.vertex = UnityObjectToClipPos(v.vertex);
			o.uv = v.uv;
			o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
			return o;
		}

		ENDCG

			SubShader{

				Cull Off
				//Lighting Off
				//ZWrite Off
				//ColorMask RGB
				AlphaTest Greater .01
				Blend SrcAlpha OneMinusSrcAlpha
				ColorMaterial AmbientAndDiffuse

				Tags
				{
					"Queue" = "Transparent"
					"IgnoreProjector" = "True"
					"RenderType" = "Transparent"
				}

				Pass
				{
					CGPROGRAM
					#pragma vertex vert
					#pragma fragment frag

					uniform StructuredBuffer<float4> _Buffer;

					fixed4 frag(v2f i) : SV_Target
					{
						Ray ray;
						ray.origin = localize(i.world);

						// world space direction to object space
						half3 dir = normalize(i.world - _WorldSpaceCameraPos);
						ray.dir = normalize(mul((half3x3)unity_WorldToObject, dir));

						AABB aabb;
						aabb.min = half3(-0.5, -0.5, -0.5);
						aabb.max = half3(0.5,  0.5,  0.5);

						half tnear;
						half tfar;
						intersect(ray, aabb, tnear, tfar);

						tnear = max(0.0, tnear);

						// half3 start = ray.origin + ray.dir * tnear;
						half _MaxSteps = (_MatrixX + _MatrixY + _MatrixZ) / _StepRatio;
						half step_size = abs(tfar - tnear) / _MaxSteps;
						half3 ds = normalize(ray.origin + ray.dir * tfar - ray.origin) * step_size;
						if (length(ds) == 0) {
							return 0;
						}
						half voxelAlpha = 0;
						half4 volumeValue = 0;

						half4 finalColor = half4(0, 0, 0, 0);
						half3 p = ray.origin + ray.dir * tfar;
						half3 uv = 0;
						int index = 0;

						for (int iter = 0; iter < _MaxSteps; iter++) {
							uv = p + 0.5;
							index = round((_MatrixZ - 1) * uv.z) * _MatrixX * _MatrixY + round((_MatrixX - 1) * uv.x) * _MatrixY + round((_MatrixY - 1) * uv.y);
							volumeValue = colors[index] * _Intensity;
							voxelAlpha = _RaymarchAlpha * volumeValue.a;
							finalColor = finalColor * (1.0f - voxelAlpha) + (volumeValue.rgba * voxelAlpha);
							p -= ds;
						}
						if (length(finalColor) < _BlackCutoff) {
							return 0;
						}

						//return half4(finalColor);

						return half4(finalColor.rgb, 1);
					}

					ENDCG
				}
		}
}
