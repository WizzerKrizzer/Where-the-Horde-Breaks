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
        _MainTex ("Goblin Texture", 2D) = "white" {}
        _ModelEnabled ("Draw The Goblin Model", Float) = 0
        _WalkPoses ("Walk Poses A Cycle", Float) = 6
        _StrideMetres ("Metres A Walk Cycle At Scale 0.36", Float) = 0.7
        _OutlineWidth ("Outline Width, Metres", Float) = 0
        _OutlineColor ("Outline Color", Color) = (0.05, 0.08, 0.04, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        CGINCLUDE
            #include "UnityCG.cginc"
            #include "HordeMounds.cginc"
            #include "HordeGoblin.cginc"

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
                float edgeWalk;
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
            // The goblin model (GoblinModel): one row of vertex positions per pose.
            sampler2D _MainTex;
            float _ModelEnabled;
            float _OutlineWidth;
            float4 _OutlineColor;

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                // x: what a vertex of the goblin model belongs to (0 the body, 1 club, 2 bone, 3 stone).
                float2 held : TEXCOORD1;
                uint vertexID : SV_VertexID;
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
                float2 uv : TEXCOORD6;
                float shade : TEXCOORD8;
                float3 world : TEXCOORD7;
                // War paint (HordeGoblinLooks.cginc): rgb and how much on this vertex.
                float4 paint : TEXCOORD9;
            };

            // How far towards the camera the held weapon is drawn, metres (the brute's club, 0 for the
            // goblin): it is drawn over the bodies round it instead of passing through them (user,
            // 2026-11-15: the brute's club clipped through the goblins next to it). Only where it is
            // drawn moves, along the line to the camera, so on screen it stays in its place.
            float _HeldDepthPull;
            float4 HeldClipPos(float3 world, float2 held)
            {
                if (_HeldDepthPull > 0.0 && held.x > 0.5)
                {
                    world += normalize(_WorldSpaceCameraPos - world) * _HeldDepthPull;
                }

                // Dead or hidden (AgentWorld drops them 100 km down): one point, no area, so no
                // fragment has to be thrown away and the depth test can run before shading
                // (optimization, 2026-11-16: the clip() that hid them turned early depth off).
                if (world.y < -50000.0)
                {
                    return float4(0.0, 0.0, -1.0, 1.0);
                }

                return UnityWorldToClipPos(world);
            }

            // Where a vertex of an agent's body is in the world. `outline` pushes it out along its
            // normal by that many metres (the outline pass).
            float3 AgentWorld(Attributes input, float outline, out AgentState state, out uint agentIndex, out float active,
                out float variation, out float3 localNormal)
            {
                agentIndex = _VisibleIndices[input.instanceID + _VisibleIndexOffset];
                state = _AgentStates[agentIndex];
                active = state.status == 1 ? 1.0 : 0.0;
                float3 localVertex = input.vertex.xyz;
                localNormal = input.normal;

                variation = frac(sin((float)agentIndex * 12.9898) * 43758.5453);
                bool hidden = false;
                if (_ModelEnabled > 0.5)
                {
                    localVertex = GoblinLocal(agentIndex, input.vertexID, input.held, localNormal,
                        outline / max(state.scale, 0.01), state.animationDistance, state.locomotionSpeed, state.facing,
                        state.scale, (state.flags & 2u) != 0u, state.attackTimer, state.attackInterval, state.flags, hidden);
                }

                localVertex.xz *= _PackingScale;
                float2 renderedPosition = lerp(state.stepStartPosition, state.position, _PresentationAlpha);
                // Thrown into the air: lifted along its arc and drawn a little larger, as if nearer
                // the camera above (12% per metre); its shadow stays true to size.
                float airScale = 1.0 + 0.12 * state.airHeight;
                float3 world = localVertex * (state.scale * _VisualScaleMultiplier * airScale) +
                    float3(renderedPosition.x, 0.0, renderedPosition.y);
                world.y += state.airHeight;
                // Climbing over a fallen mammoth.
                world.y += HordeMoundLift(renderedPosition);
                world.y += (1.0 - active) * -100000.0;
                if (hidden) world.y = -100000.0;
                return world;
            }

            Varyings Vert(Attributes input)
            {
                AgentState state;
                uint agentIndex;
                float active;
                float variation;
                float3 localNormal;
                float3 world = AgentWorld(input, 0.0, state, agentIndex, active, variation, localNormal);
                Varyings output;
                output.position = HeldClipPos(world, input.held);
                output.normal = localNormal;
                output.tint = state.tint;
                output.active = active;
                output.variation = variation;
                output.uv = input.uv;
                // One of five skin shades, picked at random for each goblin (user, 2026-11-14:
                // "very slight", so a crowd is not one flat patch).
                output.shade = floor(frac(sin((float)agentIndex * 78.233 + 4.7) * 24634.6345) * 5.0);
                output.world = world;
                output.paint = input.held.x < 0.5 && input.held.y < 0.5 ? GoblinPaint(agentIndex, input.vertex.xyz) : 0.0;
                output.rare = ((agentIndex + 1u) % 1000u) == 0u && _UseSpeedColors < 0.5 ? 1.0 : 0.0;
                output.burn = state.burnTimer > 0.0 && state.burnDamagePerSecond > 0.0
                    ? ((state.flags & 8u) != 0u ? 2.0 : 1.0)
                    : (state.slowTimer > 0.0 && state.slowMultiplier <= 0.06 ? 3.0 : 0.0);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float normalTint = input.tint >= 3.5 ? 1.0 : saturate(input.tint);
                float individualVariation = lerp(0.94, 1.06, input.variation);

                if (_ModelEnabled > 0.5)
                {
                    // Flat facets from the screen-space slope of the surface: the low poly look,
                    // with no normals to bake for every pose.
                    float3 facet = normalize(cross(ddy(input.world), ddx(input.world)));
                    if (dot(facet, _WorldSpaceCameraPos - input.world) < 0.0) facet = -facet;
                    // Strong steps between the facets, so a body keeps its shape up close.
                    float facetLight = (0.46 + 0.62 * saturate(dot(facet, normalize(float3(0.35, 0.8, 0.25))))) *
                        lerp(0.94, 1.06, input.variation);
                    float3 albedo = tex2D(_MainTex, input.uv).rgb;
                    float3 shades[5] = {
                        float3(1.00, 1.00, 1.00),
                        float3(1.07, 1.03, 0.92),
                        float3(0.93, 1.00, 1.05),
                        float3(0.90, 0.93, 0.90),
                        float3(1.04, 1.08, 1.02)
                    };
                    // Only the green skin takes the shade; the club and the loincloth stay as they are.
                    float skin = saturate((albedo.g - max(albedo.r, albedo.b)) * 8.0);
                    albedo *= lerp(float3(1.0, 1.0, 1.0), shades[(uint)input.shade], skin);
                    albedo = lerp(albedo, input.paint.rgb, saturate(input.paint.a) * skin * 0.9);
                    if (input.burn > 2.5)
                    {
                        albedo = lerp(albedo, _StunColor.rgb, 0.35);
                    }
                    else if (input.burn > 0.5)
                    {
                        albedo = lerp(albedo, input.burn > 1.5 ? _PoisonColor.rgb : _BurnColor.rgb, 0.62);
                    }
                    return float4(albedo * facetLight, 1.0);
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

            struct OutlineVaryings
            {
                float4 position : SV_POSITION;
                float active : TEXCOORD0;
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

            // The goblin model's outline: the body drawn again a little larger, inside out and dark,
            // so each goblin has an edge against its neighbours (user, 2026-11-14). Off for the
            // placeholder bodies and for the far level of detail (_OutlineWidth 0).
            OutlineVaryings VertOutline(Attributes input)
            {
                OutlineVaryings output;
                if (_ModelEnabled < 0.5 || _OutlineWidth <= 0.0)
                {
                    // No outline (the far level of detail): every vertex at one point, so the
                    // triangles have no area and nothing is posed or drawn (optimization, 2026-11-16:
                    // posing them only to hide them cost a third of the far crowd's vertex work).
                    output.position = float4(0.0, 0.0, -1.0, 1.0);
                    output.active = 0.0;
                    return output;
                }

                AgentState state;
                uint agentIndex;
                float active;
                float variation;
                float3 localNormal;
                AgentState placed = _AgentStates[_VisibleIndices[input.instanceID + _VisibleIndexOffset]];
                float2 at = lerp(placed.stepStartPosition, placed.position, _PresentationAlpha);
                float width = OutlineWidthAt(float3(at.x, placed.airHeight + 0.6, at.y), _OutlineWidth);
                float3 world = AgentWorld(input, width, state, agentIndex, active, variation, localNormal);
                output.position = HeldClipPos(world, input.held);
                output.active = active;
                return output;
            }

            fixed4 FragOutline(OutlineVaryings input) : SV_Target
            {
                return _OutlineColor;
            }

        ENDCG

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDCG
        }

        Pass
        {
            Name "Outline"
            Cull Front
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex VertOutline
            #pragma fragment FragOutline
            #pragma multi_compile_instancing
            ENDCG
        }
    }
}
