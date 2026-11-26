Shader "AnimeCS/Device burst"
{
    Properties { _BaseMap("Explosion flipbook",2D)="white"{} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma editor_sync_compilation
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct Attributes {float4 pos:POSITION;float2 uv:TEXCOORD0;half4 tint:COLOR;};
            struct Varyings {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;half4 tint:COLOR;};
            Varyings vert(Attributes v)
            {
                Varyings o;o.pos=TransformObjectToHClip(v.pos.xyz);o.uv=v.uv;o.tint=v.tint;return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                half4 f=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);
                return half4(f.rgb*i.tint.rgb*1.6, f.a*i.tint.a);
            }
            ENDHLSL
        }
    }
}
