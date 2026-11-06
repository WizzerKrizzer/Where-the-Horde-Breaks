Shader "TowerDefense/StyleToon"
{
    // Throwaway: banded (toon) lighting from vertex colours plus an inverted-hull outline.
    Properties
    {
        _OutlineColor ("Outline", Color) = (0.12, 0.23, 0.07, 1)
        _OutlineWidth ("Outline width (metres)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "Outline"
            Cull Front
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _OutlineColor;
            float _OutlineWidth;

            struct Attributes
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                float3 local = input.vertex.xyz;
                float scale = length(unity_ObjectToWorld._m00_m10_m20);
                float2 away = local.xz;
                float distanceFromAxis = max(length(away), 0.0001);
                local.xz += away / distanceFromAxis * (_OutlineWidth / max(scale, 0.0001));
                output.position = UnityObjectToClipPos(float4(local, 1.0));
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }

        Pass
        {
            Name "Body"
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 normal : TEXCOORD0;
                float4 color : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.color = input.color;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float lighting = saturate(dot(normalize(input.normal), normalize(float3(-0.45, 0.85, 0.3))));
                // Three flat bands instead of a smooth gradient.
                float band = lighting > 0.78 ? 1.0 : (lighting > 0.45 ? 0.82 : 0.6);
                return float4(input.color.rgb * band, 1.0);
            }
            ENDCG
        }
    }
}
