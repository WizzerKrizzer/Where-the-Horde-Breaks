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
                float3 world = localVertex * (state.scale * _VisualScaleMultiplier) +
                    float3(renderedPosition.x, 0.0, renderedPosition.y);
                // Thrown by a blast: lifted along its arc.
                world.y += state.airHeight;
                world.y += (1.0 - active) * -100000.0;

                output.position = UnityWorldToClipPos(world);
                output.normal = localNormal;
                output.tint = state.tint;
                output.active = active;
                output.variation = frac(sin((float)agentIndex * 12.9898) * 43758.5453);
                output.rare = ((agentIndex + 1u) % 1000u) == 0u && _UseSpeedColors < 0.5 ? 1.0 : 0.0;
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
                return enemyColor * light;
            }
            ENDCG
        }
    }
}
