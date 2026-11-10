Shader "TowerDefense/HordeCorpse"
{
    // Dead goblins. HordeSimulation.compute (RecordCorpse) writes, once per death, where the body
    // starts, where it comes to rest, and how it flies; this shader plays that back from the birth
    // time: the body arcs through the air if it was thrown (tumbling, with a shadow under it),
    // tips over, skids to a stop at the road's edge, and stays down.
    //
    // The shape is whatever mesh GpuHordeSimulation.SetCorpseMesh gives it. Today that is the
    // placeholder body, tipped over by _LieAngle and squashed by _Flatten. A real goblin model
    // can be dropped in the same way (or a lying pose of it, with _LieAngle 0 and _Flatten 1).
    Properties
    {
        _Color ("Corpse Color", Color) = (0.4, 0.4, 0.4, 1)
        _BurnedColor ("Burned Corpse Color", Color) = (0.16, 0.13, 0.11, 1)
        _PoisonedColor ("Poisoned Corpse Color", Color) = (0.27, 0.36, 0.2, 1)
        _LieAngle ("Lie Angle (degrees)", Range(0, 90)) = 90
        _Flatten ("Flatten When Lying", Range(0.05, 1)) = 0.55
        _BodyHeight ("Body Height (mesh units)", Float) = 1
        _BodyRadius ("Body Radius (mesh units)", Float) = 0.5
        _PackingScale ("Dense Packing Silhouette Scale", Range(1, 1.25)) = 1.18
        _VisualScaleMultiplier ("Visual Scale Multiplier", Range(0.1, 2)) = 1
        _ShadowColor ("Air Shadow Color", Color) = (0, 0, 0, 0.42)
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    // Must match HordeSimulation.compute.
    #define CORPSE_STRIDE 12u
    #define AIR_GRAVITY 15.7
    #define AIR_DRAG 0.6
    #define CORPSE_GROUND_DRAG 5.4

    StructuredBuffer<int> _Aux;
    uint _CorpseBase;
    float _CorpseClock;
    float _LieAngle;
    float _Flatten;
    float _BodyHeight;
    float _BodyRadius;
    float _PackingScale;
    float _VisualScaleMultiplier;

    struct CorpsePose
    {
        float3 world;
        float3 normal;
        float height;
        float valid;
        uint mark;
    };

    float AuxFloat(uint offset)
    {
        return asfloat(_Aux[offset]);
    }

    float3 RotateAxis(float3 v, float3 axis, float angle)
    {
        float s;
        float c;
        sincos(angle, s, c);
        return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
    }

    CorpsePose PoseCorpse(uint instance, float3 vertex, float3 normal)
    {
        uint offset = _CorpseBase + instance * CORPSE_STRIDE;
        float2 start = float2(AuxFloat(offset), AuxFloat(offset + 1u));
        float2 rest = float2(AuxFloat(offset + 2u), AuxFloat(offset + 3u));
        float scale = AuxFloat(offset + 4u);
        float birth = AuxFloat(offset + 5u);
        float flight = AuxFloat(offset + 6u);
        float startHeight = AuxFloat(offset + 7u);
        float rise = AuxFloat(offset + 8u);
        float speed = AuxFloat(offset + 9u);
        float heading = AuxFloat(offset + 10u);
        uint info = (uint)_Aux[offset + 11u];

        CorpsePose pose;
        pose.valid = birth > 0.0 ? 1.0 : 0.0;
        pose.mark = info >> 16;
        float t = max(0.0, _CorpseClock - birth);

        // Distance along the way the body went, with the same drag as a live thrown body: light
        // in the air, then a short skid. Played as a fraction of the stored path, so a path the
        // compute stopped at the road's edge stops there too.
        float airDecay = exp(-AIR_DRAG * flight);
        float total = speed * (1.0 - airDecay) / AIR_DRAG + speed * airDecay / CORPSE_GROUND_DRAG;
        float travelled;
        float height;
        if (t < flight)
        {
            travelled = speed * (1.0 - exp(-AIR_DRAG * t)) / AIR_DRAG;
            height = max(0.0, startHeight + rise * t - 0.5 * AIR_GRAVITY * t * t);
        }
        else
        {
            travelled = speed * (1.0 - airDecay) / AIR_DRAG +
                speed * airDecay * (1.0 - exp(-CORPSE_GROUND_DRAG * (t - flight))) / CORPSE_GROUND_DRAG;
            height = 0.0;
        }
        float along = total > 0.0001 ? saturate(travelled / total) : 1.0;
        float2 position = lerp(start, rest, along);

        // Topples the way it was going (a still body picks a side of its own); a thrown one
        // tumbles a full turn in the air and lands lying down.
        float jitter = frac(sin((float)instance * 12.9898) * 43758.5453) - 0.5;
        heading += jitter * (speed > 0.5 ? 0.6 : 6.2831853);
        float3 fallDirection = float3(cos(heading), 0.0, sin(heading));
        float3 axis = float3(fallDirection.z, 0.0, -fallDirection.x);
        float topple = flight > 0.3
            ? smoothstep(0.0, flight, t)
            : smoothstep(0.0, 0.3, t);
        float spin = flight > 0.3 ? 6.2831853 * smoothstep(0.0, flight, t) : 0.0;
        float lie = radians(_LieAngle) * topple;

        float3 local = vertex;
        local.xz *= _PackingScale;
        float3 pivot = float3(0.0, _BodyHeight * 0.5, 0.0);
        float3 turned = RotateAxis(local - pivot, axis, lie + spin);
        float squash = lerp(1.0, _Flatten, topple);
        turned.y *= squash;
        // Its middle sinks from half its height to half its width as it lies down.
        float restingMiddle = lerp(_BodyHeight * 0.5, _BodyRadius * _PackingScale * _Flatten, topple);
        // A body in the air is drawn a little larger, as live thrown goblins are.
        float airScale = 1.0 + 0.12 * height;
        float size = scale * _VisualScaleMultiplier * airScale;

        pose.world = (turned + float3(0.0, restingMiddle, 0.0)) * size + float3(position.x, height, position.y);
        pose.normal = RotateAxis(normal, axis, lie + spin);
        pose.height = height;
        return pose;
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-1" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            float4 _Color;
            float4 _BurnedColor;
            float4 _PoisonedColor;

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 normal : TEXCOORD0;
                float mark : TEXCOORD1;
                float variation : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                CorpsePose pose = PoseCorpse(input.instanceID, input.vertex.xyz, input.normal);
                float3 world = pose.world;
                world.y += (1.0 - pose.valid) * -100000.0;
                Varyings output;
                output.position = UnityWorldToClipPos(world);
                output.normal = pose.normal;
                output.mark = (float)pose.mark;
                output.variation = frac(sin((float)input.instanceID * 78.233) * 43758.5453);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                // The same soft key light as the living horde, so a corpse reads as a body.
                float light = 0.68 + saturate(dot(normalize(input.normal), normalize(float3(0.35, 0.8, 0.25)))) * 0.32;
                float4 color = input.mark > 1.5 ? _PoisonedColor : (input.mark > 0.5 ? _BurnedColor : _Color);
                return float4(color.rgb * light * lerp(0.92, 1.08, input.variation), 1.0);
            }
            ENDCG
        }

        // The shadow of a body still in the air, as live thrown goblins have (HordeAirShadow).
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Stencil
            {
                Ref 64
                ReadMask 64
                WriteMask 64
                Comp NotEqual
                Pass Replace
            }

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            static const float3 ShadowDirection = float3(-0.147, 0.985, 0.093);
            float4 _ShadowColor;

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float strength : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                CorpsePose pose = PoseCorpse(input.instanceID, input.vertex.xyz, input.normal);
                float strength = pose.valid * saturate(pose.height / 0.25);
                float3 world = pose.world;
                world.xz -= ShadowDirection.xz / ShadowDirection.y * world.y;
                world.y = 0.03;
                world.y += strength > 0.0 ? 0.0 : -100000.0;
                Varyings output;
                output.position = UnityWorldToClipPos(world);
                output.strength = strength;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                return float4(_ShadowColor.rgb, _ShadowColor.a * input.strength);
            }
            ENDCG
        }
    }
}
