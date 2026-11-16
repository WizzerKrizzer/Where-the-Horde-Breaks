Shader "TowerDefense/HordeDust"
{
    // Dust a dead goblin raises (user, 2026-11-14): a few soft grey puffs where a thrown body
    // leaves the ground, and where it lands and along the way it rolls. The same soft puff as
    // the raging mammoth's smoke (MammothVisual.SoftPuff), not squares. Drawn per corpse record
    // (HordeSimulation.compute RecordCorpse), with the corpse's own path worked out again here as
    // HordeCorpse.shader does, so it costs nothing in the simulation. The mesh holds GpuHordeSimulation.DustPuffs quads (2 at the start, 5 at the landing);
    // a vertex's puff is its index / 4.
    Properties
    {
        _MainTex ("Puff", 2D) = "white" {}
        _DustColor ("Dust Color", Color) = (0.62, 0.62, 0.63, 1)
        _DustAlpha ("Dust Strength", Range(0, 1)) = 0.65
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            // Must match HordeSimulation.compute and HordeCorpse.shader.
            #define CORPSE_STRIDE 12u
            #define AIR_GRAVITY 15.7
            #define AIR_DRAG 0.6
            #define CORPSE_GROUND_DRAG 5.4
            #define LIFE 1.3

            StructuredBuffer<int> _Aux;
            uint _CorpseBase;
            float _CorpseClock;
            sampler2D _MainTex;
            float4 _DustColor;
            float _DustAlpha;

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float alpha : TEXCOORD1;
            };

            float AuxFloat(uint offset)
            {
                return asfloat(_Aux[offset]);
            }

            float Hash(float n)
            {
                return frac(sin(n) * 43758.5453);
            }

            Varyings Hidden()
            {
                Varyings output;
                output.position = float4(0.0, 0.0, -2.0, 1.0);
                output.uv = 0.0;
                output.alpha = 0.0;
                return output;
            }

            Varyings Vert(Attributes input)
            {
                uint offset = _CorpseBase + input.instanceID * CORPSE_STRIDE;
                float birth = AuxFloat(offset + 5u);
                float flight = AuxFloat(offset + 6u);
                float startHeight = AuxFloat(offset + 7u);
                float speed = AuxFloat(offset + 9u);
                float t = _CorpseClock - birth;
                // Only thrown bodies raise dust, and only for a short while.
                if (birth <= 0.0 || flight <= 0.3 || t < 0.0 || t > flight + 3.0)
                {
                    return Hidden();
                }

                float2 start = float2(AuxFloat(offset), AuxFloat(offset + 1u));
                float2 rest = float2(AuxFloat(offset + 2u), AuxFloat(offset + 3u));
                float airDecay = exp(-AIR_DRAG * flight);
                float airTravel = speed * (1.0 - airDecay) / AIR_DRAG;
                float groundTotal = speed * airDecay / CORPSE_GROUND_DRAG;
                float total = airTravel + groundTotal;
                float2 landing = total > 0.0001 ? lerp(start, rest, airTravel / total) : rest;
                bool rolls = groundTotal > 0.6;

                // Not every body raises dust: where a bomb kills dozens at once, all of them
                // together made a white sheet over the crowd (user's bomb clips, 2026-11-14).
                if (Hash((float)input.instanceID * 3.17 + 0.5) > 0.4) return Hidden();

                uint puff = input.vertexID / 4u;
                float seed = (float)input.instanceID * 7.13 + (float)puff * 3.71;
                float2 at;
                float appear;
                float grow;
                if (puff < 2u)
                {
                    // Kicked up where it left the ground (not when it was already in the air).
                    if (startHeight > 0.2) return Hidden();
                    at = start;
                    appear = 0.0;
                    grow = 0.8;
                }
                else
                {
                    // Where it lands, then along the way it rolls, each puff as it passes.
                    float f = (float)(puff - 2u) / 4.0 * (rolls ? 1.0 : 0.15);
                    at = lerp(landing, rest, f);
                    appear = flight + (f > 0.0 ? -log(max(0.02, 1.0 - f * 0.97)) / CORPSE_GROUND_DRAG : 0.0);
                    grow = puff == 2u ? 1.15 : 0.9;
                }

                float age = t - appear;
                if (age < 0.0 || age > LIFE)
                {
                    return Hidden();
                }

                float life = age / LIFE;
                float2 drift = (float2(Hash(seed), Hash(seed + 1.7)) - 0.5);
                float3 centre = float3(at.x, 0.2, at.y);
                centre.xz += drift * (0.35 + 0.5 * life);
                centre.y += 0.3 * age;
                float size = grow * lerp(0.6, 1.9, sqrt(life)) * lerp(0.85, 1.15, Hash(seed + 4.1));

                // A camera-facing quad, the puff turned its own way.
                float2 corner = input.uv - 0.5;
                float spin = Hash(seed + 9.3) * 6.2831853 + (Hash(seed + 2.2) - 0.5) * age;
                float s, c;
                sincos(spin, s, c);
                corner = float2(corner.x * c - corner.y * s, corner.x * s + corner.y * c);
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 world = centre + (right * corner.x + up * corner.y) * size;

                Varyings output;
                output.position = UnityWorldToClipPos(world);
                output.uv = input.uv;
                output.alpha = _DustAlpha * smoothstep(0.0, 0.1, age) * (1.0 - smoothstep(0.25, 1.0, life));
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed4 puff = tex2D(_MainTex, input.uv);
                return fixed4(_DustColor.rgb, puff.a * input.alpha);
            }
            ENDCG
        }
    }
}
