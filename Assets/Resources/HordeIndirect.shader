Shader "TowerDefense/HordeIndirect"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.08, 0.82, 0.29, 1)
        _SlowColor ("Slow Color", Color) = (0.16, 0.5, 1, 1)
        _RareColor ("One In A Thousand Color", Color) = (0.82, 0.18, 0.96, 1)
        _SpeedColor0 ("Speed 5.0-5.5", Color) = (0.96, 0.16, 0.1, 1)
        _SpeedColor1 ("Speed 5.6-6.0", Color) = (1, 0.45, 0.05, 1)
        _SpeedColor2 ("Speed 6.1-6.5", Color) = (1, 0.86, 0.12, 1)
        _SpeedColor3 ("Speed 6.6-7.0", Color) = (0.08, 0.82, 0.29, 1)
        _UseSpeedColors ("Use Speed Colors", Float) = 0
        _PackingScale ("Dense Packing Silhouette Scale", Range(1, 1.25)) = 1.18
        _VisualScaleMultiplier ("Visual Scale Multiplier", Range(0.1, 2)) = 1
        _HordeSpriteEnabled ("Horde Sprite Enabled", Float) = 0
        _HordeSpriteFarLod ("Horde Sprite Far LOD", Float) = 0
        _HordeSpriteAtlas ("Horde Sprite Atlas", 2D) = "white" {}
        _HordeSpriteStride ("Horde Sprite Cycle Distance", Float) = 5.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
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
            float _HordeSpriteEnabled;
            float _HordeSpriteFarLod;
            float _HordeSpriteStride;
            float _PresentationAlpha;
            float2 _SpriteDepthSortAxis;
            float _SpriteDepthSortSlope;
            sampler2D _HordeSpriteAtlas;

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
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
                float2 uv : TEXCOORD5;
                float2 cardUv : TEXCOORD6;
            };

            Varyings Vert(Attributes input)
            {
                uint agentIndex = _VisibleIndices[input.instanceID + _VisibleIndexOffset];
                AgentState state = _AgentStates[agentIndex];
                Varyings output;
                float active = state.status == 1 ? 1.0 : 0.0;
                float3 localVertex = input.vertex.xyz;
                float3 localNormal = input.normal;
                float2 atlasUv = input.uv;

                if (_HordeSpriteEnabled > 0.5)
                {
                    // The art moves only when the GPU simulation accepted real
                    // displacement. It cannot keep running once the horde stops.
                    float speed = length(state.velocity);
                    float2 forward = dot(state.facing, state.facing) > 0.1 ? normalize(state.facing) :
                        (speed > 0.02 ? state.velocity / speed : float2(0, 1));
                    float startPhase = frac((float)agentIndex * 0.61803398875);
                    float worldStride = max(0.25, _HordeSpriteStride * state.scale * _VisualScaleMultiplier);
                    float stepDistance = length(state.position - state.stepStartPosition);
                    float travelled = max(0.0, state.animationDistance - stepDistance * (1.0 - _PresentationAlpha));
                    float moving = smoothstep(0.025, 0.10, state.locomotionSpeed);
                    float frame = moving > 0.001 ? floor(frac(travelled / worldStride + startPhase) * 5.0) : 0.0;

                    // Use one unambiguous cardinal card. Blending neighbouring
                    // cards looked like two faces were painted onto one enemy;
                    // genuine eight-direction art is the correct future upgrade.
                    // Rows are N, E, S, W from top to bottom.
                    float quarterPosition = atan2(forward.x, forward.y) * 0.63661977237;
                    if (quarterPosition < 0.0) quarterPosition += 4.0;
                    float row = fmod(floor(quarterPosition + 0.5), 4.0);
                    // Pick one authored view, then rotate the horizontal card by
                    // only the remaining <= 45 degrees.  Blending adjacent art
                    // made two faces/backs overlap; leaving the card unrotated
                    // made diagonal movement visibly face the wrong way.
                    float headingAngle = atan2(forward.x, forward.y);
                    float authoredAngle = row * 1.57079632679;
                    float residualAngle = atan2(sin(headingAngle - authoredAngle),
                        cos(headingAngle - authoredAngle));
                    float residualSin;
                    float residualCos;
                    sincos(residualAngle, residualSin, residualCos);
                    localVertex.xz = float2(
                        localVertex.x * residualCos + localVertex.z * residualSin,
                        -localVertex.x * residualSin + localVertex.z * residualCos);
                    // Source art is N,E,S,W from top to bottom; Unity UVs begin at
                    // the bottom. The inset prevents bilinear frame bleeding.
                    atlasUv = float2((input.uv.x * 0.986 + 0.007 + frame) / 5.0,
                        (input.uv.y * 0.986 + 0.007 + (3.0 - row)) / 4.0);
                    localNormal = float3(0.0, 1.0, 0.0);
                }

                localVertex.xz *= _PackingScale;
                float2 renderedPosition = lerp(state.stepStartPosition, state.position, _PresentationAlpha);
                float3 world = localVertex * (state.scale * _VisualScaleMultiplier) +
                    float3(renderedPosition.x, 0.0, renderedPosition.y);
                // Cards are flat and therefore overlap on exactly the same depth
                // plane. Give them a stable, camera-relative micro elevation so
                // their draw order does not depend on the unordered GPU cull list.
                // The road is at y=0.092; the complete range remains above it and
                // affects rendering only, never physics or pathing.
                if (_HordeSpriteEnabled > 0.5)
                {
                    float stableJitter = frac((float)agentIndex * 0.7548777);
                    float cameraOrder = dot(renderedPosition, _SpriteDepthSortAxis);
                    world.y += 0.13 + cameraOrder * _SpriteDepthSortSlope + stableJitter * 0.00035;
                }
                world.y += (1.0 - active) * -100000.0;

                output.position = UnityWorldToClipPos(world);
                output.normal = localNormal;
                output.uv = atlasUv;
                output.cardUv = input.uv;
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

                if (_HordeSpriteEnabled > 0.5)
                {
                    // A full painted sprite becomes a noisy collection of black
                    // outlines below a few screen pixels. Distant agents use one
                    // soft, unlit horde token instead: it preserves the mass and
                    // direction of the crowd without pretending that its shadow
                    // is still readable at this scale.
                    if (_HordeSpriteFarLod > 0.5)
                    {
                        float2 centeredUv = input.cardUv - 0.5;
                        float radial = dot(centeredUv * float2(1.0, 0.82),
                            centeredUv * float2(1.0, 0.82));
                        clip(0.235 + fwidth(radial) - radial);
                        float core = saturate(1.0 - radial / 0.235);
                        float3 farColor = lerp(float3(0.13, 0.085, 0.035),
                            float3(0.32, 0.38, 0.095), 0.48 + core * 0.28);
                        farColor *= individualVariation;
                        return fixed4(farColor, 1.0);
                    }
                    fixed4 sprite = tex2D(_HordeSpriteAtlas, input.uv);
                    clip(sprite.a - 0.08);
                    // Painted directional light and a contact shadow are part of
                    // the card. Per-pixel 3D lighting made the old mesh pure white.
                    sprite.rgb *= individualVariation;
                    sprite.rgb = lerp(sprite.rgb, sprite.rgb * _SlowColor.rgb * 1.35, normalTint * 0.24);
                    sprite.rgb = lerp(sprite.rgb, _RareColor.rgb, input.rare * 0.28);
                    return fixed4(sprite.rgb, 1.0);
                }

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
