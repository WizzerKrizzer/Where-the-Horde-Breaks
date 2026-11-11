Shader "TowerDefense/StyleFlame"
{
    // A flame drawn on one quad (FlameVisual): the quad turns to the camera but keeps the world's
    // up on screen, so the flame always rises, whatever holds it. Its shape is a teardrop eaten
    // away at the edges by scrolling noise, white-yellow in the core, orange, then red at the tips.
    // Vertex colour: r is a per-flame seed, a the strength.
    Properties
    {
        _Speed ("Flicker speed", Float) = 1.6
        _Width ("Width", Float) = 1
    }

    SubShader
    {
        // The quad is turned in the vertex shader from the object's own matrix, so batching (which
        // bakes the matrix into the vertices) would break it.
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Pass
        {
            ZWrite Off
            Cull Off
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Speed;
            float _Width;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 center = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float width = length(unity_ObjectToWorld._m00_m10_m20);
                float height = length(unity_ObjectToWorld._m01_m11_m21);
                float3 toCamera = normalize(_WorldSpaceCameraPos - center);
                if (unity_OrthoParams.w > 0.5)
                {
                    toCamera = -normalize(UNITY_MATRIX_V[2].xyz);
                }

                float3 up = float3(0, 1, 0);
                up = normalize(up - toCamera * dot(up, toCamera) + float3(0, 1e-4, 0));
                float3 right = normalize(cross(up, toCamera));
                float3 world = center + right * v.vertex.x * width * _Width + up * v.vertex.y * height;
                o.pos = UnityWorldToClipPos(world);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            float4 frag(v2f i) : SV_Target
            {
                float seed = i.color.r * 17.0;
                float t = _Time.y * _Speed;
                float y = i.uv.y;
                // Sway: the flame's line bends more towards its tip.
                float sway = (noise(float2(seed, t * 1.3)) - 0.5) * 0.35 * y * y;
                float x = (i.uv.x - 0.5) - sway;
                // Teardrop: widest a quarter of the way up, pointed at the top.
                float width = 0.42 * pow(saturate(1 - y), 0.75) * smoothstep(0.0, 0.25, y + 0.08);
                float edge = abs(x) / max(width, 1e-3);
                // Scrolling noise eats the outline and the tip into tongues.
                float n = noise(float2(i.uv.x * 4.0 + seed, y * 3.0 - t * 2.2)) * 0.6
                        + noise(float2(i.uv.x * 9.0 - seed, y * 7.0 - t * 3.7)) * 0.4;
                float body = saturate((1.0 - edge) * 2.2 + (n - 0.55) * 1.6 - y * 0.35);
                body *= smoothstep(1.0, 0.75, y + (n - 0.5) * 0.3);
                float core = saturate((1.0 - edge * 1.7) * 1.6 - y * 1.1 + (n - 0.5) * 0.6);
                float3 red = float3(0.85, 0.18, 0.04);
                float3 orange = float3(1.0, 0.52, 0.08);
                float3 yellow = float3(1.0, 0.9, 0.55);
                float3 color = lerp(red, orange, saturate(body * 1.6));
                color = lerp(color, yellow, core);
                float alpha = saturate(body * 1.4) * i.color.a;
                // Premultiplied: the core glows over what is behind it.
                return float4(color * alpha * (1.0 + core * 0.6), alpha * (0.85 - core * 0.35));
            }
            ENDCG
        }
    }
}
