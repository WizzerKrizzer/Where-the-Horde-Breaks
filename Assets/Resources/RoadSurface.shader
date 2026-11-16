Shader "TowerDefense/WeatheredRoad"
{
    Properties
    {
        _RoadField ("Footprint and wheel ruts", 2D) = "black" {}
        _GroundColor ("Ground", Color) = (0.3, 0.3, 0.3, 1)
        _RoadColor ("Road", Color) = (0.4, 0.4, 0.4, 1)
        _WorldRect ("World bounds", Vector) = (0,0,100,100)
        _DirtColor ("Road dirt", Color) = (0.40, 0.31, 0.22, 1)
        _DirtAmount ("Dirt pattern on the road (0 = flat greybox)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            sampler2D _RoadField;
            fixed4 _GroundColor, _RoadColor;
            float4 _WorldRect;
            fixed4 _DirtColor;
            float _DirtAmount;

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                    lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            // Trodden dirt (user, 2026-11-14: on the flat grey road the goblins seemed to glide):
            // patches of lighter and darker earth and scattered pebbles, fixed to the ground, so
            // the eye has something to see the feet move against.
            float3 Dirt(float2 p)
            {
                float patches = Noise(p * 0.55) * 0.6 + Noise(p * 1.9) * 0.4;
                // Stepped into a few flat tones, to sit with the low poly models.
                patches = floor(patches * 4.0) / 3.0;
                float3 colour = _DirtColor.rgb * lerp(0.84, 1.12, patches);
                float2 cell = floor(p * 1.6);
                float2 inside = frac(p * 1.6);
                float chance = Hash(cell + 17.0);
                float2 centre = 0.25 + 0.5 * float2(Hash(cell + 3.0), Hash(cell + 9.0));
                float radius = 0.05 + 0.09 * Hash(cell + 5.0);
                float pebble = chance > 0.72 ? step(length(inside - centre), radius) : 0.0;
                colour = lerp(colour, _DirtColor.rgb * (chance > 0.86 ? 1.45 : 0.62), pebble);
                return colour;
            }
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; SHADOW_COORDS(1) };
            v2f vert(appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                TRANSFER_SHADOW(o); return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                // Greybox ground; the road is trodden dirt.
                float2 p = i.world.xz;
                float4 field = tex2D(_RoadField, (p-_WorldRect.xy) / _WorldRect.zw);
                float distance = (field.r-.5)*8;
                float road = smoothstep(-.05, .05, distance);
                float3 roadColour = lerp(_RoadColor.rgb, Dirt(p), _DirtAmount);
                float3 colour = lerp(_GroundColor.rgb, roadColour, road);
                colour *= lerp(.65, 1.0, SHADOW_ATTENUATION(i));
                return fixed4(colour,1);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
