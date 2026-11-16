Shader "TowerDefense/StyleGlow"
{
    // Light thrown on the ground by a fire (the burning rock, user 2026-11-12): a flat low-poly disc
    // (LowPolyShapes.Disc), added over what is below in three hard rings, brightest in the middle,
    // flickering a little.
    Properties
    {
        _Color ("Colour", Color) = (1, 0.45, 0.1, 0.6)
        _Flicker ("Flicker", Float) = 0.25
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            ZWrite Off
            Cull Off
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Flicker;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 flat : TEXCOORD0;
            };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.flat = vertex.xz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float d = length(i.flat) * 2.0;
                float falloff = ceil(saturate(1.0 - d) * 3.0) / 3.0;
                falloff *= falloff;
                float t = _Time.y;
                float flicker = 1.0 - _Flicker * (0.5 + 0.5 * sin(t * 11.0) * sin(t * 7.3 + 1.7));
                return float4(_Color.rgb * _Color.a * falloff * flicker, 0);
            }
            ENDCG
        }
    }
}
