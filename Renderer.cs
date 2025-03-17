using OpenTK.Graphics;
using OpenTK.Graphics.Vulkan;
using OpenTK.Platform;

namespace Game.Core.Rendering;

public class Renderer
{

    private static VkInstance _instance;

    public static void Init(WindowHandle window)
    {

        VKLoader.Init();
        VKLoader.SetInstance(_instance);

    }

    public static void Unload()
    {



    }   

}