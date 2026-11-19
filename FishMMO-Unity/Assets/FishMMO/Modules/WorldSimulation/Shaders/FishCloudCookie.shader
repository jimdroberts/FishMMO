// Turns the weather noise into a cloud-shadow cookie for the sun: white where light passes,
// darker under clouds. Blitted only when the cloud cover changes.
Shader "Hidden/FishMMO/Weather/CloudCookie"
{
    Properties { _MainTex ("Noise", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _CookieParams; // x cover, y darkness, z softness

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f Vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 Frag(v2f i) : SV_Target
            {
                float4 n = tex2D(_MainTex, i.uv);
                float value = n.r * 0.65 + n.g * 0.35;
                float cover = _CookieParams.x;
                float cloud = smoothstep(1.0 - cover - _CookieParams.z, 1.0 - cover + _CookieParams.z, value) * saturate(cover * 3.0);
                float light = 1.0 - cloud * _CookieParams.y;
                return float4(light, light, light, light);
            }
            ENDCG
        }
    }
    Fallback Off
}
