Shader "TowerDefense/StyleGround"
{
    // Throwaway: one flat ground colour with small, low-contrast world-space cell noise.
    Properties
    {
        _Color ("Ground", Color) = (0.4, 0.29, 0.22, 1)
        _CellSize ("Cell size (metres)", Float) = 0.7
        _Variation ("Brightness variation", Range(0, 0.3)) = 0.06
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-1" }

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _CellSize;
            float _Variation;

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 world : TEXCOORD0;
            };

            float Hash(float2 cell)
            {
                return frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
            }

            Varyings Vert(float4 vertex : POSITION)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(vertex);
                output.world = mul(unity_ObjectToWorld, vertex).xz;
                return output;
            }

            float SmoothNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(Hash(cell), Hash(cell + float2(1, 0)), f.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + float2(1, 1)), f.x), f.y);
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                // Fine cells on a rotated grid for grain, smooth patches so large areas are not
                // flat. Neither lines up with the screen, so no checkerboard.
                float2 rotated = float2(
                    input.world.x * 0.866 - input.world.y * 0.5,
                    input.world.x * 0.5 + input.world.y * 0.866);
                float fine = Hash(floor(rotated / _CellSize));
                float coarse = SmoothNoise(input.world / (_CellSize * 18.0)) * 0.6 + SmoothNoise(input.world / (_CellSize * 60.0)) * 0.4;
                float shade = 1.0 + (fine - 0.5) * _Variation * 0.5 + (coarse - 0.5) * _Variation * 1.6;
                return float4(_Color.rgb * shade, 1.0);
            }
            ENDCG
        }
    }
}
