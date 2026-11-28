Shader "AnimeCS/Character Surface"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo",2D)="white"{}
        [MainColor] _BaseColor("Tint",Color)=(1,1,1,1)
        [Normal] _BumpMap("Normal",2D)="bump"{}
        _BumpScale("Normal strength", Range(0,2))=.65
        _Smoothness("Highlight focus",Range(0,1))=.34
        _Metallic("Metallic",Range(0,1))=0
        _SpecColor("Highlight tint",Color)=(.12,.12,.12,1)
        _ClearCoatMask("Hair sheen",Range(0,1))=0
        _ClearCoatSmoothness("Face shadow softness",Range(0,1))=0
        _Cutoff("Alpha threshold",Range(0,1))=.38
        [HideInInspector] _AlphaToMask("MSAA coverage",Float)=0
        [HideInInspector] _Surface("Surface",Float)=0
        [HideInInspector] _SrcBlend("Source",Float)=1
        [HideInInspector] _DstBlend("Destination",Float)=0
        [HideInInspector] _ZWrite("Depth write",Float)=1
        [HideInInspector] _Cull("Culling",Float)=0
        [PerRendererData] _FriendlyRim("Visible teammate", Float)=0
        [HideInInspector] _EmissionColor("Emission",Color)=(0,0,0,1)
        [HideInInspector] _EmissionMap("Emission map",2D)="white"{}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "UniversalMaterialType"="Lit" }
        Pass
        {
            Name "CharacterForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            AlphaToMask [_AlphaToMask]
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex LitPassVertex
            #pragma fragment CharacterFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/LitForwardPass.hlsl"

            UNITY_INSTANCING_BUFFER_START(FactionCue)
                UNITY_DEFINE_INSTANCED_PROP(float,_FriendlyRim)
            UNITY_INSTANCING_BUFFER_END(FactionCue)

            half4 CharacterFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SurfaceData s;
                InitializeStandardLitSurfaceData(input.uv,s);
                InputData inputData;
                InitializeInputData(input,s.normalTS,inputData);
                InitializeBakedGIData(input,inputData);
                Light key=GetMainLight(inputData.shadowCoord,inputData.positionWS,inputData.shadowMask);
                half3 n=inputData.normalWS,v=inputData.viewDirectionWS;
                half nl=dot(n,key.direction);
                half width=max(fwidth(nl)*1.5h,lerp(.15h,.29h,_ClearCoatSmoothness));
                half band=smoothstep(-.055h-width,-.055h+width,nl);
                half vis=key.shadowAttenuation*key.distanceAttenuation;
                half3 ambient=max(inputData.bakedGI,half3(.055h,.065h,.09h));
                half3 shadowTint=lerp(half3(.80h,.85h,1.h),half3(1.h,.86h,.90h),_ClearCoatSmoothness);
                half3 lighting=shadowTint*(.23h+.58h*ambient)+key.color*vis*(.07h+.46h*band+.12h*saturate(nl));
                uint count=GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light fill=GetAdditionalLight(lightIndex,inputData.positionWS);
                    lighting+=fill.color*fill.distanceAttenuation*fill.shadowAttenuation*saturate(dot(n,fill.direction))*.55h;
                LIGHT_LOOP_END
                half3 h=SafeNormalize(v+key.direction);
                half highlight=pow(saturate(dot(n,h)),lerp(13.h,98.h,_Smoothness));
                half sheen=smoothstep(.43h,.81h,highlight);
                half3 tangent=normalize(cross(n,abs(n.y)<.95h?half3(0,1,0):half3(1,0,0)));
                half hair=pow(saturate(1.h-abs(dot(tangent,h))),18.h)*saturate(nl+.1h);
                half rim=pow(1.h-saturate(dot(n,v)),3.7h)*saturate(nl+.18h);
                half3 accent=lerp(_SpecColor.rgb,s.albedo*.55h,_Metallic);
                half3 color=s.albedo*lighting*(1.h-.22h*_Metallic)+key.color*vis*(accent*(.28h*highlight+.32h*sheen)+
                    _ClearCoatMask*.07h*hair+rim*.04h);
                half ally=UNITY_ACCESS_INSTANCED_PROP(FactionCue,_FriendlyRim);
                half edge=smoothstep(.65h,.99h,1.h-abs(dot(normalize(input.normalWS),v)));
                color+=ally*edge*half3(.026h,.20h,.265h);
                return half4(MixFog(color,inputData.fogCoord),s.alpha);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DOTS.hlsl"
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma shader_feature_local _ALPHATEST_ON
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DOTS.hlsl"
            #pragma shader_feature_local _ALPHATEST_ON
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DOTS.hlsl"
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/RenderingLayers.hlsl"
            #pragma shader_feature_local _ALPHATEST_ON
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex UniversalVertexMeta
            #pragma fragment UniversalFragmentMetaLit
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature EDITOR_VISUALIZATION
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/LitMetaPass.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
