using System.Numerics;
using System.Runtime.InteropServices;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using Silk.NET.WebGPU;
using Uno.WinUI.Graphics3DWebGpu;
using GpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ReportScene.Rendering.WebGpu;

/// <summary>
/// Native WebGPU renderer borrowing Uno's device, queue and target. One renderer
/// belongs to one viewport; pipelines and immutable texture caches are shared by
/// all report surfaces on the same device. No readback or full-page texture is used.
/// </summary>
public sealed unsafe class WebGpuReportRenderer : IDisposable
{
    private readonly WebGPU api;
    private readonly Device* device;
    private readonly Queue* queue;
    private GpuDeviceResources? resources;
    private GpuBuffer* uniformBuffer;
    private GpuBuffer* primitiveBuffer;
    private BindGroup* frameBindings;
    private int capacity;
    private WebGpuReportCanvas? commandCanvas;
    private bool disposed;

    public WebGpuRenderStatistics LastStatistics { get; private set; }

    public WebGpuReportRenderer(WebGpuCanvasContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        api = context.Api;
        device = context.Device;
        queue = context.Queue;
        try
        {
            resources = GpuDeviceResources.Acquire(context);
            var descriptor = new BufferDescriptor { Size = 16, Usage = BufferUsage.Uniform | BufferUsage.CopyDst };
            uniformBuffer = api.DeviceCreateBuffer(device, &descriptor);
            if (uniformBuffer == null) throw new InvalidOperationException("WebGPU could not allocate the report frame buffer.");
            EnsureCapacity(256);
        }
        catch { Dispose(); throw; }
    }

    public void Render(WebGpuCanvasContext context, RectD viewport, Action<ReportCanvas> draw)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(draw);
        if (context.Device != device) throw new InvalidOperationException("A report renderer cannot cross WebGPU devices.");
        if (context.TargetView == null || context.PixelWidth <= 0 || context.PixelHeight <= 0
            || !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height)
            || viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentException("A finite, positive viewport and initialized WebGPU target are required.");

        var shared = resources!;
        lock (shared) RenderCore(context, viewport, draw, shared);
    }

    private void RenderCore(WebGpuCanvasContext context, RectD viewport, Action<ReportCanvas> draw, GpuDeviceResources shared)
    {
        shared.Textures.BeginFrame();
        var canvas = commandCanvas ??= new WebGpuReportCanvas(shared.Textures, viewport, context.PixelWidth, context.PixelHeight);
        canvas.Reset(viewport, context.PixelWidth, context.PixelHeight);
        draw(canvas);
        canvas.ValidateBalancedState();
        EnsureCapacity(Math.Max(1, canvas.Primitives.Count));

        Vector4 frame = new((float)viewport.Width, (float)viewport.Height,
            (float)(context.PixelWidth / viewport.Width), (float)(context.PixelHeight / viewport.Height));
        api.QueueWriteBuffer(queue, uniformBuffer, 0, &frame, 16);
        var primitives = CollectionsMarshal.AsSpan(canvas.Primitives);
        ulong bytes = checked((ulong)primitives.Length * (ulong)sizeof(GpuPrimitive));
        if (!primitives.IsEmpty)
            fixed (GpuPrimitive* data = primitives)
                api.QueueWriteBuffer(queue, primitiveBuffer, 0, data, (nuint)bytes);

        CommandEncoder* encoder = null;
        RenderPassEncoder* pass = null;
        CommandBuffer* commands = null;
        try
        {
            var encoderDescriptor = new CommandEncoderDescriptor();
            encoder = api.DeviceCreateCommandEncoder(device, &encoderDescriptor);
            if (encoder == null) throw new InvalidOperationException("WebGPU command encoder allocation failed.");
            var attachment = new RenderPassColorAttachment
            {
                View = context.TargetView,
                DepthSlice = uint.MaxValue, // WGPU_DEPTH_SLICE_UNDEFINED for a 2D target.
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color(0, 0, 0, 0)
            };
            var descriptor = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &attachment };
            pass = api.CommandEncoderBeginRenderPass(encoder, &descriptor);
            if (pass == null) throw new InvalidOperationException("WebGPU report render pass creation failed.");
            api.RenderPassEncoderSetPipeline(pass, shared.Pipeline);
            api.RenderPassEncoderSetBindGroup(pass, 0, frameBindings, 0, (uint*)null);
            foreach (var batch in canvas.Batches)
            {
                api.RenderPassEncoderSetBindGroup(pass, 1, batch.Texture.BindGroup, 0, (uint*)null);
                api.RenderPassEncoderDraw(pass, 6, (uint)batch.Count, 0, (uint)batch.First);
            }
            api.RenderPassEncoderEnd(pass);
            api.RenderPassEncoderRelease(pass);
            pass = null;
            var commandDescriptor = new CommandBufferDescriptor();
            commands = api.CommandEncoderFinish(encoder, &commandDescriptor);
            if (commands == null) throw new InvalidOperationException("WebGPU report command recording failed.");
            api.QueueSubmit(queue, 1, &commands);
            LastStatistics = new(primitives.Length, canvas.Batches.Count, shared.Textures.TextureUploads,
                checked((long)bytes + 16 + shared.Textures.UploadedBytes), shared.Textures.AtlasPages)
            {
                MaximumTextureDimension = shared.Textures.MaximumTextureDimension,
                ImageTileSize = shared.Textures.ImageTileSize,
                CachedImageBytes = shared.Textures.RetainedImageBytes,
                CachedPathMaskBytes = shared.Textures.RetainedMaskBytes,
                ImageStagingBytes = shared.Textures.RetainedStagingBytes,
                TextShapingOperations = canvas.TextShapingOperations,
                TextBoundsQueries = canvas.TextBoundsQueries,
                TextFontCreations = canvas.TextFontCreations,
                GlyphCacheHits = shared.Textures.GlyphCacheHits,
                GlyphRasterizations = shared.Textures.GlyphRasterizations
            };
        }
        finally
        {
            if (pass != null) api.RenderPassEncoderRelease(pass);
            if (commands != null) api.CommandBufferRelease(commands);
            if (encoder != null) api.CommandEncoderRelease(encoder);
        }
    }

    private void EnsureCapacity(int required)
    {
        if (required <= capacity) return;
        if (required > WebGpuReportCanvas.MaximumPrimitives)
            throw new InvalidOperationException("Visible report geometry exceeds the WebGPU primitive budget.");
        int next = 256;
        while (next < required) next = checked(next * 2);
        ulong bytes = checked((ulong)next * (ulong)sizeof(GpuPrimitive));
        var descriptor = new BufferDescriptor { Size = bytes, Usage = BufferUsage.Storage | BufferUsage.CopyDst };
        GpuBuffer* replacement = api.DeviceCreateBuffer(device, &descriptor);
        if (replacement == null) throw new InvalidOperationException("WebGPU report geometry allocation failed.");
        BindGroup* bindings = null;
        try
        {
            BindGroupEntry* entries = stackalloc BindGroupEntry[2];
            entries[0] = new() { Binding = 0, Buffer = uniformBuffer, Size = 16 };
            entries[1] = new() { Binding = 1, Buffer = replacement, Size = bytes };
            var group = new BindGroupDescriptor { Layout = resources!.FrameLayout, EntryCount = 2, Entries = entries };
            bindings = api.DeviceCreateBindGroup(device, &group);
            if (bindings == null) throw new InvalidOperationException("WebGPU frame bindings could not be created.");
        }
        catch
        {
            if (bindings != null) api.BindGroupRelease(bindings);
            api.BufferRelease(replacement);
            throw;
        }
        if (frameBindings != null) api.BindGroupRelease(frameBindings);
        if (primitiveBuffer != null) api.BufferRelease(primitiveBuffer);
        primitiveBuffer = replacement;
        frameBindings = bindings;
        capacity = next;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (frameBindings != null) { api.BindGroupRelease(frameBindings); frameBindings = null; }
        if (primitiveBuffer != null) { api.BufferRelease(primitiveBuffer); primitiveBuffer = null; }
        if (uniformBuffer != null) { api.BufferRelease(uniformBuffer); uniformBuffer = null; }
        commandCanvas = null;
        resources?.Release();
        resources = null;
    }
}

/// <summary>Device-scoped immutable pipeline state; no application device is created or disposed here.</summary>
internal sealed unsafe class GpuDeviceResources
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(nint Device, TextureFormat Format), GpuDeviceResources> Shared = new();
    private readonly (nint Device, TextureFormat Format) key;
    private readonly WebGPU api;
    private int references;
    public RenderPipeline* Pipeline;
    public BindGroupLayout* FrameLayout;
    private BindGroupLayout* textureLayout;
    private PipelineLayout* pipelineLayout;
    private Sampler* sampler;
    public GpuTextureCache Textures { get; private set; } = null!;

    public static GpuDeviceResources Acquire(WebGpuCanvasContext context)
    {
        lock (Gate)
        {
            var key = ((nint)context.Device, context.TargetFormat);
            if (!Shared.TryGetValue(key, out var resource))
            {
                resource = new(context);
                Shared.Add(key, resource);
            }
            resource.references++;
            return resource;
        }
    }

    private GpuDeviceResources(WebGpuCanvasContext context)
    {
        key = ((nint)context.Device, context.TargetFormat);
        api = context.Api;
        ShaderModule* shader = null;
        nint code = 0, vertexName = 0, fragmentName = 0;
        try
        {
            BindGroupLayoutEntry* frameEntries = stackalloc BindGroupLayoutEntry[2];
            frameEntries[0] = new()
            {
                Binding = 0, Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = 16 }
            };
            frameEntries[1] = new()
            {
                Binding = 1, Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = 128 }
            };
            var frameDescriptor = new BindGroupLayoutDescriptor { EntryCount = 2, Entries = frameEntries };
            FrameLayout = api.DeviceCreateBindGroupLayout(context.Device, &frameDescriptor);
            BindGroupLayoutEntry* textureEntries = stackalloc BindGroupLayoutEntry[2];
            textureEntries[0] = new()
            {
                Binding = 0, Visibility = ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering }
            };
            textureEntries[1] = new()
            {
                Binding = 1, Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.Dimension2D }
            };
            var textureDescriptor = new BindGroupLayoutDescriptor { EntryCount = 2, Entries = textureEntries };
            textureLayout = api.DeviceCreateBindGroupLayout(context.Device, &textureDescriptor);
            if (FrameLayout == null || textureLayout == null) throw new InvalidOperationException("WebGPU report resource layouts could not be created.");
            BindGroupLayout** layouts = stackalloc BindGroupLayout*[2];
            layouts[0] = FrameLayout; layouts[1] = textureLayout;
            var layoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 2, BindGroupLayouts = layouts };
            pipelineLayout = api.DeviceCreatePipelineLayout(context.Device, &layoutDescriptor);

            using var stream = typeof(WebGpuReportRenderer).Assembly.GetManifestResourceStream("ReportScene.Rendering.WebGpu.Report2D.wgsl")
                ?? throw new InvalidOperationException("The report WGSL shader resource is missing.");
            using var reader = new StreamReader(stream);
            code = Marshal.StringToCoTaskMemUTF8(reader.ReadToEnd());
            var wgsl = new ShaderModuleWGSLDescriptor { Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor }, Code = (byte*)code };
            var module = new ShaderModuleDescriptor { NextInChain = &wgsl.Chain };
            shader = api.DeviceCreateShaderModule(context.Device, &module);
            if (shader == null) throw new InvalidOperationException("WebGPU report shader compilation failed.");
            vertexName = Marshal.StringToCoTaskMemUTF8("vertex_main");
            fragmentName = Marshal.StringToCoTaskMemUTF8("fragment_main");
            var blend = new BlendState
            {
                Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
                Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha }
            };
            var colorTarget = new ColorTargetState { Format = context.TargetFormat, Blend = &blend, WriteMask = ColorWriteMask.All };
            var fragment = new FragmentState { Module = shader, EntryPoint = (byte*)fragmentName, TargetCount = 1, Targets = &colorTarget };
            var descriptor = new RenderPipelineDescriptor
            {
                Layout = pipelineLayout,
                Vertex = new VertexState { Module = shader, EntryPoint = (byte*)vertexName },
                Fragment = &fragment,
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue }
            };
            Pipeline = api.DeviceCreateRenderPipeline(context.Device, &descriptor);
            if (Pipeline == null) throw new InvalidOperationException("WebGPU report pipeline creation failed.");
            var samplerDescriptor = new SamplerDescriptor
            {
                AddressModeU = AddressMode.ClampToEdge, AddressModeV = AddressMode.ClampToEdge, AddressModeW = AddressMode.ClampToEdge,
                MagFilter = FilterMode.Linear, MinFilter = FilterMode.Linear, MipmapFilter = MipmapFilterMode.Linear,
                LodMinClamp = 0, LodMaxClamp = 32, MaxAnisotropy = 1
            };
            sampler = api.DeviceCreateSampler(context.Device, &samplerDescriptor);
            if (sampler == null) throw new InvalidOperationException("WebGPU report sampler creation failed.");
            Textures = new(context, textureLayout, sampler);
        }
        catch { DisposeResources(); throw; }
        finally
        {
            if (shader != null) api.ShaderModuleRelease(shader);
            if (code != 0) Marshal.FreeCoTaskMem(code);
            if (vertexName != 0) Marshal.FreeCoTaskMem(vertexName);
            if (fragmentName != 0) Marshal.FreeCoTaskMem(fragmentName);
        }
    }

    public void Release()
    {
        lock (Gate)
        {
            if (--references != 0) return;
            Shared.Remove(key);
            DisposeResources();
        }
    }

    private void DisposeResources()
    {
        Textures?.Dispose();
        if (sampler != null) { api.SamplerRelease(sampler); sampler = null; }
        if (Pipeline != null) { api.RenderPipelineRelease(Pipeline); Pipeline = null; }
        if (pipelineLayout != null) { api.PipelineLayoutRelease(pipelineLayout); pipelineLayout = null; }
        if (textureLayout != null) { api.BindGroupLayoutRelease(textureLayout); textureLayout = null; }
        if (FrameLayout != null) { api.BindGroupLayoutRelease(FrameLayout); FrameLayout = null; }
    }
}
