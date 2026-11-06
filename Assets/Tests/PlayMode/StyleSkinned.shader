Shader "TowerDefense/StyleSkinned"
{
    // Throwaway: a textured, skinned character (baked to a mesh each frame on the CPU) lit with
    // the same three-band flat shading and 80° planar drop shadow as the other style shaders.
    // The face normal comes from screen-space derivatives, so the mesh looks faceted and
    // single-sided cloth works with culling off.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _VertexColor ("Use vertex colour", Float) = 0
        _ShadowStrength ("Shadow strength", Range(0, 1)) = 0.42
        _ShadowPlane ("Shadow plane height", Float) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float _VertexColor;
    float _ShadowStrength;
    float _ShadowPlane;
    static const float3 LightDirection = float3(-0.533, 0.775, 0.339);
    static const float3 ShadowDirection = float3(-0.147, 0.985, 0.093);
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
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(float4 vertex : POSITION) : SV_POSITION
            {
                float3 world = mul(unity_ObjectToWorld, vertex).xyz;
                world.xz -= ShadowDirection.xz / ShadowDirection.y * (world.y - _ShadowPlane);
                world.y = _ShadowPlane + 0.01;
                return mul(UNITY_MATRIX_VP, float4(world, 1.0));
            }

            fixed4 Frag() : SV_Target
            {
                return float4(0.05, 0.03, 0.06, _ShadowStrength);
            }
            ENDCG
        }

        Pass
        {
            Name "Body"
            Cull Off
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                float4 color : TEXCOORD2;
            };

            Varyings Vert(float4 vertex : POSITION, float2 uv : TEXCOORD0, float4 color : COLOR)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(vertex);
                output.world = mul(unity_ObjectToWorld, vertex).xyz;
                output.uv = uv;
                output.color = color;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(cross(ddy(input.world), ddx(input.world)));
                float lighting = saturate(dot(normal, LightDirection));
                float band = lighting > 0.72 ? 1.0 : (lighting > 0.35 ? 0.8 : 0.58);
                float3 albedo = _VertexColor > 0.5 ? input.color.rgb : tex2D(_MainTex, input.uv).rgb;
                return float4(albedo * band, 1.0);
            }
            ENDCG
        }
    }
}
