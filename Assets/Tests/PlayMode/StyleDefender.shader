Shader "TowerDefense/StyleDefender"
{
    // Throwaway: tower defenders in the low-poly style with a shoot-and-wait cycle in the
    // vertex shader. Vertex colour alpha is the part (0 upper body, 0.25 left arm, 0.5 right
    // arm, 1 static), uv.x 1 marks skin, uv.y the held item (0.25 hidden while not loaded,
    // 0.5 free item turned by the pose around its hand, 0.75 both). _Seed: the integer part is
    // the defender type (0 archer, 1 slinger, 2 rock thrower, 3 spear thrower), the fraction
    // offsets the cycle so neighbours do not shoot together.
    Properties
    {
        _Period ("Seconds between shots", Float) = 2.6
        _PoseRate ("Poses per second", Float) = 12
        _ShadowStrength ("Shadow strength", Range(0, 1)) = 0.42
        _ShadowPlane ("Shadow plane height", Float) = 0
        // Right shoulder and hand pivots (the left side is mirrored) and the hip height.
        _Shoulder ("Shoulder pivot", Vector) = (0.21, 0.8, 0, 0)
        _Hand ("Hand pivot", Vector) = (0.23, 0.38, 0.04, 0)
        _HipHeight ("Hip pivot height", Float) = 0.45
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    float _Period;
    float _PoseRate;
    float _ShadowStrength;
    float _ShadowPlane;
    float _StyleTime;
    static const float3 LightDirection = float3(-0.533, 0.775, 0.339);
    // Shadows use the same direction raised to 80 degrees (see StyleLowPoly.shader).
    static const float3 ShadowDirection = float3(-0.147, 0.985, 0.093);
    // Pivots in mesh space; must match the mesh (DefenderMeshes).
    float4 _Shoulder;
    float4 _Hand;
    float _HipHeight;
    #define ShoulderL float3(-_Shoulder.x, _Shoulder.y, _Shoulder.z)
    #define ShoulderR _Shoulder.xyz
    #define HandL float3(-_Hand.x, _Hand.y, _Hand.z)
    #define HandR _Hand.xyz
    #define Hip float3(0.0, _HipHeight, 0.0)

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

    float3 RotX(float3 p, float a)
    {
        float s = sin(a);
        float c = cos(a);
        return float3(p.x, p.y * c - p.z * s, p.y * s + p.z * c);
    }

    float3 RotY(float3 p, float a)
    {
        float s = sin(a);
        float c = cos(a);
        return float3(p.x * c + p.z * s, p.y, -p.x * s + p.z * c);
    }

    float3 RotZ(float3 p, float a)
    {
        float s = sin(a);
        float c = cos(a);
        return float3(p.x * c - p.y * s, p.x * s + p.y * c, p.z);
    }

    // Arm pose: x swings sideways (positive towards +X), y swings forward and up (negative),
    // z pulls the whole arm along Z.
    float3 Arm(float3 p, float3 shoulder, float3 pose)
    {
        return RotX(RotZ(p - shoulder, pose.x), pose.y) + shoulder + float3(0, 0, pose.z);
    }

    // Keyframe step: from the value so far towards target between t0 and t1. Chained calls
    // give a piecewise linear path through the keys.
    float Key(float value, float t, float t0, float t1, float target)
    {
        return lerp(value, target, saturate((t - t0) / (t1 - t0)));
    }

    float3 Key3(float3 value, float t, float t0, float t1, float3 target)
    {
        return lerp(value, target, saturate((t - t0) / (t1 - t0)));
    }

    struct Shot
    {
        float3 left;
        float3 right;
        float lean;
        float itemLeft;
        float itemRight;
        float spin;
        float spinLeft;
        float loaded;
    };

    // One shot at the start of the cycle (t in seconds), then a wait until the next one.
    Shot Pose(float type, float t)
    {
        Shot o;
        o.lean = 0;
        o.spin = 0;
        o.spinLeft = 0;
        o.itemLeft = 0;
        o.itemRight = 0;
        if (type < 0.5)
        {
            // Archer: raise the bow, draw to the cheek, release with a snap of the hand. The
            // bow is held flat (canted) so it reads from above; at rest it points forward.
            float3 left0 = float3(0.1, -0.25, 0);
            float3 right0 = float3(-0.05, -0.1, 0);
            o.left = Key3(left0, t, 0.0, 0.12, float3(0.5, -1.55, 0));
            o.right = Key3(right0, t, 0.0, 0.12, float3(-0.45, -1.5, 0));
            o.spinLeft = Key(1.4, t, 0.0, 0.12, 0.0);
            o.right = Key3(o.right, t, 0.12, 0.42, float3(-0.45, -1.5, -0.22));
            o.lean = Key(o.lean, t, 0.12, 0.42, -0.04);
            o.right = Key3(o.right, t, 0.42, 0.48, float3(-0.1, -1.2, -0.32));
            o.lean = Key(o.lean, t, 0.42, 0.48, 0.05);
            o.left = Key3(o.left, t, 0.48, 0.95, left0);
            o.right = Key3(o.right, t, 0.48, 0.95, right0);
            o.lean = Key(o.lean, t, 0.48, 0.95, 0);
            o.spinLeft = Key(o.spinLeft, t, 0.48, 0.95, 1.4);
            o.loaded = t > 0.08 && t < 0.45 ? 1 : 0;
        }
        else if (type < 1.5)
        {
            // Slinger: arm overhead, two fast turns of the sling, release forward.
            float3 left0 = float3(0.1, -0.15, 0);
            float3 right0 = float3(0.05, -0.1, 0);
            o.right = Key3(right0, t, 0.0, 0.15, float3(-0.2, -2.9, 0));
            o.left = Key3(left0, t, 0.0, 0.15, float3(0.3, -1.2, 0));
            o.itemRight = Key(0, t, 0.0, 0.15, -1.5708);
            o.spin = (clamp(t, 0.15, 0.68) - 0.15) * 25.13;
            o.right = Key3(o.right, t, 0.68, 0.76, float3(-0.1, -1.2, 0));
            o.lean = Key(o.lean, t, 0.68, 0.76, 0.12);
            o.left = Key3(o.left, t, 0.76, 1.05, left0);
            o.right = Key3(o.right, t, 0.76, 1.05, right0);
            o.lean = Key(o.lean, t, 0.76, 1.05, 0);
            o.itemRight = Key(o.itemRight, t, 0.76, 1.05, 0);
            o.loaded = t < 0.7 || t > 1.6 ? 1 : 0;
        }
        else if (type < 2.5)
        {
            // Rock thrower: heave the rock overhead, throw with the whole body, pick up another.
            float3 hold = float3(0, -0.55, 0);
            o.right = Key3(hold, t, 0.0, 0.35, float3(0, -2.75, 0));
            o.lean = Key(0, t, 0.0, 0.35, -0.18);
            o.right = Key3(o.right, t, 0.35, 0.43, float3(0, -1.1, 0));
            o.lean = Key(o.lean, t, 0.35, 0.43, 0.25);
            o.right = Key3(o.right, t, 0.43, 0.9, float3(0, -0.3, 0));
            o.lean = Key(o.lean, t, 0.43, 0.9, 0);
            o.right = Key3(o.right, t, 1.5, 1.65, hold);
            o.left = o.right;
            o.loaded = t < 0.41 || t > 1.55 ? 1 : 0;
        }
        else
        {
            // Spear thrower: spear up over the shoulder, aim with the free hand, throw, take
            // the next spear.
            float3 left0 = float3(0.1, -0.15, 0);
            float3 right0 = float3(0.05, -0.35, 0);
            o.right = Key3(right0, t, 0.0, 0.3, float3(0.05, -3.4, 0));
            o.itemRight = Key(-1.45, t, 0.0, 0.3, -0.12);
            o.left = Key3(left0, t, 0.0, 0.3, float3(0.3, -1.45, 0));
            o.lean = Key(0, t, 0.0, 0.3, -0.12);
            o.right = Key3(o.right, t, 0.3, 0.38, float3(-0.15, -1.5, 0));
            o.lean = Key(o.lean, t, 0.3, 0.38, 0.22);
            o.right = Key3(o.right, t, 0.38, 0.9, float3(0.05, -0.6, 0));
            o.left = Key3(o.left, t, 0.38, 0.9, left0);
            o.lean = Key(o.lean, t, 0.38, 0.9, 0);
            o.right = Key3(o.right, t, 1.45, 1.6, right0);
            o.itemRight = Key(o.itemRight, t, 1.45, 1.6, -1.45);
            o.loaded = t < 0.36 || t > 1.5 ? 1 : 0;
        }

        return o;
    }

    void Animate(inout float3 p, inout float3 n, float part, float item, float encoded)
    {
        if (part > 0.9) return;
        float type = floor(encoded);
        float seed = frac(encoded);
        // The sling turns fast, so it gets more poses per second than the others.
        float rate = type > 0.5 && type < 1.5 ? 16.0 : _PoseRate;
        float t = frac(_StyleTime / _Period + seed) * _Period;
        t = floor(t * rate) / rate;
        Shot o = Pose(type, t);
        // A slow stepped sway so a waiting defender is not frozen.
        o.lean += 0.02 * sin(floor(_StyleTime * 4.0) / 4.0 * 2.0 + seed * 6.2831853);

        bool left = part > 0.1 && part < 0.4;
        bool right = part > 0.4 && part < 0.6;
        if (left || right)
        {
            float3 shoulder = left ? ShoulderL : ShoulderR;
            float3 hand = left ? HandL : HandR;
            float3 pose = left ? o.left : o.right;
            if (item > 0.4)
            {
                // Free item: turned around its hand by the pose, then carried by the hand.
                float pitch = left ? o.itemLeft : o.itemRight;
                float spin = right ? o.spin : o.spinLeft;
                p = Arm(hand, shoulder, pose) + RotY(RotX(p - hand, pitch), spin);
                n = RotY(RotX(n, pitch), spin);
            }
            else
            {
                p = Arm(p, shoulder, pose);
                n = RotX(RotZ(n, pose.x), pose.y);
            }

            bool hides = abs(item - 0.25) < 0.1 || abs(item - 0.75) < 0.1;
            if (hides && o.loaded < 0.5) p = Arm(hand, shoulder, pose);
        }

        p = RotX(p - Hip, o.lean) + Hip;
        n = RotX(n, o.lean);
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
                float3 local = input.vertex.xyz;
                float3 normal = input.normal;
                Animate(local, normal, input.color.a, input.uv.y, UNITY_ACCESS_INSTANCED_PROP(Props, _Seed));
                float3 world = mul(unity_ObjectToWorld, float4(local, 1.0)).xyz;
                // Project along the light onto the floor the defender stands on.
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
                float3 normal : TEXCOORD0;
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
                float3 local = input.vertex.xyz;
                float3 normal = input.normal;
                Animate(local, normal, input.color.a, input.uv.y, encoded);
                Varyings output;
                output.position = UnityObjectToClipPos(float4(local, 1.0));
                output.normal = UnityObjectToWorldNormal(normal);
                float3 color = input.color.rgb;
                if (input.uv.x > 0.5)
                {
                    // Three skin tones so a tower of defenders does not look copied.
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
