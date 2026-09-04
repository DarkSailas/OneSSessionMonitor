using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using OneSSessionMonitor.Core.Models;
using OneSSessionMonitor.Core.Ras;
using OneSSessionMonitor.Core.Services;
using OneSSessionMonitor.Core.State;

namespace OneSSessionMonitor.Gui;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "ONEC_CLEANER_")
            .Build();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Error()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<SessionMonitorOptions>(configuration.GetSection(SessionMonitorOptions.SectionName));

        services.AddSingleton<RacProcessExecutor>();
        services.AddSingleton<IRasClient, RasClient>();
        services.AddSingleton<ISessionFilter, SessionFilter>();
        services.AddSingleton<OneSSessionMonitor.Core.Slk.ISlkClient, OneSSessionMonitor.Core.Slk.SlkClient>();
        services.AddSingleton<ISessionMonitorService, DefaultSessionMonitorService>();
        services.AddSingleton<CleanerState>();
        services.AddSingleton<MainWindow>();

        ServiceProvider = services.BuildServiceProvider();

        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
