using System.Text;
using Game.Core.Utilities;
using OpenTK.Core.Native;
using OpenTK.Graphics;
using OpenTK.Graphics.Vulkan;
using OpenTK.Platform;

namespace Game.Core.Rendering;

public unsafe class Renderer
{

    private static VkInstance _instance;
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
        if (_result != VkResult.Success)
        {
            GameLogger.Throw($"Error creating Vulkan instance with error {_result}");
        }
        _instance = instance;

        VKLoader.SetInstance(_instance);

    }

    public static void Unload()
    {

        Vk.DestroyInstance(_instance, null);

    }   

}