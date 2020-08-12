Shader "Unlit/NewUnlitShader"
{
    Properties
    {
        _Intensity("Intensity", Range(0.5, 5.0)) = 1.2
        _MatrixX("Matrix X", Range(0,512)) = 64
        _MatrixY("Matrix Y", Range(0,512)) = 64
        _Offset("Offset", Vector) = (0,0,0)
    }
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half _Intensity;
            float4 _Offset;
            int _MatrixX, _MatrixY;
            uniform StructuredBuffer<float3> colors;


            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            half2 get_uv(half2 p) {
                // half3 local = localize(p);
                return (p + _Offset.xy);
            }


            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half2 uv = get_uv(i.uv);
                int index =  round((_MatrixX - 1) * uv.x) * _MatrixY + round((_MatrixY - 1) * uv.y);
                half3 col = colors[index] * _Intensity;                
                return half4(col, 1);
            }


            ENDCG
        }
    }
}