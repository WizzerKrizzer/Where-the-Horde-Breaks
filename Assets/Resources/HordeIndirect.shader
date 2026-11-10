Shader "TowerDefense/HordeIndirect"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.55, 0.55, 0.55, 1)
        _SlowColor ("Slow Color", Color) = (0.8, 0.8, 0.8, 1)
        _RareColor ("One In A Thousand Color", Color) = (0.95, 0.95, 0.95, 1)
        _SpeedColor0 ("Speed 5.0-5.5", Color) = (0.3, 0.3, 0.3, 1)
        _SpeedColor1 ("Speed 5.6-6.0", Color) = (0.42, 0.42, 0.42, 1)
        _SpeedColor2 ("Speed 6.1-6.5", Color) = (0.55, 0.55, 0.55, 1)
        _SpeedColor3 ("Speed 6.6-7.0", Color) = (0.68, 0.68, 0.68, 1)
        _UseSpeedColors ("Use Speed Colors", Float) = 0
        _PackingScale ("Dense Packing Silhouette Scale", Range(1, 1.25)) = 1.18
        _VisualScaleMultiplier ("Visual Scale Multiplier", Range(0.1, 2)) = 1
        _BurnColor ("Burning Overlay", Color) = (0.95, 0.45, 0.12, 1)
        _PoisonColor ("Poisoned Overlay", Color) = (0.42, 0.85, 0.22, 1)
        _StunColor ("Stunned Overlay", Color) = (1, 0.9, 0.45, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

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
            float4 _BaseColor;
            float4 _SlowColor;
            float4 _RareColor;
            float4 _SpeedColor0;
            float4 _SpeedColor1;
            float4 _SpeedColor2;
            float4 _SpeedColor3;
            float _UseSpeedColors;
            float _PackingScale;
            float _VisualScaleMultiplier;
            float4 _BurnColor;
            float4 _PoisonColor;
            float4 _StunColor;
            float _LightingVariation;
            float _PresentationAlpha;

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
                float tint : TEXCOORD1;
                float active : TEXCOORD2;
                float variation : TEXCOORD3;
                float rare : TEXCOORD4;
                // 0: none, 1: burning, 2: poisoned, 3: stunned (a light wash; the stars may be capped).
                float burn : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                uint agentIndex = _VisibleIndices[input.instanceID + _VisibleIndexOffset];
                AgentState state = _AgentStates[agentIndex];
                Varyings output;
                float active = state.status == 1 ? 1.0 : 0.0;
                float3 localVertex = input.vertex.xyz;
                float3 localNormal = input.normal;

                localVertex.xz *= _PackingScale;
                float2 renderedPosition = lerp(state.stepStartPosition, state.position, _PresentationAlpha);
                // Thrown into the air: lifted along its arc and drawn a little larger, as if nearer
                // the camera above (12% per metre); its shadow stays true to size.
                float airScale = 1.0 + 0.12 * state.airHeight;
                float3 world = localVertex * (state.scale * _VisualScaleMultiplier * airScale) +
                    float3(renderedPosition.x, 0.0, renderedPosition.y);
                world.y += state.airHeight;
                world.y += (1.0 - active) * -100000.0;

                output.position = UnityWorldToClipPos(world);
                output.normal = localNormal;
                output.tint = state.tint;
                output.active = active;
                output.variation = frac(sin((float)agentIndex * 12.9898) * 43758.5453);
                output.rare = ((agentIndex + 1u) % 1000u) == 0u && _UseSpeedColors < 0.5 ? 1.0 : 0.0;
                output.burn = state.burnTimer > 0.0 && state.burnDamagePerSecond > 0.0
                    ? ((state.flags & 8u) != 0u ? 2.0 : 1.0)
                    : (state.slowTimer > 0.0 && state.slowMultiplier <= 0.06 ? 3.0 : 0.0);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                clip(input.active - 0.5);
                float normalTint = input.tint >= 3.5 ? 1.0 : saturate(input.tint);
                float individualVariation = lerp(0.94, 1.06, input.variation);

                float directionalLight = 0.68 + saturate(dot(normalize(input.normal), normalize(float3(0.35, 0.8, 0.25)))) * 0.32;
                float light = lerp(0.84, directionalLight, saturate(_LightingVariation)) * individualVariation;
                float4 enemyColor = lerp(_BaseColor, _SlowColor, normalTint);
                if (_UseSpeedColors > 0.5)
                {
                    if (input.tint >= 3.5) enemyColor = _SlowColor;
                    else if (input.tint < 0.5) enemyColor = _SpeedColor0;
                    else if (input.tint < 1.5) enemyColor = _SpeedColor1;
                    else if (input.tint < 2.5) enemyColor = _SpeedColor2;
                    else enemyColor = _SpeedColor3;
                }
                enemyColor = lerp(enemyColor, _RareColor, input.rare);
                // Status overlays are flat colour washes (fire orange, poison green).
                if (input.burn > 2.5)
                {
                    enemyColor.rgb = lerp(enemyColor.rgb, _StunColor.rgb, 0.35);
                }
                else if (input.burn > 0.5)
                {
                    enemyColor.rgb = lerp(enemyColor.rgb, input.burn > 1.5 ? _PoisonColor.rgb : _BurnColor.rgb, 0.62);
                }
                // Light changes the brightness only; multiplying the alpha too made bodies see-through.
                return float4(enemyColor.rgb * light, enemyColor.a);
            }
            ENDCG
        }
    }
}
