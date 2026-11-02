Shader "TowerDefense/WeatheredRoad"
{
    Properties
    {
        _RoadField ("Footprint and wheel ruts", 2D) = "black" {}
        _GroundColor ("Ground", Color) = (0.3, 0.3, 0.3, 1)
        _RoadColor ("Road", Color) = (0.4, 0.4, 0.4, 1)
        _WorldRect ("World bounds", Vector) = (0,0,100,100)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            sampler2D _RoadField;
            fixed4 _GroundColor, _RoadColor;
            float4 _WorldRect;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; SHADOW_COORDS(1) };
            v2f vert(appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                TRANSFER_SHADOW(o); return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                // Greybox placeholder: two flat values split by the road field.
                float2 p = i.world.xz;
                float4 field = tex2D(_RoadField, (p-_WorldRect.xy) / _WorldRect.zw);
                float distance = (field.r-.5)*8;
                float road = smoothstep(-.05, .05, distance);
                float3 colour = lerp(_GroundColor.rgb, _RoadColor.rgb, road);
                colour *= lerp(.65, 1.0, SHADOW_ATTENUATION(i));
                return fixed4(colour,1);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
