using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    static class DanbaidongRenderGraph
    {
        sealed class EmptyShadowPass { }

        internal static TextureHandle EmptyShadowArray(RenderGraph graph)
        {
            var descriptor = new TextureDesc(1, 1)
            {
                name = "Danbaidong empty shadow array",
                dimension = TextureDimension.Tex2DArray,
                slices = 1,
                depthBufferBits = DepthBits.Depth16,
                isShadowMap = true,
                clearBuffer = true,
                clearColor = Color.white
            };
            var texture = graph.CreateTexture(descriptor);
            using var builder = graph.AddRasterRenderPass<EmptyShadowPass>("Clear empty shadow array", out _);
            builder.SetRenderAttachmentDepth(texture, AccessFlags.Write);
            builder.SetRenderFunc(static (EmptyShadowPass _, RasterGraphContext __) => { });
            return texture;
        }
    }
}
