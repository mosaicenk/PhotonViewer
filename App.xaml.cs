using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PhotonViewer.Core.Memory;
using PhotonViewer.Core.Services;
using PhotonViewer.ViewModels;
using PhotonViewer.Views;

namespace PhotonViewer;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Configure process priority for performance
        ConfigureProcessPriority();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<ImageMemoryPool>();
        services.AddSingleton<IImageLoaderService, ImageLoaderService>();
        services.AddSingleton<ICacheService>(sp =>
        {
            var loader = sp.GetRequiredService<IImageLoaderService>();
            return new ImageCacheService(loader, maxCacheMegabytes: 512);
        });

        // ViewModels
        services.AddTransient<MainViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }

    private static void ConfigureProcessPriority()
    {
        try
        {
            // Set process priority to above normal for better responsiveness
            var process = System.Diagnostics.Process.GetCurrentProcess();
            process.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal;
        }
        catch
        {
            // Ignore if we can't change priority
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Cleanup
        _serviceProvider?.Dispose();
        ImageMemoryPool.TriggerCleanup(aggressive: true);
        
        base.OnExit(e);
    }
}
