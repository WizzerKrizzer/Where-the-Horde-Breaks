#ifndef HORDE_GOBLIN_LOOKS_INCLUDED
#define HORDE_GOBLIN_LOOKS_INCLUDED

// Small differences between one goblin (or brute) and the next (user, 2026-11-15: hoods, skulls
// and mantles made a big crowd harder to read, so only small ones): some have a long tail of hair
// down the back, some a taller crest of spikes, and some a few faint marks of paint on the head.
// Picked from the body's number, so the living body, its outline, its shadow and its corpse all
// agree (the corpse ring keeps the low 16 bits of the number). build_goblin.py and build_brute.py
// number the hair: 1 the tail, 2 the crest.

// 1 for a model with these parts (the goblin, the brute); 0 for one without, which wears nothing extra.
float _GoblinLooks;
// The goblin model's height in its own units, standing (for the paint's place on the head).
float _GoblinHeight;
// Where the paint goes, as shares of that height: x the crown stripe from, y and z the cheek bars
// from and to. Zero is the goblin's (a big head from about 0.6 of its height); the brute's head is
// only the top sixth of it.
float4 _PaintBands;

float GoblinLookHash(uint agentIndex, float seed)
{
    return frac(sin((float)(agentIndex & 0xFFFFu) * seed) * 43758.5453);
}

// Whether this goblin shows a vertex worn as `wear` (0 is the body, always shown).
bool GoblinWears(uint agentIndex, float wear)
{
    if (wear < 0.5)
    {
        return true;
    }

    if (_GoblinLooks < 0.5)
    {
        return false;
    }

    // Most have only their own hair; a quarter a tail of hair, a fifth a crest.
    float pick = GoblinLookHash(agentIndex, 47.1373);
    float hair = pick < 0.55 ? 0.0 : (pick < 0.8 ? 1.0 : 2.0);
    return abs(wear - hair) < 0.5;
}

// Faint marks on the head of about a quarter: rgb the colour, a how much of it lies on this vertex
// (kept light, so it reads only up close). `rest` is the vertex standing (the mesh's own position),
// in the model's units.
float4 GoblinPaint(uint agentIndex, float3 rest)
{
    if (_GoblinLooks < 0.5 || _GoblinHeight <= 0.0)
    {
        return 0.0;
    }

    float pick = GoblinLookHash(agentIndex, 19.9133);
    if (pick > 0.25)
    {
        return 0.0;
    }

    // Ochre, pale clay or soot (a deep red read as wounds from above).
    float3 colour = pick < 0.1 ? float3(0.80, 0.45, 0.16) : (pick < 0.18 ? float3(0.86, 0.83, 0.74) : float3(0.13, 0.11, 0.1));
    float h = rest.y / _GoblinHeight;
    float x = rest.x / _GoblinHeight;
    float z = rest.z / _GoblinHeight;
    float mask;
    if (GoblinLookHash(agentIndex, 7.7713) < 0.5)
    {
        // A stripe over the crown, front to back.
        float crown = _PaintBands.x > 0.0 ? _PaintBands.x : 0.8;
        mask = h > crown && abs(x) < 0.04 ? 1.0 : 0.0;
    }
    else
    {
        // A bar across each cheek, under the eyes.
        float2 cheeks = _PaintBands.y > 0.0 ? _PaintBands.yz : float2(0.71, 0.79);
        mask = h > cheeks.x && h < cheeks.y && abs(x) > 0.025 && z > 0.03 ? 1.0 : 0.0;
    }

    return float4(colour, mask * 0.5);
}

#endif
