using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Game.Core.Utilities;
using OpenTK.Core.Native;
using OpenTK.Graphics;
using OpenTK.Graphics.Vulkan;
using OpenTK.Mathematics;
using OpenTK.Platform;

namespace Game.Core.Rendering;

struct QueueFamilyIndices
{

    public uint? GraphicsFamily;
    public uint? PresentFamily;

    public bool IsCompatible => GraphicsFamily.HasValue && PresentFamily.HasValue;

}

struct SwapchainSupportInfo
{

    public VkSurfaceCapabilitiesKHR Capabilities;
    public List<VkSurfaceFormatKHR> Formats = new();
    public List<VkPresentModeKHR> PresentModes = new();

    public SwapchainSupportInfo() {}

}
public unsafe class Renderer
{

    private static VkInstance _instance;
    private static VkPhysicalDevice _physicalDevice;
    private static VkDevice _device;
    private static VkQueue _graphicsQueue;
    private static QueueFamilyIndices _queueFamilies;
    private static SwapchainSupportInfo _swapchainSupportInfo;
    private static VkQueue _presentQueue;
    private static VkSurfaceKHR _surface;
    private static VkSwapchainKHR _swapchain;
    private static List<VkImage> _swapchainImages = new();
    private static List<VkImageView> _swapchainImageViews = new();
    private static List<VkFramebuffer> _swapchainFramebuffers = new();
    private static VkFormat _swapchainFormat;
    private static VkExtent2D _swapchainExtent;
    private static VkRenderPass _renderPass;
    private static VkPipelineLayout _pipelineLayout;
    private static VkPipeline _graphicsPipeline;
    private static VkCommandPool _commandPool;
    private static VkCommandBuffer _commandBuffer;
    private static VkSemaphore _imageAvailableSemaphore;
    private static VkSemaphore _renderFinishedSemaphore;
    private static VkFence _inFlightFence;
    private static VkResult _result;

    public static void Init(WindowHandle window)
    {

        VKLoader.Init();

        GameLogger.Log("Creating Vulkan instance.");
        VkApplicationInfo appInfo = new VkApplicationInfo();
        appInfo.sType = VkStructureType.StructureTypeApplicationInfo;
        fixed (byte* name = "Game in Vulkan"u8) appInfo.pApplicationName = name;
        appInfo.applicationVersion = Vk.MAKE_API_VERSION(0, 1, 0, 0);
        fixed (byte* name = "No Engine"u8) appInfo.pEngineName = name;
        appInfo.engineVersion = Vk.MAKE_API_VERSION(0, 1, 0, 0);
        appInfo.apiVersion = Vk.VK_API_VERSION_1_3;

        VkInstanceCreateInfo instanceCreateInfo = new VkInstanceCreateInfo();
        instanceCreateInfo.sType = VkStructureType.StructureTypeInstanceCreateInfo;
        instanceCreateInfo.pApplicationInfo = &appInfo;

        ReadOnlySpan<string> requiredExtensions = Toolkit.Vulkan.GetRequiredInstanceExtensions();
        byte** requiredExtensionsPtr = MarshalTk.MarshalStringArrayToAnsiStringArrayPtr(requiredExtensions, out uint requiredExtensionsCount);
        instanceCreateInfo.enabledExtensionCount = requiredExtensionsCount;
        instanceCreateInfo.ppEnabledExtensionNames = requiredExtensionsPtr;

        GameLogger.Log("Enabling validation layers.");
        ReadOnlySpan<string> validationLayers = [ "VK_LAYER_KHRONOS_validation" ];

        uint layerPropertyCount;
        Vk.EnumerateInstanceLayerProperties(&layerPropertyCount, null);
        VkLayerProperties* availableLayers = stackalloc VkLayerProperties[(int)layerPropertyCount];
        Vk.EnumerateInstanceLayerProperties(&layerPropertyCount, availableLayers);

        bool foundValidationLayers = false;
        foreach (string layerName in validationLayers)
        {

            for (int i = 0; i < layerPropertyCount; i++)
            {

                ReadOnlySpan<byte> availableLayerName = availableLayers[i].layerName;
                availableLayerName = availableLayerName.Slice(0, availableLayerName.IndexOf((byte)0));
                if (layerName == Encoding.UTF8.GetString(availableLayerName))
                {
                    foundValidationLayers = true;
                    break;
                }

            }

        }

        byte** validationLayersPtr = MarshalTk.MarshalStringArrayToAnsiStringArrayPtr(validationLayers, out uint count);

        if (foundValidationLayers)
        {
            GameLogger.Log("Found validation layers.");
            instanceCreateInfo.enabledLayerCount = count;
            instanceCreateInfo.ppEnabledLayerNames = validationLayersPtr;
        } else
        {
            GameLogger.Log("Did not find validation layers.", SeverityType.Warning);
            instanceCreateInfo.enabledLayerCount = 0;
        }

        VkInstance instance;
        _result = Vk.CreateInstance(&instanceCreateInfo, null, &instance);
        if (_result != VkResult.Success) GameLogger.Throw($"Error creating Vulkan instance with error {_result}");
        _instance = instance;

        VKLoader.SetInstance(_instance);

        GameLogger.Log("Selecting physical device.");
        uint physicalDeviceCount;
        Vk.EnumeratePhysicalDevices(_instance, &physicalDeviceCount, null);
        if (physicalDeviceCount == 0) GameLogger.Throw("Could not find a physical device with Vulkan support.");
        VkPhysicalDevice* physicalDevices = stackalloc VkPhysicalDevice[(int)physicalDeviceCount];
        Vk.EnumeratePhysicalDevices(_instance, &physicalDeviceCount, physicalDevices);
        for (int i = 0; i < physicalDeviceCount; i++)
        {

            VkPhysicalDeviceProperties deviceProperties;
            Vk.GetPhysicalDeviceProperties(physicalDevices[i], &deviceProperties);

            VkPhysicalDeviceFeatures deviceFeatures;
            Vk.GetPhysicalDeviceFeatures(physicalDevices[i], &deviceFeatures);

            if (deviceProperties.deviceType == VkPhysicalDeviceType.PhysicalDeviceTypeDiscreteGpu || deviceProperties.deviceType == VkPhysicalDeviceType.PhysicalDeviceTypeIntegratedGpu)
            {

                ReadOnlySpan<byte> deviceName = deviceProperties.deviceName;
                deviceName = deviceName.Slice(0, deviceName.IndexOf((byte)0));
                GameLogger.Log($"Found device {Encoding.UTF8.GetString(deviceName)}");

                _physicalDevice = physicalDevices[i];
                break;

            }

        }

        GameLogger.Log("Creating window surface.");
        _result = Toolkit.Vulkan.CreateWindowSurface(_instance, window, null, out _surface);
        if (_result != VkResult.Success) GameLogger.Throw($"Error creating window surface with error {_result}");

        GameLogger.Log("Getting required queue families.");
        uint queueFamilyCount;
        Vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, &queueFamilyCount, null);
        VkQueueFamilyProperties* queueFamilies = stackalloc VkQueueFamilyProperties[(int)queueFamilyCount];
        Vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, &queueFamilyCount, queueFamilies);
        _queueFamilies = new QueueFamilyIndices();
        for (int i = 0; i < queueFamilyCount; i++)
        {

            if ((queueFamilies[i].queueFlags & VkQueueFlagBits.QueueGraphicsBit) == VkQueueFlagBits.QueueGraphicsBit)
            {

                _queueFamilies.GraphicsFamily = (uint) i;

            } else
            {
                int isPresentSupported;
                Vk.GetPhysicalDeviceSurfaceSupportKHR(_physicalDevice, (uint) i, _surface, &isPresentSupported);
                if (isPresentSupported == 1)
                {
                    _queueFamilies.PresentFamily = (uint) i;
                }
            }

            if (_queueFamilies.IsCompatible) break;

        }

        GameLogger.Log("Getting required extensions");
        ReadOnlySpan<string> requiredDeviceExtensions = [ "VK_KHR_swapchain" ];
        uint extensionCount;
        Vk.EnumerateDeviceExtensionProperties(_physicalDevice, null, &extensionCount, null);
        VkExtensionProperties* extensionProperties = stackalloc VkExtensionProperties[(int)extensionCount];
        Vk.EnumerateDeviceExtensionProperties(_physicalDevice, null, &extensionCount, extensionProperties);
        List<string> supportedDeviceExtensions = new();
        for (int i = 0; i < extensionCount; i++)
        {

            ReadOnlySpan<byte> extensionName = extensionProperties[i].extensionName;
            extensionName = extensionName.Slice(0, extensionName.IndexOf((byte)0));
            supportedDeviceExtensions.Add(Encoding.UTF8.GetString(extensionName));

        }

        bool hasRequiredExtensions = false;
        foreach (string requestedExtension in requiredDeviceExtensions)
        {

            if (supportedDeviceExtensions.Contains(requestedExtension))
            {
                hasRequiredExtensions = true;
            } else
            {
                hasRequiredExtensions = false;
            }

        }

        if (!hasRequiredExtensions) GameLogger.Throw("Device does not have the required extensions needed.");

        if (!_queueFamilies.IsCompatible) GameLogger.Throw("The physical device needs a graphics queue when it doesn't");

        GameLogger.Log("Creating the logical device.");
        List<VkDeviceQueueCreateInfo> queueCreateInfos = new();
        List<uint> queueFamilyIndices = [ _queueFamilies.GraphicsFamily.Value, _queueFamilies.PresentFamily.Value ];

        float queuePriority = 1.0f;
        foreach (uint queueFamilyIndex in queueFamilyIndices)
        {

            VkDeviceQueueCreateInfo queueCreateInfo = new VkDeviceQueueCreateInfo();
            queueCreateInfo.sType = VkStructureType.StructureTypeDeviceQueueCreateInfo;
            queueCreateInfo.queueFamilyIndex = queueFamilyIndex;
            queueCreateInfo.queueCount = 1;
            queueCreateInfo.pQueuePriorities = &queuePriority;
            queueCreateInfos.Add(queueCreateInfo);

        }

        VkDeviceQueueCreateInfo* queueFamiliesPtr = stackalloc VkDeviceQueueCreateInfo[queueCreateInfos.Count];
        for (int i = 0; i < queueCreateInfos.Count; i++) queueFamiliesPtr[i] = queueCreateInfos[i];

        VkPhysicalDeviceFeatures features = new VkPhysicalDeviceFeatures();

        VkDeviceCreateInfo deviceCreateInfo = new VkDeviceCreateInfo();
        deviceCreateInfo.sType = VkStructureType.StructureTypeDeviceCreateInfo;
        deviceCreateInfo.pQueueCreateInfos = queueFamiliesPtr;
        deviceCreateInfo.queueCreateInfoCount = (uint) queueCreateInfos.Count;
        deviceCreateInfo.pEnabledFeatures = &features;

        byte** requiredDeviceExtensionsPtrs = MarshalTk.MarshalStringArrayToAnsiStringArrayPtr(requiredDeviceExtensions, out uint requiredDeviceExtensionCount);

        deviceCreateInfo.enabledExtensionCount = requiredDeviceExtensionCount;
        deviceCreateInfo.ppEnabledExtensionNames = requiredDeviceExtensionsPtrs;
        if (foundValidationLayers)
        {
            deviceCreateInfo.enabledLayerCount = count;
            deviceCreateInfo.ppEnabledLayerNames = validationLayersPtr;
        } else
        {
            deviceCreateInfo.enabledLayerCount = 0;
        }

        SwapchainSupportInfo swapchainSupportInfo = new SwapchainSupportInfo();
        Vk.GetPhysicalDeviceSurfaceCapabilitiesKHR(_physicalDevice, _surface, &swapchainSupportInfo.Capabilities);

        uint surfaceFormatCount;
        Vk.GetPhysicalDeviceSurfaceFormatsKHR(_physicalDevice, _surface, &surfaceFormatCount, null);
        VkSurfaceFormatKHR* surfaceFormatsPtr = stackalloc VkSurfaceFormatKHR[(int)surfaceFormatCount];
        Vk.GetPhysicalDeviceSurfaceFormatsKHR(_physicalDevice, _surface, &surfaceFormatCount, surfaceFormatsPtr);
        for (int i = 0; i < surfaceFormatCount; i++)
        {
            swapchainSupportInfo.Formats.Add(surfaceFormatsPtr[i]);
        }

        uint presentModeCount;
        Vk.GetPhysicalDeviceSurfacePresentModesKHR(_physicalDevice, _surface, &presentModeCount, null);
        VkPresentModeKHR* presentModesPtr = stackalloc VkPresentModeKHR[(int)presentModeCount];
        Vk.GetPhysicalDeviceSurfacePresentModesKHR(_physicalDevice, _surface, &presentModeCount, presentModesPtr);
        for (int i = 0; i < presentModeCount; i++)
        {
            swapchainSupportInfo.PresentModes.Add(presentModesPtr[i]);
        }

        bool swapchainIsGood = swapchainSupportInfo.Formats.Count != 0 && swapchainSupportInfo.PresentModes.Count != 0;
        if (!swapchainIsGood) GameLogger.Throw("Swapchain doesnt have required things.");

        VkSurfaceFormatKHR surfaceFormat = new VkSurfaceFormatKHR();
        VkPresentModeKHR surfacePresentMode = new VkPresentModeKHR();

        GameLogger.Log("Finding the best swapchain format.");
        foreach (VkSurfaceFormatKHR format in swapchainSupportInfo.Formats)
        {
            if (format.format == VkFormat.FormatB8g8r8a8Srgb && format.colorSpace == VkColorSpaceKHR.ColorspaceSrgbNonlinearKhr) 
            {
                surfaceFormat = format;
                break;
            }
        }

        GameLogger.Log("Finding the best swapchain present mode.");
        // dont care abt this yet
        surfacePresentMode = VkPresentModeKHR.PresentModeFifoKhr;

        VkExtent2D extent;
        if (swapchainSupportInfo.Capabilities.currentExtent.width != uint.MaxValue)
        {
            extent = swapchainSupportInfo.Capabilities.currentExtent;
        } else 
        {

            Toolkit.Window.GetFramebufferSize(window, out Vector2i size);
            extent.width = (uint) Math.Clamp(size.X, swapchainSupportInfo.Capabilities.minImageExtent.width, swapchainSupportInfo.Capabilities.maxImageExtent.width);
            extent.height = (uint) Math.Clamp(size.Y, swapchainSupportInfo.Capabilities.minImageExtent.height, swapchainSupportInfo.Capabilities.maxImageExtent.height);

        }

        VkDevice device;
        _result = Vk.CreateDevice(_physicalDevice, &deviceCreateInfo, null, &device);
        _device = device;
        if (_result != VkResult.Success) GameLogger.Throw($"Error creating device with error {_result}");

        VkQueue graphicsQueue;
        VkQueue presentQueue;
        Vk.GetDeviceQueue(_device, _queueFamilies.GraphicsFamily.Value, 0, &graphicsQueue);
        Vk.GetDeviceQueue(_device, _queueFamilies.PresentFamily.Value, 0, &presentQueue);
        _graphicsQueue = graphicsQueue;
        _presentQueue = presentQueue;

        uint imageCount = swapchainSupportInfo.Capabilities.minImageCount + 1;
        VkSwapchainCreateInfoKHR swapchainCreateInfo = new VkSwapchainCreateInfoKHR();
        swapchainCreateInfo.sType = VkStructureType.StructureTypeSwapchainCreateInfoKhr;
        swapchainCreateInfo.surface = _surface;
        swapchainCreateInfo.minImageCount = imageCount;
        swapchainCreateInfo.imageFormat = surfaceFormat.format;
        swapchainCreateInfo.imageColorSpace = surfaceFormat.colorSpace;
        swapchainCreateInfo.imageExtent = extent;
        swapchainCreateInfo.imageArrayLayers = 1;
        swapchainCreateInfo.imageUsage = VkImageUsageFlagBits.ImageUsageColorAttachmentBit;

        swapchainCreateInfo.imageSharingMode = VkSharingMode.SharingModeConcurrent;
        swapchainCreateInfo.queueFamilyIndexCount = 2;
        uint* queueFamilyIndicesPtr = stackalloc uint[2];
        for (int i = 0; i < queueFamilyIndices.Count; i++)
        {
            queueFamilyIndicesPtr[i] = queueFamilyIndices[i];
        }
        swapchainCreateInfo.pQueueFamilyIndices = queueFamilyIndicesPtr;
        swapchainCreateInfo.preTransform = swapchainSupportInfo.Capabilities.currentTransform;
        swapchainCreateInfo.compositeAlpha = VkCompositeAlphaFlagBitsKHR.CompositeAlphaOpaqueBitKhr;
        swapchainCreateInfo.presentMode = surfacePresentMode;
        swapchainCreateInfo.clipped = (int) Vk.False;

        VkSwapchainKHR swapchain;
        _result = Vk.CreateSwapchainKHR(_device, &swapchainCreateInfo, null, &swapchain);
        if (_result != VkResult.Success) GameLogger.Throw($"Error creating swapchain with error {_result}");
        _swapchain = swapchain;

        uint swapchainImageCount;
        Vk.GetSwapchainImagesKHR(_device, _swapchain, &swapchainImageCount, null);
        VkImage* swapchainImages = stackalloc VkImage[(int)swapchainImageCount];
        Vk.GetSwapchainImagesKHR(_device, _swapchain, &swapchainImageCount, swapchainImages);
        for (int i = 0; i < swapchainImageCount; i++)
        {
            _swapchainImages.Add(swapchainImages[i]);
        }

        _swapchainFormat = surfaceFormat.format;
        _swapchainExtent = extent;

        for (int i = 0; i < _swapchainImages.Count; i++)
        {

            VkImageViewCreateInfo imageViewCreateInfo = new VkImageViewCreateInfo();
            imageViewCreateInfo.sType = VkStructureType.StructureTypeImageViewCreateInfo;
            imageViewCreateInfo.image = _swapchainImages[i];
            imageViewCreateInfo.viewType = VkImageViewType.ImageViewType2d;
            imageViewCreateInfo.format = surfaceFormat.format;

            imageViewCreateInfo.components.r = VkComponentSwizzle.ComponentSwizzleIdentity;
            imageViewCreateInfo.components.g = VkComponentSwizzle.ComponentSwizzleIdentity;
            imageViewCreateInfo.components.b = VkComponentSwizzle.ComponentSwizzleIdentity;
            imageViewCreateInfo.components.a = VkComponentSwizzle.ComponentSwizzleIdentity;

            imageViewCreateInfo.subresourceRange.aspectMask = VkImageAspectFlagBits.ImageAspectColorBit;
            imageViewCreateInfo.subresourceRange.baseMipLevel = 0;
            imageViewCreateInfo.subresourceRange.levelCount = 1;
            imageViewCreateInfo.subresourceRange.baseArrayLayer = 0;
            imageViewCreateInfo.subresourceRange.layerCount = 1;

            VkImageView imageView;
            _result = Vk.CreateImageView(_device, &imageViewCreateInfo, null, &imageView);
            if (_result != VkResult.Success) GameLogger.Throw($"Error making image view with error {_result}");
            _swapchainImageViews.Add(imageView);

        }

        GameLogger.Log("Creating the render passes.");
        VkAttachmentDescription colorAttachment = new VkAttachmentDescription();
        colorAttachment.format = _swapchainFormat;
        colorAttachment.samples = VkSampleCountFlagBits.SampleCount1Bit;
        colorAttachment.loadOp = VkAttachmentLoadOp.AttachmentLoadOpClear;
        colorAttachment.storeOp = VkAttachmentStoreOp.AttachmentStoreOpStore;
        colorAttachment.stencilLoadOp = VkAttachmentLoadOp.AttachmentLoadOpDontCare;
        colorAttachment.stencilStoreOp = VkAttachmentStoreOp.AttachmentStoreOpDontCare;
        colorAttachment.initialLayout = VkImageLayout.ImageLayoutUndefined;
        colorAttachment.finalLayout = VkImageLayout.ImageLayoutPresentSrcKhr;

        VkAttachmentReference colorAttachmentReference = new VkAttachmentReference();
        colorAttachmentReference.attachment = 0;
        colorAttachmentReference.layout = VkImageLayout.ImageLayoutColorAttachmentOptimal;

        VkSubpassDescription subpassDescription = new VkSubpassDescription();
        subpassDescription.colorAttachmentCount = 1;
        subpassDescription.pColorAttachments = &colorAttachmentReference;

        VkSubpassDependency subpassDependency = new VkSubpassDependency();
        subpassDependency.srcSubpass = Vk.SubpassExternal;
        subpassDependency.dstSubpass = 0;
        subpassDependency.srcStageMask = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit;
        subpassDependency.srcAccessMask = 0;
        subpassDependency.dstStageMask = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit;
        subpassDependency.dstAccessMask = VkAccessFlagBits.AccessColorAttachmentWriteBit;

        VkRenderPassCreateInfo renderPassCreateInfo = new VkRenderPassCreateInfo();
        renderPassCreateInfo.sType = VkStructureType.StructureTypeRenderPassCreateInfo;
        renderPassCreateInfo.attachmentCount = 1;
        renderPassCreateInfo.pAttachments = &colorAttachment;
        renderPassCreateInfo.subpassCount = 1;
        renderPassCreateInfo.pSubpasses = &subpassDescription;
        renderPassCreateInfo.dependencyCount = 1;
        renderPassCreateInfo.pDependencies = &subpassDependency;

        VkRenderPass renderPass;
        _result = Vk.CreateRenderPass(_device, &renderPassCreateInfo, null, &renderPass);
        if (_result != VkResult.Success) GameLogger.Throw($"Failed to create render pass with error {_result}");
        _renderPass = renderPass;

        GameLogger.Log("Creating the graphics pipeline.");
        byte[] vertex = File.ReadAllBytes("shad_vert.spv");
        byte[] fragment = File.ReadAllBytes("shad_frag.spv");

        byte* vertexPtr = stackalloc byte[vertex.Length];
        byte* fragmentPtr = stackalloc byte[fragment.Length];

        Marshal.Copy(vertex, 0, (nint) vertexPtr, vertex.Length);
        Marshal.Copy(fragment, 0, (nint) fragmentPtr, fragment.Length);

        VkShaderModuleCreateInfo vertexShaderModuleCreateInfo = new VkShaderModuleCreateInfo();
        vertexShaderModuleCreateInfo.sType = VkStructureType.StructureTypeShaderModuleCreateInfo;
        vertexShaderModuleCreateInfo.codeSize = (uint) vertex.Length;
        vertexShaderModuleCreateInfo.pCode = (uint*) vertexPtr;

        VkShaderModule vertexShaderModule;
        _result = Vk.CreateShaderModule(_device, &vertexShaderModuleCreateInfo, null, &vertexShaderModule);
        if (_result != VkResult.Success) GameLogger.Throw($"Error creating shader module with error {_result}");

        VkShaderModuleCreateInfo fragmentShaderModuleCreateInfo = new VkShaderModuleCreateInfo();
        fragmentShaderModuleCreateInfo.sType = VkStructureType.StructureTypeShaderModuleCreateInfo;
        fragmentShaderModuleCreateInfo.codeSize = (uint) fragment.Length;
        fragmentShaderModuleCreateInfo.pCode = (uint*) fragmentPtr;

        VkShaderModule fragmentShaderModule;
        _result = Vk.CreateShaderModule(_device, &fragmentShaderModuleCreateInfo, null, &fragmentShaderModule);
        if (_result != VkResult.Success) GameLogger.Throw($"Error creating shader module with error {_result}");

        VkPipelineShaderStageCreateInfo vertexShaderStageCreateInfo = new VkPipelineShaderStageCreateInfo();
        vertexShaderStageCreateInfo.sType = VkStructureType.StructureTypePipelineShaderStageCreateInfo;
        vertexShaderStageCreateInfo.stage = VkShaderStageFlagBits.ShaderStageVertexBit;
        vertexShaderStageCreateInfo.module = vertexShaderModule;
        fixed (byte* namePtr = "main"u8) vertexShaderStageCreateInfo.pName = namePtr;

        VkPipelineShaderStageCreateInfo fragmentShaderStageCreateInfo = new VkPipelineShaderStageCreateInfo();
        fragmentShaderStageCreateInfo.sType = VkStructureType.StructureTypePipelineShaderStageCreateInfo;
        fragmentShaderStageCreateInfo.stage = VkShaderStageFlagBits.ShaderStageFragmentBit;
        fragmentShaderStageCreateInfo.module = fragmentShaderModule;
        fixed (byte* namePtr = "main"u8) fragmentShaderStageCreateInfo.pName = namePtr;

        VkPipelineShaderStageCreateInfo* shaderStageCreateInfos = stackalloc VkPipelineShaderStageCreateInfo[] { vertexShaderStageCreateInfo, fragmentShaderStageCreateInfo };

        VkDynamicState* dynamicStates = stackalloc VkDynamicState[] { VkDynamicState.DynamicStateViewport, VkDynamicState.DynamicStateScissor };

        VkPipelineDynamicStateCreateInfo dynamicStateCreateInfo = new VkPipelineDynamicStateCreateInfo();
        dynamicStateCreateInfo.sType = VkStructureType.StructureTypePipelineDynamicStateCreateInfo;
        dynamicStateCreateInfo.dynamicStateCount = 2;
        dynamicStateCreateInfo.pDynamicStates = dynamicStates;

        VkPipelineVertexInputStateCreateInfo vertexInputStateCreateInfo = new VkPipelineVertexInputStateCreateInfo();
        vertexInputStateCreateInfo.sType = VkStructureType.StructureTypePipelineVertexInputStateCreateInfo;
        vertexInputStateCreateInfo.vertexAttributeDescriptionCount = 0;
        vertexInputStateCreateInfo.vertexBindingDescriptionCount = 0;

        VkPipelineInputAssemblyStateCreateInfo inputAssemblyStateCreateInfo = new VkPipelineInputAssemblyStateCreateInfo();
        inputAssemblyStateCreateInfo.sType = VkStructureType.StructureTypePipelineInputAssemblyStateCreateInfo;
        inputAssemblyStateCreateInfo.topology = VkPrimitiveTopology.PrimitiveTopologyTriangleList;
        inputAssemblyStateCreateInfo.primitiveRestartEnable = (int) Vk.False;

        VkViewport viewport = new VkViewport();
        viewport.x = 0.0f;
        viewport.y = 0.0f;
        viewport.width = _swapchainExtent.width;
        viewport.height = _swapchainExtent.height;
        viewport.minDepth = 0.0f;
        viewport.maxDepth = 1.0f;

        VkRect2D scissor = new VkRect2D();
        scissor.offset.x = 0;
        scissor.offset.y = 0;
        scissor.extent = _swapchainExtent;

        VkPipelineViewportStateCreateInfo viewportStateCreateInfo = new VkPipelineViewportStateCreateInfo();
        viewportStateCreateInfo.sType = VkStructureType.StructureTypePipelineViewportStateCreateInfo;
        viewportStateCreateInfo.scissorCount = 1;
        viewportStateCreateInfo.viewportCount = 1;

        VkPipelineRasterizationStateCreateInfo rasterizationStateCreateInfo = new VkPipelineRasterizationStateCreateInfo();
        rasterizationStateCreateInfo.sType = VkStructureType.StructureTypePipelineRasterizationStateCreateInfo;
        rasterizationStateCreateInfo.depthClampEnable = (int) Vk.False;
        rasterizationStateCreateInfo.polygonMode = VkPolygonMode.PolygonModeFill;
        rasterizationStateCreateInfo.cullMode = VkCullModeFlagBits.CullModeBackBit;
        rasterizationStateCreateInfo.frontFace = VkFrontFace.FrontFaceClockwise;
        rasterizationStateCreateInfo.lineWidth = 1.0f;
        rasterizationStateCreateInfo.depthBiasEnable = (int) Vk.False;

        VkPipelineMultisampleStateCreateInfo multisampleStateCreateInfo = new VkPipelineMultisampleStateCreateInfo();
        multisampleStateCreateInfo.sType = VkStructureType.StructureTypePipelineMultisampleStateCreateInfo;
        multisampleStateCreateInfo.sampleShadingEnable = (int) Vk.False;
        multisampleStateCreateInfo.rasterizationSamples = VkSampleCountFlagBits.SampleCount1Bit;

        VkPipelineDepthStencilStateCreateInfo depthStencilStateCreateInfo = new VkPipelineDepthStencilStateCreateInfo();
        depthStencilStateCreateInfo.sType = VkStructureType.StructureTypePipelineDepthStencilStateCreateInfo;
        
        VkPipelineColorBlendAttachmentState colorBlendAttachmentState = new VkPipelineColorBlendAttachmentState();
        colorBlendAttachmentState.colorWriteMask = VkColorComponentFlagBits.ColorComponentRBit | VkColorComponentFlagBits.ColorComponentGBit | VkColorComponentFlagBits.ColorComponentBBit | VkColorComponentFlagBits.ColorComponentABit;
        colorBlendAttachmentState.blendEnable = (int) Vk.False;
        
        VkPipelineColorBlendStateCreateInfo colorBlendStateCreateInfo = new VkPipelineColorBlendStateCreateInfo();
        colorBlendStateCreateInfo.sType = VkStructureType.StructureTypePipelineColorBlendStateCreateInfo;
        colorBlendStateCreateInfo.logicOpEnable = (int) Vk.False;
        colorBlendStateCreateInfo.attachmentCount = 1;
        colorBlendStateCreateInfo.pAttachments = &colorBlendAttachmentState;

        VkPipelineLayoutCreateInfo pipelineLayoutCreateInfo = new VkPipelineLayoutCreateInfo();
        pipelineLayoutCreateInfo.sType = VkStructureType.StructureTypePipelineLayoutCreateInfo;
        
        VkPipelineLayout pipelineLayout;
        _result = Vk.CreatePipelineLayout(_device, &pipelineLayoutCreateInfo, null, &pipelineLayout);
        if (_result != VkResult.Success) GameLogger.Throw($"Failed to create pipeline layout with error {_result}"); 
        _pipelineLayout = pipelineLayout;

        VkGraphicsPipelineCreateInfo graphicsPipelineCreateInfo = new VkGraphicsPipelineCreateInfo();
        graphicsPipelineCreateInfo.sType = VkStructureType.StructureTypeGraphicsPipelineCreateInfo;
        graphicsPipelineCreateInfo.stageCount = 2;
        graphicsPipelineCreateInfo.pStages = shaderStageCreateInfos;
        graphicsPipelineCreateInfo.pVertexInputState = &vertexInputStateCreateInfo;
        graphicsPipelineCreateInfo.pInputAssemblyState = &inputAssemblyStateCreateInfo;
        graphicsPipelineCreateInfo.pViewportState = &viewportStateCreateInfo;
        graphicsPipelineCreateInfo.pRasterizationState = &rasterizationStateCreateInfo;
        graphicsPipelineCreateInfo.pMultisampleState = &multisampleStateCreateInfo;
        graphicsPipelineCreateInfo.pDepthStencilState = null;
        graphicsPipelineCreateInfo.pColorBlendState = &colorBlendStateCreateInfo;
        graphicsPipelineCreateInfo.pDynamicState = &dynamicStateCreateInfo;
        graphicsPipelineCreateInfo.layout = _pipelineLayout;
        graphicsPipelineCreateInfo.renderPass = _renderPass;
        graphicsPipelineCreateInfo.subpass = 0;
        graphicsPipelineCreateInfo.basePipelineHandle = VkPipeline.Zero;
        graphicsPipelineCreateInfo.basePipelineIndex = -1;

        VkPipeline graphicsPipeline;
        _result = Vk.CreateGraphicsPipelines(_device, VkPipelineCache.Zero, 1, &graphicsPipelineCreateInfo, null, &graphicsPipeline);
        if (_result != VkResult.Success) GameLogger.Throw($"Failed to create graphics pipeline with error {_result}");
        _graphicsPipeline = graphicsPipeline;

        GameLogger.Log("Creating framebuffers.");
        VkImageView* attachments = stackalloc VkImageView[1];
        foreach (VkImageView imageView in _swapchainImageViews)
        {

            attachments[0] = imageView;

            VkFramebufferCreateInfo framebufferCreateInfo = new VkFramebufferCreateInfo();
            framebufferCreateInfo.sType = VkStructureType.StructureTypeFramebufferCreateInfo;
            framebufferCreateInfo.renderPass = _renderPass;
            framebufferCreateInfo.attachmentCount = 1;
            framebufferCreateInfo.pAttachments = attachments;
            framebufferCreateInfo.width = _swapchainExtent.width;
            framebufferCreateInfo.height = _swapchainExtent.height;
            framebufferCreateInfo.layers = 1;

            VkFramebuffer framebuffer;
            _result = Vk.CreateFramebuffer(_device, &framebufferCreateInfo, null, &framebuffer);
            if (_result != VkResult.Success) GameLogger.Throw($"Failed to create framebuffer with error {_result}");
            _swapchainFramebuffers.Add(framebuffer);

        }

        GameLogger.Log("Creating command pool.");
        VkCommandPoolCreateInfo commandPoolCreateInfo = new VkCommandPoolCreateInfo();
        commandPoolCreateInfo.sType = VkStructureType.StructureTypeCommandPoolCreateInfo;
        commandPoolCreateInfo.flags = VkCommandPoolCreateFlagBits.CommandPoolCreateResetCommandBufferBit;
        commandPoolCreateInfo.queueFamilyIndex = _queueFamilies.GraphicsFamily.Value;

        VkCommandPool commandPool;
        _result = Vk.CreateCommandPool(_device, &commandPoolCreateInfo, null, &commandPool);
        if (_result != VkResult.Success) GameLogger.Throw($"Failed to create command pool with error {_result}");
        _commandPool = commandPool;

        GameLogger.Log("Creating command buffer.");
        VkCommandBufferAllocateInfo commandBufferAllocateInfo = new VkCommandBufferAllocateInfo();
        commandBufferAllocateInfo.sType = VkStructureType.StructureTypeCommandBufferAllocateInfo;
        commandBufferAllocateInfo.commandPool = _commandPool;
        commandBufferAllocateInfo.level = VkCommandBufferLevel.CommandBufferLevelPrimary;
        commandBufferAllocateInfo.commandBufferCount = 1;

        VkCommandBuffer commandBuffer;
        _result = Vk.AllocateCommandBuffers(_device, &commandBufferAllocateInfo, &commandBuffer);
        if (_result != VkResult.Success) GameLogger.Throw($"Failed to allocate command buffer with error {_result}");
        _commandBuffer = commandBuffer;

        GameLogger.Log("Creating sync objects.");
        VkSemaphoreCreateInfo semaphoreCreateInfo = new VkSemaphoreCreateInfo();
        semaphoreCreateInfo.sType = VkStructureType.StructureTypeSemaphoreCreateInfo;

        VkFenceCreateInfo fenceCreateInfo = new VkFenceCreateInfo();
        fenceCreateInfo.sType = VkStructureType.StructureTypeFenceCreateInfo;
        fenceCreateInfo.flags = VkFenceCreateFlagBits.FenceCreateSignaledBit;

        VkSemaphore imageAvailableSemaphore;
        VkSemaphore renderFinishedSemaphore;
        VkFence inFlightFence;
        _result = Vk.CreateSemaphore(_device, &semaphoreCreateInfo, null, &imageAvailableSemaphore);
        if (_result != VkResult.Success) GameLogger.Throw($"Failed to create semaphore with error {_result}");
        _imageAvailableSemaphore = imageAvailableSemaphore;
        _result = Vk.CreateSemaphore(_device, &semaphoreCreateInfo, null, &renderFinishedSemaphore);
        if (_result != VkResult.Success) GameLogger.Throw($"Failed to create semaphore with error {_result}");
        _renderFinishedSemaphore = renderFinishedSemaphore;
        _result = Vk.CreateFence(_device, &fenceCreateInfo, null, &inFlightFence);
        if (_result != VkResult.Success) GameLogger.Throw($"Failed to create fence with error {_result}");
        _inFlightFence = inFlightFence;

    }

    public static void RecordCommandBuffer(VkCommandBuffer commandBuffer, uint imageIndex)
    {

        VkCommandBufferBeginInfo commandBufferBeginInfo = new VkCommandBufferBeginInfo();
        commandBufferBeginInfo.sType = VkStructureType.StructureTypeCommandBufferBeginInfo;
        commandBufferBeginInfo.flags = 0;
        commandBufferBeginInfo.pInheritanceInfo = null;

        _result = Vk.BeginCommandBuffer(commandBuffer, &commandBufferBeginInfo);
        if (_result != VkResult.Success) GameLogger.Throw($"Failed to begin command buffer with error {_result}");

        VkRenderPassBeginInfo renderPassBeginInfo = new VkRenderPassBeginInfo();
        renderPassBeginInfo.sType = VkStructureType.StructureTypeRenderPassBeginInfo;
        renderPassBeginInfo.renderPass = _renderPass;
        renderPassBeginInfo.framebuffer = _swapchainFramebuffers[(int)imageIndex];
        renderPassBeginInfo.renderArea.offset = new VkOffset2D(0, 0);
        renderPassBeginInfo.renderArea.extent = _swapchainExtent;

        VkClearValue clearColor = new VkClearValue();
        clearColor.color = new VkClearColorValue();
        clearColor.color.float32[0] = 0.0f;
        clearColor.color.float32[1] = 0.0f;
        clearColor.color.float32[2] = 0.0f;
        clearColor.color.float32[3] = 1.0f;

        renderPassBeginInfo.clearValueCount = 1;
        renderPassBeginInfo.pClearValues = &clearColor;

        Vk.CmdBeginRenderPass(commandBuffer, &renderPassBeginInfo, VkSubpassContents.SubpassContentsInline);
        Vk.CmdBindPipeline(commandBuffer, VkPipelineBindPoint.PipelineBindPointGraphics, _graphicsPipeline);

        VkViewport viewport = new VkViewport();
        viewport.x = 0.0f;
        viewport.y = 0.0f;
        viewport.width = _swapchainExtent.width;
        viewport.height = _swapchainExtent.height;
        viewport.minDepth = 0.0f;
        viewport.maxDepth = 1.0f;
        Vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);

        VkRect2D scissor = new VkRect2D();
        scissor.offset = new VkOffset2D(0, 0);
        scissor.extent = _swapchainExtent;
        Vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);

        Vk.CmdDraw(commandBuffer, 3, 1, 0, 0);

        Vk.CmdEndRenderPass(_commandBuffer);
        _result = Vk.EndCommandBuffer(commandBuffer);
        if (_result != VkResult.Success) GameLogger.Throw($"Failed to end command buffer with error {_result}");

    }

    public static void DrawTriangle()
    {

        fixed (VkFence* inFlightFencePtr = &_inFlightFence)
        {

            Vk.WaitForFences(_device, 1, inFlightFencePtr, (int) Vk.True, ulong.MaxValue);
            Vk.ResetFences(_device, 1, inFlightFencePtr);

        }

        uint imageIndex;
        Vk.AcquireNextImageKHR(_device, _swapchain, ulong.MaxValue, _imageAvailableSemaphore, VkFence.Zero, &imageIndex);
        Vk.ResetCommandBuffer(_commandBuffer, 0);

        RecordCommandBuffer(_commandBuffer, imageIndex);

        VkSubmitInfo submitInfo = new VkSubmitInfo();
        submitInfo.sType = VkStructureType.StructureTypeSubmitInfo;

        // VkSemaphore* waitSemaphores = stackalloc VkSemaphore[1] { _imageAvailableSemaphore };
        VkPipelineStageFlagBits* waitStages = stackalloc VkPipelineStageFlagBits[1] { VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit };
        
        submitInfo.waitSemaphoreCount = 1;
        fixed (VkSemaphore* imageAvailableSemaphorePtr = &_imageAvailableSemaphore) submitInfo.pWaitSemaphores = imageAvailableSemaphorePtr;
        submitInfo.pWaitDstStageMask = waitStages;
        submitInfo.commandBufferCount = 1;
        fixed (VkCommandBuffer* commandBufferPtr = &_commandBuffer) submitInfo.pCommandBuffers = commandBufferPtr;

        // VkSemaphore* signalSemaphores = stackalloc VkSemaphore[1] { _renderFinishedSemaphore };
        submitInfo.signalSemaphoreCount = 1;
        fixed (VkSemaphore* signalSemaphoresPtr = &_renderFinishedSemaphore) submitInfo.pSignalSemaphores = signalSemaphoresPtr;

        _result = Vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, _inFlightFence);
        if (_result != VkResult.Success) GameLogger.Throw($"Failed to submit queue with error {_result}");

        VkPresentInfoKHR presentInfo = new VkPresentInfoKHR();
        presentInfo.sType = VkStructureType.StructureTypePresentInfoKhr;
        presentInfo.waitSemaphoreCount = 1;
        fixed (VkSemaphore* signalSemaphoresPtr = &_renderFinishedSemaphore) presentInfo.pWaitSemaphores = signalSemaphoresPtr;

        // VkSwapchainKHR* swapchains = stackalloc VkSwapchainKHR[1] { _swapchain };
        presentInfo.swapchainCount = 1;
        fixed (VkSwapchainKHR* swapchainPtr = &_swapchain) presentInfo.pSwapchains = swapchainPtr;
        presentInfo.pImageIndices = &imageIndex;

        Vk.QueuePresentKHR(_presentQueue, &presentInfo);

    }

    public static void Wait()
    {

        Vk.DeviceWaitIdle(_device);

    }

    public static void Unload()
    {
        
        Vk.DestroySemaphore(_device, _imageAvailableSemaphore, null);
        Vk.DestroySemaphore(_device, _renderFinishedSemaphore, null);
        Vk.DestroyFence(_device, _inFlightFence, null);
        Vk.DestroyCommandPool(_device, _commandPool, null);
        foreach (VkFramebuffer framebuffer in _swapchainFramebuffers)
        {
            Vk.DestroyFramebuffer(_device, framebuffer, null);
        }
        _swapchainFramebuffers.Clear();
        Vk.DestroyPipeline(_device, _graphicsPipeline, null);
        Vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
        Vk.DestroyRenderPass(_device, _renderPass, null);
        foreach (VkImageView imageView in _swapchainImageViews)
        {
            Vk.DestroyImageView(_device, imageView, null);
        }
        _swapchainImageViews.Clear();
        Vk.DestroySwapchainKHR(_device, _swapchain, null);
        Vk.DestroySurfaceKHR(_instance, _surface, null);
        Vk.DestroyDevice(_device, null);
        Vk.DestroyInstance(_instance, null);

    }   

}