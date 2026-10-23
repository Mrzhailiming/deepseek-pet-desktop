// 桌宠的 UI 材质：和 UI/Default 一样，只是输出预乘 alpha。
// 透明窗口由 DWM 按「预乘 alpha」合成；UI/Default 的 Blend SrcAlpha OneMinusSrcAlpha 写进帧缓冲的 alpha 是 a²，
// 半透明的字边会发虚发黑。这里改成 Blend One OneMinusSrcAlpha 并在片元里把颜色乘上 alpha。
Shader "DeepseekPet/UIPremultiplied"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask RGBA

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = (tex2D(_MainTex, i.texcoord) + _TextureSampleAdd) * i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
