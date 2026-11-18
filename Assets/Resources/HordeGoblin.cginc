#ifndef HORDE_GOBLIN_INCLUDED
#define HORDE_GOBLIN_INCLUDED

#include "HordeGoblinLooks.cginc"

// The goblin model in the horde (GoblinModel): the body's vertex positions for every pose are in
// _PoseTex, one row a pose and one texel a vertex. Shared by the body, its outline and its shadow,
// so all three take the same pose, turn, size and thing in the hand.
//
// Rows: the walk (_WalkPoses of them), then standing still, the weapon raised, the weapon struck.

Texture2D<float4> _PoseTex;
float _WalkPoses;
float _StrideMetres;
// The horde scale the model was built at (0 is the goblin's 0.36; the brute's is 0.39), and what it
// holds: above 0.5 always that thing; below -0.5 each its own of the three, never nothing (the
// brutes); otherwise each goblin its own, some empty-handed.
float _ModelBaseScale;
float _HeldFixed;
// The blow's own rows, for a model that has them (the brute, build_brute.py; all 0 for the goblin):
// x the first row, y how many, z which of them is the blow landing (the rows before it are the
// wind-up, the ones from it the blow held and coming back). _AttackTimes: x the seconds the wind-up
// takes before the blow lands, y the seconds the rest takes after it.
// w how many different blows follow one another, each y rows long; which one this blow is and
// which the last was come in the agent's flags (HordeSimulation.compute BLOW_SHIFT).
float4 _AttackRows;
float4 _AttackTimes;

float GoblinHash(uint agentIndex, float seed)
{
    return frac(sin((float)agentIndex * seed) * 43758.5453);
}

// What this goblin holds: 1 a club, 2 a bone, 3 a stone, 0 nothing (user, 2026-11-14).
float GoblinHeld(uint agentIndex)
{
    float pick = GoblinHash(agentIndex, 31.7171);
    return pick < 0.5 ? 1.0 : (pick < 0.7 ? 2.0 : (pick < 0.85 ? 3.0 : 0.0));
}

// The vertex in the agent's own space, posed and turned to its heading, in mesh units (to be
// multiplied by the agent's scale). `held` is the vertex's own numbers: x what it is held (0 for the
// body), y what it is worn (0 for the body); a vertex of something this goblin does not hold or wear
// comes back with `hidden` set. `outline` pushes the vertex out
// along its normal by that many mesh units.
float3 GoblinLocal(uint agentIndex, uint vertexID, float2 held, float3 normal, float outline,
    float animationDistance, float locomotionSpeed, float2 facing, float scale,
    bool fighting, float attackTimer, float attackInterval, uint flags, out bool hidden)
{
    float holding = _HeldFixed > 0.5 ? _HeldFixed
        : (_HeldFixed < -0.5 ? 1.0 + floor(GoblinHash(agentIndex, 31.7171) * 2.999) : GoblinHeld(agentIndex));
    hidden = (held.x > 0.5 && abs(held.x - holding) > 0.5) || !GoblinWears(agentIndex, held.y);
    if (hidden)
    {
        // Not drawn: skip the posing (optimization, 2026-11-16: the things a goblin does not hold
        // or wear are about half the far model's vertices). The callers drop it out of sight.
        return float3(0.0, 0.0, 0.0);
    }

    // No two alike: each a little taller or shorter, and turned a few degrees off the road.
    float size = lerp(0.9, 1.1, GoblinHash(agentIndex, 91.3458));
    float offCourse = radians(16.0) * (GoblinHash(agentIndex, 57.5811) - 0.5);

    // The walk advances with the ground covered, so the legs keep to the body's real speed; each
    // goblin starts at its own point of the cycle.
    float stride = max(0.05, _StrideMetres * size * scale / (_ModelBaseScale > 0.0 ? _ModelBaseScale : 0.36));
    float cycle = frac(animationDistance / stride + GoblinHash(agentIndex, 12.9898));
    uint pose = (uint)min(floor(cycle * _WalkPoses), _WalkPoses - 1.0);
    if (fighting)
    {
        // The blow lands as the attack timer starts again: the weapon is down for a moment after
        // it, and goes up shortly before the next.
        float sinceBlow = max(0.0, attackInterval - attackTimer);
        pose = (uint)_WalkPoses + (sinceBlow < 0.18 ? 2u : (attackTimer < 0.3 ? 1u : 0u));
        if (_AttackRows.y > 0.5)
        {
            // More drawings (user, 2026-11-15: two poses looked like teleporting): the wind-up
            // played up to the blow, the rest after it, standing in between.
            float hit = _AttackRows.z;
            float after = _AttackRows.y - hit;
            // The blow that just landed, and the one being wound up, each from a different place
            // for each brute (so they do not all swing alike).
            float blows = max(1.0, _AttackRows.w);
            float offset = floor(GoblinHash(agentIndex, 77.1213) * blows);
            float landed = fmod((float)((flags >> 6) & 3u) + offset, blows);
            float coming = fmod((float)((flags >> 8) & 3u) + offset, blows);
            // Only right after a blow: before the first one the timer is longer than the interval
            // (the brute stands still first), and that is not the end of a blow (user, 2026-11-16:
            // it started its attack with the end of one). The first wait (1.4 s) is shorter than
            // the interval (1.8 s), so it also needs a blow to have landed since it arrived
            // (HordeSimulation.compute BLOW_LANDED_FLAG).
            float sinceLanded = attackInterval - attackTimer;
            if ((flags & 1024u) != 0u && sinceLanded >= 0.0 && sinceLanded < _AttackTimes.y)
            {
                pose = (uint)(_AttackRows.x + landed * _AttackRows.y + hit + min(floor(sinceLanded / _AttackTimes.y * after), after - 1.0));
            }
            else if (attackTimer < _AttackTimes.x)
            {
                pose = (uint)(_AttackRows.x + coming * _AttackRows.y + min(floor((1.0 - attackTimer / _AttackTimes.x) * hit), hit - 1.0));
            }
            else
            {
                pose = (uint)_WalkPoses;
            }
        }
    }
    else if (locomotionSpeed <= 0.3)
    {
        pose = (uint)_WalkPoses;
    }

    float3 local = _PoseTex.Load(int3(vertexID, pose, 0)).xyz * size;
    float2 forward = dot(facing, facing) > 0.0001 ? normalize(facing) : float2(0.0, 1.0);
    float s, c;
    sincos(offCourse, s, c);
    forward = float2(forward.x * c - forward.y * s, forward.x * s + forward.y * c);
    local = float3(local.x * forward.y + local.z * forward.x, local.y, -local.x * forward.x + local.z * forward.y);
    // The first pose's normals, turned with the body: near enough for the outline.
    float3 turned = float3(normal.x * forward.y + normal.z * forward.x, normal.y, -normal.x * forward.x + normal.z * forward.y);
    // Drawn a little off its spot in the simulation, the same way every frame: the solver packs a
    // crowd into even rows, and up to 0.16 m each way breaks them up (user, 2026-11-14).
    float2 nudge = (float2(GoblinHash(agentIndex, 23.1406), GoblinHash(agentIndex, 64.8213)) - 0.5) * (0.32 / max(scale, 0.01));
    return local + turned * outline + float3(nudge.x, 0.0, nudge.y);
}

#endif
