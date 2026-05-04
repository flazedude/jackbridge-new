using Avalonia;
using System;
using System.IO;
using System.Reflection;

namespace JackBridge.GUI;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var binDir = Path.Combine(AppContext.BaseDirectory, "bin");
        if (Directory.Exists(binDir))
        {
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                var name = new AssemblyName(e.Name).Name;
                var path = Path.Combine(binDir, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
        }

        App.StartMinimized = args.Length > 0 && args[0] == "--minimized";
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
