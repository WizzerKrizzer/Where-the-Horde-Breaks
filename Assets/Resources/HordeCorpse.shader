Shader "TowerDefense/HordeCorpse"
{
    // Dead goblins. HordeSimulation.compute (RecordCorpse) writes, once per death, where the body
    // starts, where it comes to rest, and how it flies; this shader plays that back from the birth
    // time: the body arcs through the air if it was thrown (tumbling, with a shadow under it),
    // tips over, skids to a stop at the road's edge, and stays down.
    //
    // The shape is whatever mesh GpuHordeSimulation.SetCorpseMesh gives it: the placeholder body,
    // tipped over by _LieAngle and squashed by _Flatten, or the goblin model (_ModelEnabled), which
    // goes from standing to one of its four baked ways of lying dead, drawn like a living goblin: its own
    // texture, flat facets, a dark outline and a shadow (user, 2026-11-14).
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
        _MainTex ("Goblin Texture", 2D) = "white" {}
        _ModelEnabled ("Draw The Goblin Model", Float) = 0
        _WalkPoses ("Walk Poses A Cycle", Float) = 6
        _OutlineWidth ("Outline Width, Metres", Float) = 0
        _OutlineColor ("Outline Color", Color) = (0.05, 0.08, 0.04, 1)
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "HordeGoblinLooks.cginc"

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
    // The goblin model (GoblinModel): it falls on its back, away from what killed it, in its own
    // colours, and drops what it held.
    float _ModelEnabled;
    sampler2D _MainTex;
    // Rows of GoblinModel's pose texture: the walk, standing, the blow (two), then four ways of
    // lying dead, each its positions and its normals.
    Texture2D<float4> _PoseTex;
    float _WalkPoses;
    float _OutlineWidth;
    float4 _OutlineColor;
    // Corpse culling (GpuHordeSimulation, optimization 2026-11-16): instance n draws the slot at
    // _CorpseVisible[_CorpseVisibleOffset + n]; without it (the brutes' small ring) instance n is slot n.
    StructuredBuffer<uint> _CorpseVisible;
    uint _CorpseVisibleOffset;
    float _CorpseCulled;

    uint CorpseSlot(uint instanceID)
    {
        if (_CorpseCulled > 0.5)
        {
            return _CorpseVisible[_CorpseVisibleOffset + instanceID];
        }

        return instanceID;
    }

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

    // The goblin a corpse was (the low 16 bits of its number), for what it wore.
    uint CorpseAgent(uint instance)
    {
        return (uint)_Aux[_CorpseBase + instance * CORPSE_STRIDE + 11u] & 0xFFFFu;
    }

    // What a dead body does not show (what it held, hair it did not wear): one point, nothing posed.
    bool CorpseHides(uint slot, float2 held)
    {
        return _ModelEnabled > 0.5 && (held.x > 0.5 || !GoblinWears(CorpseAgent(slot), held.y));
    }

    CorpsePose PoseCorpse(uint instance, float3 vertex, float3 normal, uint vertexID, float outline)
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
        // A body thrown far lands still moving and may roll over along the ground once or twice
        // before it stops (user, 2026-11-14): whole turns, about one for every metre, at most two.
        float airTravel = speed * (1.0 - airDecay) / AIR_DRAG;
        float groundTotal = total - airTravel;
        float rolls = flight > 0.3 && groundTotal > 0.6 ? min(2.0, round(groundTotal / 1.0)) : 0.0;
        float rolled = t > flight && groundTotal > 0.0001 ? saturate((travelled - airTravel) / groundTotal) : 0.0;
        float2 position = lerp(start, rest, along);

        // Topples the way it was going (a still body picks a side of its own); a thrown one
        // tumbles a full turn in the air and lands lying down.
        float jitter = frac(sin((float)instance * 12.9898) * 43758.5453) - 0.5;
        heading += jitter * (speed > 0.5 ? 0.6 : 6.2831853);
        // No two die alike (user, 2026-11-14): each body has its own way of lying, a slower or
        // quicker fall a moment later or sooner, and, thrown, its own number of turns about its own
        // axis and a twist about the upright.
        float pick = frac(sin((float)instance * 78.233) * 43758.5453);
        float pace = frac(sin((float)instance * 39.3468) * 43758.5453);
        float tumble = frac(sin((float)instance * 93.9898) * 43758.5453);
        float twist = frac(sin((float)instance * 17.1717) * 43758.5453) - 0.5;
        uint way = (uint)min(floor(pick * 4.0), 3.0);
        float3 fallDirection = float3(cos(heading), 0.0, sin(heading));
        float3 axis = float3(fallDirection.z, 0.0, -fallDirection.x);
        bool thrown = flight > 0.3;
        float fallTime = lerp(0.22, 0.65, pace);
        float fallDelay = (1.0 - pace) * 0.12;
        float topple = thrown
            ? smoothstep(0.0, flight, t)
            : smoothstep(fallDelay, fallDelay + fallTime, t);
        float spin = 0.0;
        if (thrown && _ModelEnabled > 0.5)
        {
            // Most do not turn over at all, they only drift lying down as they fly; a few long
            // flights make one whole turn (user, 2026-11-14: many in step looked fake). Either way
            // round, about an axis up to 60 degrees off square to the throw.
            float turns = (tumble < 0.25 && flight > 0.6 ? 1.0 : 0.0) * (frac(tumble * 7.31) < 0.5 ? -1.0 : 1.0);
            float off = (frac(tumble * 13.7) - 0.5) * 2.1;
            float so, co;
            sincos(off, so, co);
            axis = float3(axis.x * co - axis.z * so, 0.0, axis.x * so + axis.z * co);
            spin = 6.2831853 * turns * smoothstep(0.0, flight, t);
        }
        else if (thrown)
        {
            spin = tumble < 0.25 && flight > 0.6 ? 6.2831853 * smoothstep(0.0, flight, t) : 0.0;
        }
        float lie = radians(_LieAngle) * topple;

        float3 local = vertex;
        if (_ModelEnabled > 0.5)
        {
            // From standing to lying on its back, head the way it falls, spinning about its middle
            // while it flies.
            uint still = (uint)_WalkPoses;
            uint row = still + 3u + way * 2u;
            float3 standing = _PoseTex.Load(int3(vertexID, still, 0)).xyz;
            float3 lying = _PoseTex.Load(int3(vertexID, row, 0)).xyz;
            float3 lyingNormal = _PoseTex.Load(int3(vertexID, row + 1u, 0)).xyz;
            local = lerp(standing, lying, topple);
            float3 n = normalize(lerp(normal, lyingNormal, topple));
            local += n * outline / max(scale, 0.01);
            // Face down falls head first, on its back head last; curled and sprawled any way. A
            // thrown body also twists about the upright as it flies.
            float yaw = (way == 1u ? 3.14159265 : (way == 2u ? 1.5707963 : 0.0)) + twist * 1.4;
            if (rolls > 0.0)
            {
                // A roller lies across the way it goes, so it rolls over like a log.
                yaw = (way == 2u ? 0.0 : 1.5707963) + twist * 0.4;
            }
            else if (thrown)
            {
                yaw += twist * 1.2 * smoothstep(0.0, flight, t);
            }
            float sy, cy;
            sincos(yaw, sy, cy);
            float2 forward = -float2(fallDirection.x * cy - fallDirection.z * sy, fallDirection.x * sy + fallDirection.z * cy);
            local = float3(local.x * forward.y + local.z * forward.x, local.y, -local.x * forward.x + local.z * forward.y);
            n = float3(n.x * forward.y + n.z * forward.x, n.y, -n.x * forward.x + n.z * forward.y);
            float3 middle = float3(0.0, lerp(_BodyHeight * 0.45, 0.4, topple), 0.0);
            float3 spun = RotateAxis(local - middle, axis, spin) + middle;
            float3 rollAxis = float3(fallDirection.z, 0.0, -fallDirection.x);
            float roll = -6.2831853 * rolls * rolled;
            float3 rollMiddle = float3(0.0, 0.4, 0.0);
            spun = RotateAxis(spun - rollMiddle, rollAxis, roll) + rollMiddle;
            float modelSize = scale * _VisualScaleMultiplier * (1.0 + 0.05 * height);
            pose.world = spun * modelSize + float3(position.x, height, position.y);
            pose.normal = RotateAxis(RotateAxis(n, axis, spin), rollAxis, roll);
            pose.height = height;
            return pose;
        }
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
                float2 uv : TEXCOORD0;
                float2 held : TEXCOORD1;
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 normal : TEXCOORD0;
                float mark : TEXCOORD1;
                float variation : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float3 world : TEXCOORD4;
                float4 paint : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                uint slot = CorpseSlot(input.instanceID);
                if (CorpseHides(slot, input.held))
                {
                    output = (Varyings)0;
                    output.position = float4(0.0, 0.0, -1.0, 1.0);
                    return output;
                }

                CorpsePose pose = PoseCorpse(slot, input.vertex.xyz, input.normal, input.vertexID, 0.0);
                float3 world = pose.world;
                world.y += (1.0 - pose.valid) * -100000.0;
                uint agent = CorpseAgent(slot);
                output.uv = input.uv;
                output.world = world;
                output.position = UnityWorldToClipPos(world);
                output.normal = pose.normal;
                output.mark = (float)pose.mark;
                output.variation = frac(sin((float)slot * 78.233) * 43758.5453);
                output.paint = input.held.x < 0.5 && input.held.y < 0.5 ? GoblinPaint(agent, input.vertex.xyz) : 0.0;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                // The same soft key light as the living horde, so a corpse reads as a body.
                float light = 0.68 + saturate(dot(normalize(input.normal), normalize(float3(0.35, 0.8, 0.25)))) * 0.32;
                if (_ModelEnabled > 0.5)
                {
                    // Lit like a living goblin (flat facets), its colours a little dulled.
                    float3 facet = normalize(cross(ddy(input.world), ddx(input.world)));
                    if (dot(facet, _WorldSpaceCameraPos - input.world) < 0.0) facet = -facet;
                    float facetLight = 0.46 + 0.62 * saturate(dot(facet, normalize(float3(0.35, 0.8, 0.25))));
                    float3 skin = tex2D(_MainTex, input.uv).rgb;
                    float green = saturate((skin.g - max(skin.r, skin.b)) * 8.0);
                    skin = lerp(skin, input.paint.rgb, saturate(input.paint.a) * green * 0.9);
                    skin = lerp(skin, dot(skin, float3(0.3, 0.59, 0.11)).xxx, 0.25) * 0.88;
                    if (input.mark > 1.5) skin = lerp(skin, _PoisonedColor.rgb, 0.5);
                    else if (input.mark > 0.5) skin = lerp(skin, _BurnedColor.rgb, 0.75);
                    return float4(skin * facetLight * lerp(0.94, 1.06, input.variation), 1.0);
                }

                float4 color = input.mark > 1.5 ? _PoisonedColor : (input.mark > 0.5 ? _BurnedColor : _Color);
                return float4(color.rgb * light * lerp(0.92, 1.08, input.variation), 1.0);
            }
            ENDCG
        }

        // The goblin model's outline, as on the living (HordeIndirect): the body again, a little
        // larger, inside out and dark.
        Pass
        {
            Name "Outline"
            Cull Front
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex VertOutline
            #pragma fragment FragOutline

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 held : TEXCOORD1;
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };


            // The outline's width at a place: the full width from _OutlineFullAt metres from the
            // camera out, thinner closer in (down to a quarter), so up close it stays a thin edge
            // instead of a thick dark hull showing through the face's hollows (user, 2026-11-15:
            // faces looked smeared up close). Not capped in pixels: that thinned it at the usual
            // 11 m too, more the lower the screen's resolution (user, 2026-11-15: the crowd lost its
            // outline).
            float _OutlineFullAt;
            float OutlineWidthAt(float3 position, float width)
            {
                if (_OutlineFullAt <= 0.0 || unity_OrthoParams.w > 0.5)
                {
                    return width;
                }

                return width * clamp(distance(position, _WorldSpaceCameraPos) / _OutlineFullAt, 0.25, 1.0);
            }

            float4 VertOutline(Attributes input) : SV_POSITION
            {
                uint corpse = CorpseSlot(input.instanceID);
                if (_ModelEnabled < 0.5 || _OutlineWidth <= 0.0 || CorpseHides(corpse, input.held))
                {
                    return float4(0.0, 0.0, -1.0, 1.0);
                }

                uint slot = _CorpseBase + corpse * CORPSE_STRIDE;
                float3 lying = float3(AuxFloat(slot + 2u), 0.3, AuxFloat(slot + 3u));
                CorpsePose pose = PoseCorpse(corpse, input.vertex.xyz, input.normal, input.vertexID,
                    OutlineWidthAt(lying, _OutlineWidth));
                float3 world = pose.world;
                world.y += (1.0 - pose.valid) * -100000.0;
                return UnityWorldToClipPos(world);
            }

            fixed4 FragOutline() : SV_Target
            {
                return _OutlineColor;
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
                float2 held : TEXCOORD1;
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float strength : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                uint slot = CorpseSlot(input.instanceID);
                if (CorpseHides(slot, input.held))
                {
                    output.position = float4(0.0, 0.0, -1.0, 1.0);
                    output.strength = 0.0;
                    return output;
                }

                CorpsePose pose = PoseCorpse(slot, input.vertex.xyz, input.normal, input.vertexID, 0.0);
                // The goblin model always has a shadow, lying down too; the placeholder only in the air.
                float strength = pose.valid * (_ModelEnabled > 0.5 ? 1.0 : saturate(pose.height / 0.25));
                float3 world = pose.world;
                world.xz -= ShadowDirection.xz / ShadowDirection.y * world.y;
                world.y = 0.03;
                world.y += strength > 0.0 ? 0.0 : -100000.0;
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
