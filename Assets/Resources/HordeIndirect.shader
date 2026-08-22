Shader "TowerDefense/HordeIndirect"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.1, 0.9, 0.18, 1)
        _SlowColor ("Slow Color", Color) = (0.2, 0.62, 1, 1)
        _RareColor ("One In A Thousand Color", Color) = (0.62, 0.16, 0.82, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
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
                float padding3;
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
            };

            StructuredBuffer<AgentState> _AgentStates;
            StructuredBuffer<uint> _VisibleIndices;
            float4 _BaseColor;
            float4 _SlowColor;
            float4 _RareColor;
            float _LightingVariation;

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
                uint agentIndex = _VisibleIndices[input.instanceID];
                AgentState state = _AgentStates[agentIndex];
                Varyings output;
                float active = state.status == 1 ? 1.0 : 0.0;
                float rare = ((agentIndex + 1u) % 1000u) == 0u ? 1.0 : 0.0;
                float3 world = input.vertex.xyz * state.scale + float3(state.position.x, 0.0, state.position.y);
                world.y += (1.0 - active) * -100000.0;
                output.position = UnityWorldToClipPos(world);
                output.normal = input.normal;
                output.tint = state.tint;
                output.active = active;
                output.variation = frac(sin((float)agentIndex * 12.9898) * 43758.5453);
                output.rare = rare;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                clip(input.active - 0.5);
                float directionalLight = 0.68 + saturate(dot(normalize(input.normal), normalize(float3(0.35, 0.8, 0.25)))) * 0.32;
                float individualVariation = lerp(0.94, 1.06, input.variation);
                float light = lerp(0.84, directionalLight, saturate(_LightingVariation)) * individualVariation;
                float4 enemyColor = lerp(_BaseColor, _SlowColor, saturate(input.tint));
                enemyColor = lerp(enemyColor, _RareColor, input.rare);
                return enemyColor * light;
            }
            ENDCG
        }
    }
}
