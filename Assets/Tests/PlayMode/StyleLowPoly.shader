Shader "TowerDefense/StyleLowPoly"
{
    // Throwaway: flat-shaded low-poly units from vertex colours, a hard projected drop shadow,
    // and a stepped walk cycle in the vertex shader. Vertex colour alpha is the body part
    // (0 body, 0.25 left leg, 0.5 right leg, 0.625 left arm, 0.75 weapon arm, 1 static);
    // uv.x 1 marks skin.
    Properties
    {
        _Animate ("Animate (0 or 1)", Float) = 1
        _CycleRate ("Walk cycles per second", Float) = 1.9
        _PosesPerCycle ("Poses per cycle", Float) = 6
        _ShadowStrength ("Shadow strength", Range(0, 1)) = 0.42
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    float _Animate;
    float _CycleRate;
    float _PosesPerCycle;
    float _ShadowStrength;
    float _StyleTime;
    // normalize(-0.55, 0.8, 0.35): from the upper left. Shades the faces.
    static const float3 LightDirection = float3(-0.533, 0.775, 0.339);
    // Same direction raised to 80 degrees for the drop shadows, so they stay short and tucked
    // under each body instead of smearing across a dense crowd.
    static const float3 ShadowDirection = float3(-0.147, 0.985, 0.093);

    UNITY_INSTANCING_BUFFER_START(Props)
        UNITY_DEFINE_INSTANCED_PROP(float, _Seed)
    UNITY_INSTANCING_BUFFER_END(Props)

    struct Attributes
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float4 color : COLOR;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    float3 RotateX(float3 p, float3 pivot, float angle)
    {
        p -= pivot;
        float s = sin(angle);
        float c = cos(angle);
        return float3(p.x, p.y * c - p.z * s, p.y * s + p.z * c) + pivot;
    }

    float3 RotateY(float3 p, float angle)
    {
        float s = sin(angle);
        float c = cos(angle);
        return float3(p.x * c + p.z * s, p.y, -p.x * s + p.z * c);
    }

    // Walk pose: the cycle phase is quantised to a few poses per cycle, so the body moves
    // every frame but the limbs snap between held poses.
    void Pose(inout float3 p, inout float3 n, float part, float seed)
    {
        if (_Animate < 0.5 || part > 0.9) return;
        float phase = frac(_StyleTime * _CycleRate * (0.9 + 0.2 * frac(seed * 7.13)) + seed);
        phase = floor(phase * _PosesPerCycle) / _PosesPerCycle;
        float swing = sin(phase * 6.2831853);
        float lift = abs(cos(phase * 6.2831853));

        if (part > 0.2 && part < 0.6)
        {
            float side = part < 0.4 ? 1.0 : -1.0;
            p = RotateX(p, float3(0, 0.45, 0), side * swing * 0.7);
            n = RotateX(n, 0, side * swing * 0.7);
            p.y += max(0.0, side * swing) * 0.12;
        }
        else if (part > 0.6)
        {
            // Arms swing forward and back against the legs: the right (club) arm comes
            // forward with the left leg, the left arm with the right leg.
            float angle = part < 0.7 ? -swing * 0.75 : swing * 0.75 - 0.15;
            p = RotateX(p, float3(0, 0.95, 0), angle);
            n = RotateX(n, 0, angle);
        }

        if (part < 0.2 || part > 0.6)
        {
            // Upper body: leaning into the walk, a bob on each step, a small squash on contact.
            p.y = 0.45 + (p.y - 0.45) * (1.0 - 0.06 * (1.0 - lift));
            p.y += lift * 0.06;
            p = RotateX(p, float3(0, 0.45, 0), 0.12);
            n = RotateX(n, 0, 0.12);
        }
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "Shadow"
            // Two-sided so open shapes (flags, roofs) still cast a full shadow; the stencil keeps it single.
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Offset -1, -1
            // Overlapping shadows darken the ground once, not once per unit.
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
                float seed = frac(UNITY_ACCESS_INSTANCED_PROP(Props, _Seed));
                float3 local = input.vertex.xyz;
                float3 normal = input.normal;
                Pose(local, normal, input.color.a, seed);
                float3 world = mul(unity_ObjectToWorld, float4(local, 1.0)).xyz;
                // Project along the light onto the ground plane.
                world.xz -= ShadowDirection.xz / ShadowDirection.y * world.y;
                world.y = 0.01;
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
                float3 normal : TEXCOORD0;
                float4 color : TEXCOORD1;
                nointerpolation float2 status : TEXCOORD2;
            };

            float Hash(float x)
            {
                return frac(sin(x * 91.3458) * 47453.5453);
            }

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Integer part is the status (1 burning, 2 frozen), fraction the random seed.
                float encoded = UNITY_ACCESS_INSTANCED_PROP(Props, _Seed);
                float status = floor(encoded);
                float seed = frac(encoded);
                float3 local = input.vertex.xyz;
                float3 normal = input.normal;
                Pose(local, normal, input.color.a, seed);
                Varyings output;
                output.position = UnityObjectToClipPos(float4(local, 1.0));
                output.normal = UnityObjectToWorldNormal(normal);
                float3 color = input.color.rgb;
                if (input.uv.x > 0.5)
                {
                    // Three skin tones (olive, green, teal) so the crowd does not look copied.
                    float tone = Hash(seed + 0.37);
                    float3 tint = tone < 0.33 ? float3(1.12, 1.0, 0.62) : (tone < 0.66 ? float3(1.0, 1.0, 1.0) : float3(0.72, 1.0, 1.12));
                    color *= tint;
                }

                output.color = float4(color, 1.0);
                // Status as a flat full-silhouette colour: x burning, y frozen.
                float flicker = step(0.5, frac(_StyleTime * 9.0 + seed * 3.0));
                output.status = float2(status == 1.0 ? 0.75 + 0.25 * flicker : 0.0, status == 2.0 ? 1.0 : 0.0);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                if (input.status.x > 0.0) return float4(float3(1.0, 0.45, 0.22) * input.status.x + 0.05, 1.0);
                if (input.status.y > 0.0) return float4(0.86, 0.94, 1.0, 1.0);
                float lighting = saturate(dot(normalize(input.normal), LightDirection));
                // Three flat bands instead of a smooth gradient.
                float band = lighting > 0.72 ? 1.0 : (lighting > 0.35 ? 0.8 : 0.58);
                return float4(input.color.rgb * band, 1.0);
            }
            ENDCG
        }
    }
}
