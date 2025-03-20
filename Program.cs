using System.ComponentModel;
using Game.Core.Rendering;
using Game.Core.Utilities;
using OpenTK.Graphics;
using OpenTK.Platform;

namespace Game;

class Program
{

    private static WindowHandle _window;
    static void Main(string[] args)
    {

        ToolkitOptions options = new ToolkitOptions();
        options.ApplicationName = "Game in Vulkan";
        options.Logger = null;
        
        Toolkit.Init(options);

        VulkanGraphicsApiHints contextSettings = new VulkanGraphicsApiHints()
        {
            
        };

        _window = Toolkit.Window.Create(contextSettings);

        Toolkit.Window.SetTitle(_window, "Game in Vulkan");
        Toolkit.Window.SetSize(_window, (800, 600));
        Toolkit.Window.SetMode(_window, WindowMode.Normal);

        EventQueue.EventRaised += EventRaised;

        Renderer.Init(_window);

        while (Globals.IsRunning)
        {

            Toolkit.Window.ProcessEvents(false);

            Renderer.DrawTriangle();

        }

        Renderer.Wait();
        Renderer.Unload();
        Toolkit.Window.Destroy(_window);

    }

    private static void EventRaised(PalHandle? handle, PlatformEventType eventType, EventArgs eventArgs)
    {

        switch (eventArgs)
        {

            case CloseEventArgs closeEvent:
                Globals.IsRunning = false;
                break;

        }

    }

}
