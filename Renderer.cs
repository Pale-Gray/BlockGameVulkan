using System.Text;
using Game.Core.Utilities;
using OpenTK.Core.Native;
using OpenTK.Graphics;
using OpenTK.Graphics.Vulkan;
using OpenTK.Platform;

namespace Game.Core.Rendering;

struct QueueFamilyIndices
{

    public uint? GraphicsFamily;
    public uint? PresentFamily;

    public bool IsCompatible => GraphicsFamily.HasValue && PresentFamily.HasValue;

}
public unsafe class Renderer
{

    private static VkInstance _instance;
    private static VkPhysicalDevice _physicalDevice;
    private static VkDevice _device;
    private static VkQueue _graphicsQueue;
    private static QueueFamilyIndices _queueFamilies;
    private static VkQueue _presentQueue;
    private static VkSurfaceKHR _surface;
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
        deviceCreateInfo.enabledExtensionCount = 0;
        if (foundValidationLayers)
        {
            deviceCreateInfo.enabledLayerCount = count;
            deviceCreateInfo.ppEnabledLayerNames = validationLayersPtr;
        } else
        {
            deviceCreateInfo.enabledLayerCount = 0;
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

    }

    public static void Unload()
    {

        Vk.DestroySurfaceKHR(_instance, _surface, null);
        Vk.DestroyDevice(_device, null);
        Vk.DestroyInstance(_instance, null);

    }   

}