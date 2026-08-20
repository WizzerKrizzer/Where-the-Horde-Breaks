Shader "TowerDefense/HordeIndirect"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.1, 0.9, 0.18, 1)
        _SlowColor ("Slow Color", Color) = (0.2, 0.62, 1, 1)
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
            };

            Varyings Vert(Attributes input)
            {
                AgentState state = _AgentStates[_VisibleIndices[input.instanceID]];
                Varyings output;
                float active = state.status == 1 ? 1.0 : 0.0;
                float3 world = input.vertex.xyz * state.scale + float3(state.position.x, 0.0, state.position.y);
                world.y += (1.0 - active) * -100000.0;
                output.position = UnityWorldToClipPos(world);
                output.normal = input.normal;
                output.tint = state.tint;
                output.active = active;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                clip(input.active - 0.5);
                float light = 0.68 + saturate(dot(normalize(input.normal), normalize(float3(0.35, 0.8, 0.25)))) * 0.32;
                return lerp(_BaseColor, _SlowColor, saturate(input.tint)) * light;
            }
            ENDCG
        }
    }
}
