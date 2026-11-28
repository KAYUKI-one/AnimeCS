Shader "AnimeCS/Smoke puffs"
{
    Properties { _BaseMap("Puff",2D)="white"{} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float4 _HoleA[6],_HoleB[6];
            float4 _SmokeOrigin, _SmokeTime;
            struct Attributes {float4 pos:POSITION; half4 color:COLOR;float2 uv:TEXCOORD0;};
            struct Varyings {float4 pos:SV_POSITION;half4 color:COLOR;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;};
            Varyings vert(Attributes v)
            {
                Varyings o;
                float index = round(v.color.a * 255.0);
                float age = _SmokeTime.x;
                float3 drift = float3(sin(age*.57 + index), sin(age*.83 + index*.5)*.55, cos(age*.51 - index)) * .10;
                float3 offset = TransformObjectToWorld(v.pos.xyz) - _SmokeOrigin.xyz;
                o.world = _SmokeOrigin.xyz + (offset + drift) * _SmokeTime.y;
                o.pos = TransformWorldToHClip(o.world);
                o.uv = v.uv; o.color = half4(v.color.rgb, _SmokeTime.z);
                return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                half4 puff=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);
                float edge=saturate(1-length(i.uv*2-1));
                float alpha=i.color.a*puff.a*smoothstep(0,.27,edge);
                [unroll] for(int j=0;j<6;j++)
                {
                    float3 segment=_HoleB[j].xyz-_HoleA[j].xyz, local=i.world-_HoleA[j].xyz;
                    float d=length(local-segment*saturate(dot(local,segment)/max(.0001,dot(segment,segment))));
                    alpha*=lerp(1,smoothstep(_HoleA[j].w*.74,_HoleA[j].w+.025,d),step(.012,_HoleA[j].w));
                }
                return half4(i.color.rgb*lerp(.60,1.06,puff.r),alpha);
            }
            ENDHLSL
        }
    }
}
