Shader "TowerDefense/HordeAirShadow"
{
    // The shadow of a goblin thrown into the air: its silhouette projected onto the ground along
    // the 80 degree sun (the same direction as the style tests' drop shadows). Goblins on the
    // ground have no shadow here. The shadow fades in as the goblin rises, so it separates from the
    // body while it flies and melts back under it on landing. A stencil bit keeps overlapping
    // triangles and neighbouring shadows from darkening the ground twice.
    Properties
    {
        _ShadowColor ("Shadow Color", Color) = (0, 0, 0, 0.42)
        _PackingScale ("Dense Packing Silhouette Scale", Range(1, 1.25)) = 1.18
        _VisualScaleMultiplier ("Visual Scale Multiplier", Range(0.1, 2)) = 1
        _ShadowHeight ("Ground Height", Float) = 0.03
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Geometry+20" }

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
            #include "UnityCG.cginc"

            static const float3 ShadowDirection = float3(-0.147, 0.985, 0.093);

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

            StructuredBuffer<AgentState> _AgentStates;
            StructuredBuffer<uint> _VisibleIndices;
            uint _VisibleIndexOffset;
            float4 _ShadowColor;
            float _PackingScale;
            float _VisualScaleMultiplier;
            float _PresentationAlpha;
            float _ShadowHeight;

            struct Attributes
            {
                float4 vertex : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float strength : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                uint agentIndex = _VisibleIndices[input.instanceID + _VisibleIndexOffset];
                AgentState state = _AgentStates[agentIndex];
                // Full strength once the feet are about 0.25 m up.
                float strength = state.status == 1 ? saturate(state.airHeight / 0.25) : 0.0;
                float3 local = input.vertex.xyz;
                local.xz *= _PackingScale;
                float2 rendered = lerp(state.stepStartPosition, state.position, _PresentationAlpha);
                float3 world = local * (state.scale * _VisualScaleMultiplier) + float3(rendered.x, state.airHeight, rendered.y);
                world.xz -= ShadowDirection.xz / ShadowDirection.y * world.y;
                world.y = _ShadowHeight;
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
