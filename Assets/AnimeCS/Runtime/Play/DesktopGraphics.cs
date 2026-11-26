using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AnimeCS.Play
{
    internal readonly struct PictureQuality
    {
        internal readonly int Scale, Samples, Shadow;
        internal PictureQuality(int scale,int samples,int shadow) { Scale=scale; Samples=samples; Shadow=shadow; }
    }

    internal sealed class DesktopGraphics : IDisposable
    {
        private const string ScaleKey="AnimeCS.Video.Scale", SamplesKey="AnimeCS.Video.MSAA", ShadowKey="AnimeCS.Video.Shadows";
        private readonly RenderPipelineAsset _previous;
        private readonly UniversalRenderPipelineAsset _pipeline;
        private readonly float _shadowDistance;
        internal PictureQuality Current { get; private set; }
        internal PictureQuality Default { get; }
        internal int MaxSamples { get; }
        internal int MaxShadow => Mathf.Min(8192,SystemInfo.maxTextureSize);
        internal bool Supported => _pipeline;

        internal DesktopGraphics()
        {
            _previous=QualitySettings.renderPipeline;
            var source=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!source) return;
            _pipeline=UnityEngine.Object.Instantiate(source);
            _pipeline.name="AnimeCS · 本机画质";
            _shadowDistance=source.shadowDistance;
            MaxSamples=Mathf.Max(1,SystemInfo.GetRenderTextureSupportedMSAASampleCount(
                new RenderTextureDescriptor(128,128,RenderTextureFormat.DefaultHDR,24){msaaSamples=8}));
            Default=new PictureQuality(Mathf.RoundToInt(source.renderScale*100),source.msaaSampleCount,source.mainLightShadowmapResolution);
            Apply(new PictureQuality(PlayerPrefs.GetInt(ScaleKey,Default.Scale),PlayerPrefs.GetInt(SamplesKey,Default.Samples),
                PlayerPrefs.GetInt(ShadowKey,Default.Shadow)),false);
            QualitySettings.renderPipeline=_pipeline;
        }

        internal void Apply(PictureQuality value,bool save)
        {
            if (!_pipeline) return;
            int samples=Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(1,value.Samples)),1,Mathf.Max(1,MaxSamples));
            int shadow=Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(1024,value.Shadow)),1024,MaxShadow);
            Current=new PictureQuality(Mathf.Clamp(value.Scale,75,200),samples,shadow);
            _pipeline.renderScale=Current.Scale*.01f; _pipeline.msaaSampleCount=samples;
            _pipeline.mainLightShadowmapResolution=shadow;
            _pipeline.shadowCascadeCount=shadow<=2048?2:shadow<=4096?3:4;
            _pipeline.cascade3Split=new Vector2(.12f,.40f);
            _pipeline.shadowDistance=shadow>=8192?Mathf.Max(_shadowDistance,95):shadow<Default.Shadow?Mathf.Min(_shadowDistance,50):_shadowDistance;
            if (!save) return;
            PlayerPrefs.SetInt(ScaleKey,Current.Scale);PlayerPrefs.SetInt(SamplesKey,samples);PlayerPrefs.SetInt(ShadowKey,shadow);PlayerPrefs.Save();
        }

        public void Dispose()
        {
            if (!_pipeline) return;
            if (QualitySettings.renderPipeline==_pipeline) QualitySettings.renderPipeline=_previous;
            UnityEngine.Object.Destroy(_pipeline);
        }
    }
}

