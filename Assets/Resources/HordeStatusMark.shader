Shader "TowerDefense/HordeStatusMark"
{
    // Little stars circling above a stunned goblin (the slingers' stunning stone, the rock's
    // quake). The simulation lists the first stunned goblins of each step (HordeSimulation.compute,
    // MAX_STUN_STARS); one small ring is drawn per list entry, up to the effects setting's cap,
    // so nothing is read back to the CPU and the horde's size does not matter.
    // A stun is the GPU's near-total slow (HordeEnemyManager.StunSpeedMultiplier, 5%).
    Properties
    {
        _StarColor ("Star Color", Color) = (1, 0.9, 0.45, 1)
        _HeadHeight ("Head Height (mesh units)", Float) = 2
        _RingRadius ("Ring Radius (mesh units)", Float) = 1.25
        _SpinSpeed ("Spin (radians per second)", Float) = 4
        _VisualScaleMultiplier ("Visual Scale Multiplier", Range(0.1, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+30" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Off
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            #include "HordeMounds.cginc"

            struct AgentState
            {
                float2 position;
                float2 velocity;
                float2 knockbackVelocity;
                float scale;
                float tint;
                uint status;
                float padding;
                float health;
                float progress;
                uint flags;
                float padding2;
                float slowMultiplier;
                float slowTimer;
                float burnDamagePerSecond;
                float burnTimer;
                float mass;
                float shockwaveExposure;
                float2 shockwaveCenter;
                float shockwaveOutwardTimer;
                float shockwaveReturnTimer;
                float maxHealth;
                float armor;
                float physicalResistance;
                float fireResistance;
                float slowResistance;
                float attackDamage;
                float attackInterval;
                float wallDamageMultiplier;
                float alliedDamageMultiplier;
                float attackTimer;
                uint combatFlags;
                uint definitionIndex;
                float animationDistance;
                float2 facing;
                float2 stepStartPosition;
                float locomotionSpeed;
                float presentationTime;
                float airHeight;
                float airSpeed;
                float landingTimer;
            };

            // Must stay at or below HordeEnemyManager.StunSpeedMultiplier plus a margin; ordinary
            // slows (tar, frost) are far weaker and get no stars.
            #define STUN_SLOW_LIMIT 0.06

            StructuredBuffer<AgentState> _AgentStates;
            uint _AgentCapacity;
            // Stunned goblins counted by the last simulation step (HordeSimulation.compute), and
            // how many may show stars at once (EffectsQuality): past that, a random share does.
            StructuredBuffer<int> _Aux;
            uint _StunCountBase;
            uint _StunCountSlot;
            float _StarCap;
            float4 _StarColor;
            float _HeadHeight;
            float _RingRadius;
            float _SpinSpeed;
            float _VisualScaleMultiplier;
            float _PresentationAlpha;

            struct Attributes
            {
                float4 vertex : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float glow : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                uint listed = (uint)max(0, _Aux[_StunCountBase + _StunCountSlot]);
                uint shown = min(min(listed, 64u), (uint)max(0.0, _StarCap));
                uint index = (uint)_Aux[_StunCountBase + 3u + _StunCountSlot * 64u + min(input.instanceID, 63u)];
                index = min(index, max(1u, _AgentCapacity) - 1u);
                AgentState state = _AgentStates[index];
                bool stunned = input.instanceID < shown && state.status == 1u && state.slowTimer > 0.0 &&
                    state.slowMultiplier <= STUN_SLOW_LIMIT;

                float phase = frac(sin((float)index * 12.9898) * 43758.5453) * 6.2831853;
                float angle = _Time.y * _SpinSpeed + phase;
                float s;
                float c;
                sincos(angle, s, c);
                float3 local = input.vertex.xyz;
                float3 turned = float3(local.x * c - local.z * s, 0.0, local.x * s + local.z * c);
                // The ring rocks a little, like a dazed head.
                float around = atan2(turned.z, turned.x);
                turned.y = sin(around + _Time.y * 2.3 + phase) * 0.22;

                float size = state.scale * _VisualScaleMultiplier;
                float2 rendered = lerp(state.stepStartPosition, state.position, _PresentationAlpha);
                float3 world = turned * (_RingRadius * size) +
                    float3(rendered.x, state.airHeight + HordeMoundLift(rendered) + (_HeadHeight + 0.35) * size, rendered.y);
                // The ring closes in over the last quarter second as the stun wears off.
                float remaining = saturate(state.slowTimer / 0.25);
                world = lerp(float3(rendered.x, world.y, rendered.y), world, remaining);
                world.y += stunned ? 0.0 : -100000.0;

                output.position = UnityWorldToClipPos(world);
                output.glow = 0.85 + 0.15 * sin(_Time.y * 9.0 + phase);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                return float4(_StarColor.rgb * input.glow, 1.0);
            }
            ENDCG
        }
    }
}
