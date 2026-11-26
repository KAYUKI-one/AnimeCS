Shader "AnimeCS/Muzzle Flash"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }
        Pass
        {
            Blend One One
            Cull Off
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attr { float3 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Out { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Out Vert(Attr input)
            {
                Out o;
                float3 origin=TransformObjectToWorld(float3(0,0,0));
                float3 axis=TransformObjectToWorld(float3(0,0,1))-origin;
                float3 center=TransformWorldToView(origin);
                float3 along=mul((float3x3)UNITY_MATRIX_V,axis);
                float scale=length(along);
                float2 projected=along.xy;
                float2 dir=length(projected)>0.0001 ? normalize(projected) : float2(0,1);
                float2 crossAxis=float2(-dir.y,dir.x);
                // Side views see a short tongue. End-on views keep a compact, camera-facing core.
                center.xy+=dir*(input.uv.x-.20)*(scale*.82+length(projected)*1.85)
                         +crossAxis*(input.uv.y-.5)*scale*.88;
                o.positionCS=mul(UNITY_MATRIX_P,float4(center,1)); o.uv=input.uv; return o;
            }
            half4 Frag(Out i):SV_Target
            {
                float x=i.uv.x, y=(i.uv.y-.5)*2;
                float core=exp(-dot(float2((x-.22)*6,y*2.8),float2((x-.22)*6,y*2.8)));
                float tail=saturate(1-x);
                float sides=saturate(1-abs(y)/max(.008,.52*tail))*saturate(x*10)*tail*(.8+.2*tail);
                half3 color=core*half3(1.55,1.14,.58)+sides*sides*half3(.95,.39,.06);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
