Shader "AnimeCS/Hidden garment region"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass { ZWrite Off ColorMask 0 }
    }
}