using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

[DisallowMultipleRendererFeature("Camera-FinalColorCopy")]
public class CameraFinalColorCopyRendererFeature : ScriptableRendererFeature
{
    class CustomRenderPass : ScriptableRenderPass
    {
        string _passName = "FinalColorCopy pass";
        Material _blitMaterial;
        int _shaderTextureProperty;
        bool _generateMips;
        RTHandle _destination;

        class PassData
        {
            internal TextureHandle copySourceTexture;
        }

        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            // Records a rendering command to copy, or blit, the contents of the source texture
            // to the color render target of the render pass.
            // The RecordRenderGraph method sets the destination texture as the render target
            // with the UseTextureFragment method.
            Blitter.BlitTexture(context.cmd, data.copySourceTexture,
                new Vector4(1, 1, 0, 0), 0, false);
        }

        public void Setup(Material mat, int shaderTextureProperty, RTHandle destinationTexture, bool generateMips)
        {
            _blitMaterial = mat;
            _shaderTextureProperty = shaderTextureProperty;
            _destination = destinationTexture;
            _generateMips = generateMips;
            requiresIntermediateTexture = true;
        }

        // RecordRenderGraph is where the RenderGraph handle can be accessed, through which render passes can be added to the graph.
        // FrameData is a context container through which URP resources can be accessed and managed.
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if(!_blitMaterial)
                return;

            var resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if(cameraData.renderType == CameraRenderType.Base)// || !cameraData.camera.gameObject.CompareTag("MainCamera"))
                return;

            if(resourceData.isActiveTargetBackBuffer)
            {
                Debug.LogError("$Skipping render pass. CameraFinalColorCopy requires an intermediate ColorTexture, we can't BackBuffer as a texture input");
                return;
            }

            using(var builder = renderGraph.AddRasterRenderPass<PassData>(_passName, out var passData))
            {
                // Populate passData with the data needed by the rendering function
                // of the render pass.
                // Use the camera's active color texture
                // as the source texture for the copy operation.
                passData.copySourceTexture = resourceData.cameraColor;
                //RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                //desc.msaaSamples = 1;
                //desc.depthBufferBits = 0;

                TextureHandle destinationImported = renderGraph.ImportTexture(_destination);

                builder.UseTexture(passData.copySourceTexture);
                builder.SetRenderAttachment(destinationImported, 0);

                builder.SetRenderFunc((PassData data, RasterGraphContext context)
                    => ExecutePass(data, context));
                
                // ------- Set a texture to the global texture (No need because it's already set in Create() method):
                //builder.SetGlobalTextureAfterPass(destinationImported, _shaderTextureProperty);
            }

            //RenderGraphUtils.BlitMaterialParameters para = new(source, destinationImported, _blitMaterial, 0);
            //renderGraph.AddBlitPass(para, passName: _passName);
            //Debug.Log("Perform op");
            //resourceData.cameraColor = destinationImported;
        }
    }

    
    public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingTransparents;
    public Material material;
    public bool GenerateMipMaps;
    public string ShaderTextureProperty = "_CameraFinalColorCopy";

    [Header("Procedurally created RenderTexture:")]
    public RenderTexture RenderTextureInspector;

    CustomRenderPass _scriptablePass;
    RTHandle _renderTextureHandle;

    public RenderTexture ResultRT => _renderTextureHandle?.rt;
    
    /// <inheritdoc/>
    public override void Create()
    {
        _scriptablePass = new CustomRenderPass();

        // Configures where the render pass should be injected.
        _renderTextureHandle = null;
        RenderTextureDescriptor textureProperties = new RenderTextureDescriptor(Screen.width, Screen.height, RenderTextureFormat.Default, 0);
        textureProperties.useMipMap = GenerateMipMaps;
        textureProperties.autoGenerateMips = true;
        if(textureProperties.width < 10 || textureProperties.height < 10)
            return;
        
        RenderingUtils.ReAllocateHandleIfNeeded(ref _renderTextureHandle, textureProperties, FilterMode.Bilinear, TextureWrapMode.Clamp, name: $"CameraColor-{name}");
        Shader.SetGlobalTexture(ShaderTextureProperty, _renderTextureHandle);
        _scriptablePass.renderPassEvent = injectionPoint;
        RenderTextureInspector = _renderTextureHandle.rt;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        _scriptablePass.Setup(material, Shader.PropertyToID(ShaderTextureProperty), _renderTextureHandle, GenerateMipMaps);
        renderer.EnqueuePass(_scriptablePass);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _renderTextureHandle?.Release();
    }
}
