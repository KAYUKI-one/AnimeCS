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
        [MainColor] _BaseTint("Base tint",Color)=(1,1,1,1)
        _LayerTint("Layer tint",Color)=(1,1,1,1)
        _Pigment("Paint color and coverage",Color)=(.56,.57,.60,.045)
        _BandCenter("Light band midpoint", Range(-1,1))=.27
        _BandFeather("Light band feather", Range(.005,1))=.26
        _BandStrength("Light band strength", Range(0,1))=.67
        _ShadeTint("Indirect light tint", Color)=(.83,.91,1,1)
        _ShadeFloor("Minimum indirect light", Color)=(.39,.45,.54,1)
        _BounceTint("Shadow bounce tint", Color)=(.17,.23,.31,1)
        _FootTint("Wall base tint", Color)=(.74,.83,.93,1)
        _FootHeight("Wall gradient base and height (world units)", Vector)=(0,4,0,0)
        _PaintBias("Color texture mip bias", Range(0,4))=1.45
        _NormalDetail("Normal detail", Range(0,1))=.38
        [HDR] _EmissionColor("Emission", Color)=(0,0,0,1)
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
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull",Float)=2
        [HideInInspector] _ZWrite("ZWrite",Float)=1
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        UsePass "Soft Environment/Layered Surface/PAINTEDFORWARD"
        UsePass "Soft Environment/Layered Surface/SHADOWCASTER"
        UsePass "Soft Environment/Layered Surface/DEPTHONLY"
        UsePass "Soft Environment/Layered Surface/DEPTHNORMALS"
        UsePass "Soft Environment/Layered Surface/META"
    }
    FallBack Off
}
