Shader "AnimeCS/Painted Masonry"
{
    Properties
    {
        [MainTexture] _BaseMap("Base surface",2D)="white"{}
        [Normal] _BumpMap("Base normal",2D)="bump"{}
        _ORM("Base occlusion / roughness / metal",2D)="white"{}
        _Layer("Painted surface",2D)="white"{}
        _LayerSurface("Layer normal and roughness",2D)="white"{}
        _BlendMask("Paint breakup",2D)="white"{}
        _BaseTint("Base tint",Color)=(1,1,1,1)
        _LayerTint("Layer tint",Color)=(1,1,1,1)
        [Toggle(_PAINTED)] _Layered("Vertex painted",Float)=0
        _MetalFactor("Metallic multiplier",Float)=1
        _RoughFactor("Roughness multiplier",Float)=1
        _PaintMode("Blend mask channel",Float)=2
        _Softness("Blend softness",Float)=.1
        _ColorU("Color U",Vector)=(1,0,0,0)
        _ColorV("Color V",Vector)=(0,1,0,0)
        _NormalU("Normal U",Vector)=(1,0,0,0)
        _NormalV("Normal V",Vector)=(0,1,0,0)
        _MaskU("Mask U",Vector)=(1,0,0,0)
        _MaskV("Mask V",Vector)=(0,1,0,0)
        [HideInInspector] _Cull("Cull",Float)=2
        [HideInInspector] _ZWrite("ZWrite",Float)=1
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); TEXTURE2D(_ORM); TEXTURE2D(_Layer);
        TEXTURE2D(_BlendMask); TEXTURE2D(_LayerSurface);
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseTint, _LayerTint;
            float4 _ColorU, _ColorV, _NormalU, _NormalV, _MaskU, _MaskV;
            half _PaintMode, _Softness, _MetalFactor, _RoughFactor;
        CBUFFER_END
        float2 PaintedUV(float2 uv,float4 rowU,float4 rowV)
        {
            float3 source=float3(uv.x,1-uv.y,1);
            return float2(dot(rowU.xyz,source),1-dot(rowV.xyz,source));
        }
        half PaintWeight(float2 uv,half vertexPaint)
        {
            #if !defined(_PAINTED)
                return 0;
            #else
            half4 mask=SAMPLE_TEXTURE2D(_BlendMask,sampler_BaseMap,PaintedUV(uv,_MaskU,_MaskV));
            half center=_PaintMode>2.5h ? mask.a : mask.g;
            half width=max(.012h,_PaintMode<1.5h?mask.r:_Softness);
            half lo=max(0.h,center-width), hi=min(1.h,center+width);
            return smoothstep(lo,max(lo+.001h,hi),vertexPaint);
            #endif
        }
        half3 ReadAlbedo(float2 uv,half blend)
        {
            half3 a=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb*_BaseTint.rgb;
            #if !defined(_PAINTED)
                return a;
            #else
            half3 b=SAMPLE_TEXTURE2D(_Layer,sampler_BaseMap,PaintedUV(uv,_ColorU,_ColorV)).rgb*_LayerTint.rgb;
            return lerp(a,b,blend);
            #endif
        }
        ENDHLSL
        Pass
        {
            Name "PaintedForward"
            Tags {"LightMode"="UniversalForwardOnly"}
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _PAINTED
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct App {float4 pos:POSITION;half3 normal:NORMAL;half4 tangent:TANGENT;float2 uv:TEXCOORD0;float2 lm:TEXCOORD1;half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct Pix {float4 pos:SV_POSITION;float3 ws:TEXCOORD0;half3 normal:TEXCOORD1;half4 tangent:TEXCOORD2;float2 uv:TEXCOORD3;float2 lm:TEXCOORD4;half2 shade:TEXCOORD5; UNITY_VERTEX_OUTPUT_STEREO};
            Pix Vert(App v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Pix o=(Pix)0;UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(v.pos.xyz);
                VertexNormalInputs n=GetVertexNormalInputs(v.normal,v.tangent);
                o.pos=p.positionCS;o.ws=p.positionWS;o.normal=n.normalWS;
                o.tangent=half4(n.tangentWS,v.tangent.w*GetOddNegativeScale());
                o.uv=v.uv;o.lm=v.lm*unity_LightmapST.xy+unity_LightmapST.zw;
                o.shade=half2(v.color.a,ComputeFogFactor(o.pos.z));return o;
            }
            half4 Frag(Pix i):SV_Target
            {
                half blend=PaintWeight(i.uv,i.shade.x);
                half4 orm=SAMPLE_TEXTURE2D(_ORM,sampler_BaseMap,i.uv);
                half4 surface=half4(.5h,.5h,1.h,1.h);
                #if defined(_PAINTED)
                    surface=SAMPLE_TEXTURE2D(_LayerSurface,sampler_BaseMap,PaintedUV(i.uv,_NormalU,_NormalV));
                #endif
                half3 a=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BaseMap,i.uv));
                half3 b=surface.rgb*2.h-1.h;
                half3 ts=normalize(lerp(a,b,blend));
                // Fine engraved or gritty normal detail must converge to the pixel footprint as distance increases.
                float footprint=max(length(ddx(i.uv)),length(ddy(i.uv)));
                ts.xy*=lerp(.74h,.33h,smoothstep(.001f,.018f,footprint));
                half3 geo=normalize(i.normal);
                half3 tangent=i.tangent.xyz;
                half3 n=dot(tangent,tangent)>.25h ? normalize(tangent*ts.x+cross(geo,tangent)*i.tangent.w*ts.y+geo*ts.z) : geo;
                InputData data=(InputData)0;
                data.positionWS=i.ws;data.normalWS=n;data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.ws);
                data.shadowCoord=TransformWorldToShadowCoord(i.ws);data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.pos);
                #ifdef LIGHTMAP_ON
                    data.bakedGI=SampleLightmap(i.lm,n);
                #else
                    data.bakedGI=SampleSH(n);
                #endif
                // A large imported render tile can straddle indoor and outdoor probes. Keep legible sky fill in the darkest masonry.
                data.bakedGI=max(data.bakedGI,half3(.115h,.132h,.154h)*lerp(.8h,1.15h,saturate(geo.y)));
                data.shadowMask=SAMPLE_SHADOWMASK(i.lm);
                SurfaceData s=(SurfaceData)0;
                s.albedo=ReadAlbedo(i.uv,blend);s.alpha=1;s.occlusion=lerp(orm.r,1.h,blend);
                s.metallic=lerp(orm.b*_MetalFactor,0.h,blend);s.normalTS=ts;
                half rough=max(lerp(.39h,.22h,s.metallic),lerp(orm.g*_RoughFactor,surface.a,blend));
                float variance=dot(ddx(n),ddx(n))+dot(ddy(n),ddy(n));
                s.smoothness=1.h-sqrt(saturate(rough*rough+min(.20f,variance*.25f)));
                half4 color=UniversalFragmentPBR(data,s);
                color.rgb=MixFog(color.rgb,i.shade.y);return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        Pass
        {
            Name "Meta"
            Tags {"LightMode"="Meta"}
            Cull Off
            HLSLPROGRAM
            #pragma vertex BakeVertex
            #pragma fragment BakeFragment
            #pragma shader_feature_local _PAINTED
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
            struct BakeIn {float4 pos:POSITION;float2 uv:TEXCOORD0;float2 lm:TEXCOORD1;float2 dyn:TEXCOORD2;half4 color:COLOR;};
            struct BakeOut {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;half weight:TEXCOORD1;};
            BakeOut BakeVertex(BakeIn i)
            {
                BakeOut o;o.pos=MetaVertexPosition(i.pos,i.lm,i.dyn,unity_LightmapST,unity_DynamicLightmapST);o.uv=i.uv;o.weight=i.color.a;return o;
            }
            half4 BakeFragment(BakeOut i):SV_Target
            {
                MetaInput m=(MetaInput)0;m.Albedo=ReadAlbedo(i.uv,PaintWeight(i.uv,i.weight));return MetaFragment(m);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
