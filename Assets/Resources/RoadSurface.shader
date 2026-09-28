Shader "TowerDefense/WeatheredRoad"
{
    Properties
    {
        _RoadField ("Footprint", 2D) = "black" {}
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
            #include "UnityCG.cginc"
            sampler2D _RoadField;
            float4 _WorldRect;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.world.xz;
                float3 roadColour = float3(.34, .39, .44);
                // The dark field exists only to show the route silhouette.  It is
                // deliberately flat: all scenery and surface texture are removed.
                float3 backgroundColour = float3(.035, .045, .05);
                float4 field = tex2D(_RoadField, (p-_WorldRect.xy) / _WorldRect.zw);
                float distance = (field.r-.5)*8;
                float aa = max(fwidth(distance), .025);
                float road = smoothstep(-aa,aa,distance);
                float3 colour = lerp(backgroundColour, roadColour, road);
                return fixed4(colour,1);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
