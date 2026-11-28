Shader "AnimeCS/Horizon Ground"
{
    Properties
    {
        [MainTexture] _BaseMap("Sand", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-100" "RenderType"="Background" }
        Pass
        {
            Name "BackgroundGround"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZTest LEqual
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 lighting : TEXCOORD1;
                half fog : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                // The miniature sky scene surrounds the playable level. Only shade pixels still at sky depth.
                output.positionCS.z = UNITY_RAW_FAR_CLIP_VALUE * output.positionCS.w;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                half3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                Light sun = GetMainLight();
                output.lighting = max(SampleSH(normalWS), 0.15h) + sun.color * saturate(dot(normalWS, sun.direction));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                return half4(MixFog(albedo * input.lighting, input.fog), 1);
            }
            ENDHLSL
        }
    }
}
