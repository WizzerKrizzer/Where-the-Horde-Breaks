Shader "TowerDefense/StyleLowPolyFlame"
{
    // A low-poly flame (user, 2026-11-12: "low poly, the fire too"): a faceted teardrop
    // (LowPolyShapes.Flame), each facet one flat colour by its height, yellow at the root, orange,
    // red at the tip, its facets lit by how they face the camera. Its corners keep moving, so the
    // facets shift and the flame licks and stretches. It stands upright in the world whatever holds
    // it: its width comes from the object's x scale, its height from its y scale, its turn is ignored.
    // Vertex colour r: the facet's height (0 root, 1 tip).
    Properties
    {
        _Heat ("Heat (1 fresh, 0 dying red)", Float) = 1
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        // The flame is placed in the vertex shader from the object's own matrix, so batching (which
        // bakes the matrix into the vertices) would break it.
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "DisableBatching"="True" }
        Pass
        {
            Cull Off
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Heat;
            float _Seed;
            // Metres the tip bends over per metre of height (a moving fire trails behind it).
            float3 _Lean;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float height : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 center = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float width = length(unity_ObjectToWorld._m00_m10_m20);
                float height = length(unity_ObjectToWorld._m01_m11_m21);
                float t = _Time.y;
                float3 p = v.vertex.xyz;
                float h = saturate(p.y);
                // Each corner sways on its own beat, more towards the tip; the whole flame stretches.
                float3 wobble;
                wobble.x = sin(t * 6.3 + _Seed + p.y * 5.0 + p.z * 3.0);
                wobble.z = cos(t * 5.1 + _Seed * 1.3 + p.y * 4.0 + p.x * 3.0);
                wobble.y = sin(t * 8.2 + _Seed * 2.1 + p.x * 4.0) * 0.5;
                p += wobble * 0.13 * (0.15 + h);
                p.y *= 0.86 + 0.14 * sin(t * 9.0 + _Seed * 3.0);
                float3 world = center + float3(p.x * width, p.y * height, p.z * width);
                world += _Lean * p.y * p.y * height;
                o.pos = UnityWorldToClipPos(world);
                o.world = world;
                o.height = v.color.r;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 normal = normalize(cross(ddy(i.world), ddx(i.world)));
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float facing = abs(dot(normal, view));
                float band = facing > 0.75 ? 1.12 : (facing > 0.45 ? 0.96 : 0.8);
                float3 yellow = float3(1.0, 0.9, 0.42);
                float3 orange = float3(1.0, 0.52, 0.1);
                float3 red = float3(0.86, 0.2, 0.05);
                float3 ember = float3(0.45, 0.08, 0.03);
                // A dying flame burns redder from the root up.
                float h = saturate(i.height + (1.0 - _Heat) * 0.6);
                float3 color = h < 0.3 ? yellow : (h < 0.62 ? orange : red);
                color = lerp(ember, color, saturate(_Heat * 1.5));
                return float4(color * band, 1);
            }
            ENDCG
        }
    }
}
