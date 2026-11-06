Shader "TowerDefense/StylePreview"
{
    // Throwaway greybox for comparing silhouette styles. Not used by the game.
    Properties
    {
        _Dark ("Dark (feet)", Color) = (0.30, 0.30, 0.30, 1)
        _Light ("Light (head)", Color) = (0.75, 0.75, 0.75, 1)
        _OutlineColor ("Outline", Color) = (0.04, 0.04, 0.04, 1)
        _OutlineWidth ("Outline width (metres)", Float) = 0
        _GradientStrength ("Gradient strength", Range(0, 1)) = 1
        _Height ("Gradient height", Float) = 1.6
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
                // Grow the silhouette sideways by a constant world width.
                float scale = length(unity_ObjectToWorld._m00_m10_m20);
                float2 away = local.xz;
                float length2 = max(length(away), 0.0001);
                local.xz += away / length2 * (_OutlineWidth / max(scale, 0.0001));
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

            float4 _Dark;
            float4 _Light;
            float _GradientStrength;
            float _Height;

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 normal : TEXCOORD0;
                float height : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.height = saturate(input.vertex.y / _Height);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float light = 0.78 + saturate(dot(normalize(input.normal), normalize(float3(0.35, 0.8, 0.25)))) * 0.22;
                float4 flat = lerp(_Dark, _Light, 0.5);
                float4 shaded = lerp(_Dark, _Light, input.height);
                return lerp(flat, shaded, _GradientStrength) * light;
            }
            ENDCG
        }
    }
}
