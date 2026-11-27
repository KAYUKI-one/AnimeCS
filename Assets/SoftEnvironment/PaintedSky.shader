Shader "Soft Environment/Painted Sky"
{
    Properties
    {
        _Zenith("Zenith", Color)=(.36,.63,.87,1)
        _Horizon("Horizon", Color)=(.83,.87,.92,1)
        [NoScaleOffset] _Panorama("Painted upper hemisphere", 2D)="gray"{}
        _Artwork("Panorama opacity", Range(0,1))=.78
        _Saturation("Panorama saturation", Range(0,1.5))=.76
        _ElevationScale("Cloud elevation scale", Range(.5,2))=1.28
        _Rotation("Panorama rotation (degrees)", Range(0,360))=32
        _SunTint("Sunlight", Color)=(1,.88,.68,1)
        _SunRadius("Sun radius (degrees)", Range(.1,3))=.8
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Horizon, _SunTint;
                float _Artwork, _Saturation, _ElevationScale, _Rotation, _SunRadius;
            CBUFFER_END
            TEXTURE2D(_Panorama);
            SAMPLER(sampler_Panorama);
            struct SkyVertex { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            SkyVertex Vert(float3 vertex : POSITION)
            {
                SkyVertex o;
                o.positionCS = TransformObjectToHClip(vertex);
                o.direction = vertex;
                return o;
            }
            half3 ReadPanorama(float u, float v)
            {
                // Crossfade the ends of the painting instead of requiring a specially tiled source.
                const float overlap = .065;
                float2 uv = float2(frac(u) * (1-overlap), v);
                float2 dx = ddx(float2(u,v)), dy = ddy(float2(u,v));
                dx.x = (dx.x-round(dx.x)) * (1-overlap);
                dy.x = (dy.x-round(dy.x)) * (1-overlap);
                half3 color = SAMPLE_TEXTURE2D_GRAD(_Panorama,sampler_Panorama,uv,dx,dy).rgb;
                if (uv.x < overlap)
                {
                    half3 edge = SAMPLE_TEXTURE2D_GRAD(_Panorama,sampler_Panorama,uv+float2(1-overlap,0),dx,dy).rgb;
                    color = lerp(edge,color,smoothstep(0,overlap,uv.x));
                }
                return lerp(Luminance(color),color,_Saturation);
            }
            half4 Frag(SkyVertex i) : SV_Target
            {
                float3 ray = normalize(i.direction);
                float height = saturate(ray.y);
                half3 sky = lerp(_Horizon.rgb, _Zenith.rgb, sqrt(height));
                float u = atan2(ray.z,ray.x) / TWO_PI + _Rotation / 360;
                float v = saturate(asin(height) * (2/PI) * _ElevationScale);
                half3 painting = ReadPanorama(u,v);
                float fade = smoothstep(-.035,.08,ray.y) * (1-smoothstep(.82,1,v));
                sky = lerp(sky,painting,_Artwork * fade);
                float facing = saturate(dot(ray, GetMainLight().direction));
                sky += _SunTint.rgb * (.19 * pow(facing, 16) + 1.4 * smoothstep(cos(radians(_SunRadius+.12)), cos(radians(_SunRadius)), facing));
                return half4(sky,1);
            }
            ENDHLSL
        }
    }
}
