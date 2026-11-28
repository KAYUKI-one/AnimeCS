Shader "AnimeCS/Lobby Illustration"
{
    Properties
    {
        [MainTexture] _MainTex ("Illustration", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Workbench ("Showcase treatment", Range(0,1)) = 0
        [Toggle] _Contact ("Contact shadow", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Contact;
            float _Workbench;
            CBUFFER_END
            struct Vertex { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Fragment { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Fragment Vert(Vertex v)
            {
                Fragment f;
                f.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                f.uv = v.uv;
                return f;
            }
            half4 Frag(Fragment f) : SV_Target
            {
                half4 color=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,f.uv)*_Color;
                half shade=dot(color.rgb,half3(.2126,.7152,.0722));
                half3 frost=lerp(half3(.45,.64,.74),half3(.82,.94,.97),shade);
                color.rgb=lerp(color.rgb,frost,_Workbench*.78);
                float radial=saturate(1.0-dot(f.uv*2.0-1.0,f.uv*2.0-1.0));
                color.a *= lerp(1.0,radial*radial,_Contact);
                return color;
            }
            ENDHLSL
        }
    }
}
