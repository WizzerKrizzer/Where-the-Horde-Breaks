// Mounds the horde walks over (a fallen mammoth's body, user 2026-11-11): goblins inside one are
// drawn lifted onto it. Each mound is a capsule on the ground, from a to b with a radius, and a
// height: flat on top, easing down to the ground at its edge. Set by HordeMounds.cs; only drawing
// changes, the simulation walks them on flat ground.
#ifndef HORDE_MOUNDS_INCLUDED
#define HORDE_MOUNDS_INCLUDED

float4 _HordeMoundSegments[4]; // xy: a, zw: b
float4 _HordeMoundShapes[4];   // x: radius, y: height
float _HordeMoundCount;

float HordeMoundLift(float2 position)
{
    float lift = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        if (i < (int)_HordeMoundCount)
        {
            float2 a = _HordeMoundSegments[i].xy;
            float2 b = _HordeMoundSegments[i].zw;
            float2 ab = b - a;
            float t = saturate(dot(position - a, ab) / max(dot(ab, ab), 1e-4));
            float d = length(position - (a + ab * t));
            float radius = _HordeMoundShapes[i].x;
            lift = max(lift, _HordeMoundShapes[i].y * smoothstep(radius, radius * 0.4, d));
        }
    }

    return lift;
}

#endif
