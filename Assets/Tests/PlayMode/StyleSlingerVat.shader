Shader "TowerDefense/StyleSlingerVat"
{
    // Throwaway: the rigged slinger played from a vertex animation texture. Each texel row is
    // one frame, each column one vertex position in mesh space (baked in Blender-free Python from
    // a skeleton and a simulated sling). uv1 holds the vertex's own column and the other two
    // corners of its triangle, so the face normal is rebuilt every frame for flat shading.
    // Frames are blended, so the motion is smooth at any frame rate. uv0.x 1 marks skin.
    // _Seed: the fraction offsets the cycle (same encoding as StyleDefender).
    Properties
    {
        _VatTex ("Vertex positions", 2D) = "black" {}
        _Period ("Seconds per cycle", Float) = 2.6
        _ShadowStrength ("Shadow strength", Range(0, 1)) = 0.42
        _ShadowPlane ("Shadow plane height", Float) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _VatTex;
    float4 _VatTex_TexelSize;
    float _Period;
    float _ShadowStrength;
    float _ShadowPlane;
    float _StyleTime;
    static const float3 LightDirection = float3(-0.533, 0.775, 0.339);
    static const float3 ShadowDirection = float3(-0.147, 0.985, 0.093);

    UNITY_INSTANCING_BUFFER_START(Props)
        UNITY_DEFINE_INSTANCED_PROP(float, _Seed)
    UNITY_INSTANCING_BUFFER_END(Props)

    struct Attributes
    {
        float4 vertex : POSITION;
        float4 color : COLOR;
        float2 uv : TEXCOORD0;
        float3 corners : TEXCOORD1;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    float3 Fetch(float column, float row)
    {
        float2 uv = (float2(column, row) + 0.5) * _VatTex_TexelSize.xy;
        return tex2Dlod(_VatTex, float4(uv, 0, 0)).xyz;
    }

    // Position of a vertex column at the current time, blended between the two nearest frames.
    void Frames(float encoded, out float row0, out float row1, out float blend)
    {
        float frames = _VatTex_TexelSize.w;
        float f = frac(_StyleTime / _Period + frac(encoded)) * frames;
        row0 = floor(f);
        row1 = fmod(row0 + 1.0, frames);
        blend = f - row0;
    }

    float3 At(float column, float row0, float row1, float blend)
    {
        return lerp(Fetch(column, row0), Fetch(column, row1), blend);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "Shadow"
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Offset -1, -1
            Stencil { Ref 1 Comp NotEqual Pass Replace }
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 position : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float row0, row1, blend;
                Frames(UNITY_ACCESS_INSTANCED_PROP(Props, _Seed), row0, row1, blend);
                float3 local = At(input.corners.x, row0, row1, blend);
                float3 world = mul(unity_ObjectToWorld, float4(local, 1.0)).xyz;
                world.xz -= ShadowDirection.xz / ShadowDirection.y * (world.y - _ShadowPlane);
                world.y = _ShadowPlane + 0.01;
                Varyings output;
                output.position = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                return float4(0.05, 0.03, 0.06, _ShadowStrength);
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

            struct Varyings
            {
                float4 position : SV_POSITION;
                nointerpolation float3 normal : TEXCOORD0;
                float4 color : TEXCOORD1;
            };

            float Hash(float x)
            {
                return frac(sin(x * 91.3458) * 47453.5453);
            }

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float encoded = UNITY_ACCESS_INSTANCED_PROP(Props, _Seed);
                float row0, row1, blend;
                Frames(encoded, row0, row1, blend);
                float3 a = At(input.corners.x, row0, row1, blend);
                float3 b = At(input.corners.y, row0, row1, blend);
                float3 c = At(input.corners.z, row0, row1, blend);
                float3 normal = normalize(cross(b - a, c - a) + 1e-7);
                Varyings output;
                output.position = UnityObjectToClipPos(float4(a, 1.0));
                output.normal = UnityObjectToWorldNormal(normal);
                float3 color = input.color.rgb;
                if (input.uv.x > 0.5)
                {
                    float tone = Hash(frac(encoded) + 0.37);
                    color *= tone < 0.33 ? float3(1.1, 1.04, 0.96) : (tone < 0.66 ? float3(1, 1, 1) : float3(0.72, 0.66, 0.62));
                }

                output.color = float4(color, 1.0);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float lighting = saturate(dot(normalize(input.normal), LightDirection));
                float band = lighting > 0.72 ? 1.0 : (lighting > 0.35 ? 0.8 : 0.58);
                return float4(input.color.rgb * band, 1.0);
            }
            ENDCG
        }
    }
}
