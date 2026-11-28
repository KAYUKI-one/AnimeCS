Shader "AnimeCS/Tracer Ribbon"
{
    Properties { [HDR] _BaseColor ("Color", Color) = (1,.76,.38,.84) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha One
            Cull Off
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct Input { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            Varyings Vert(Input i)
            {
                Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; o.color = i.color; return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float flank = abs(i.uv.y * 2 - 1);
                half core = exp2(-9 * flank * flank);
                half feather = saturate((1-flank) / max(.13, fwidth(flank)));
                half lengthMask = smoothstep(0,.44,i.uv.x) * (1-smoothstep(.84,1,i.uv.x));
                return half4(lerp(_BaseColor.rgb,half3(1,.94,.76),core*.7),_BaseColor.a * i.color.a * lengthMask * feather * (.26+.74*core));
            }
            ENDHLSL
        }
    }
}
