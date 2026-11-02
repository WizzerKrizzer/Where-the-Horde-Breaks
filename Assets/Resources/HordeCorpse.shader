Shader "TowerDefense/HordeCorpse"
{
    Properties
    {
        _Color ("Corpse Color", Color) = (0.22, 0.22, 0.22, 1)
        _Flatten ("Flatten", Range(0.02, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-1" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            StructuredBuffer<int> _Aux;
            uint _CorpseBase;
            float4 _Color;
            float _Flatten;

            struct Attributes
            {
                float4 vertex : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                uint offset = _CorpseBase + input.instanceID * 4u;
                float4 corpse = float4(asfloat(_Aux[offset]), asfloat(_Aux[offset + 1u]), asfloat(_Aux[offset + 2u]), asfloat(_Aux[offset + 3u]));
                float valid = corpse.w > 0.5 ? 1.0 : 0.0;
                float3 local = input.vertex.xyz;
                local.y *= _Flatten;
                float3 world = local * corpse.z + float3(corpse.x, 0.0, corpse.y);
                world.y += (1.0 - valid) * -100000.0;
                Varyings output;
                output.position = UnityWorldToClipPos(world);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
