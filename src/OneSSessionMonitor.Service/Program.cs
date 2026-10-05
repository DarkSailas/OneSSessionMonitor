using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using OneSSessionMonitor.Core.Models;
using OneSSessionMonitor.Core.Ras;
using OneSSessionMonitor.Core.Services;
using OneSSessionMonitor.Core.Slk;
using OneSSessionMonitor.Core.State;
using OneSSessionMonitor.Service.Workers;

// Принудительно устанавливаем рабочий каталог в папку размещения службы
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

string logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
if (!Directory.Exists(logsDirectory))
{
    try { Directory.CreateDirectory(logsDirectory); } catch { }
}

// Загрузка первоначальной конфигурации для параметров логирования
var tempConfig = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables(prefix: "ONEC_CLEANER_")
    .Build();

string logLevelStr = tempConfig["Serilog:MinimumLevel:Default"] ?? tempConfig["SessionCleaner:LogLevel"] ?? "Error";
if (!Enum.TryParse<LogEventLevel>(logLevelStr, true, out var minLevel))
{
    minLevel = LogEventLevel.Error;
}

int retainedDays = 7;
if (int.TryParse(tempConfig["SessionCleaner:LogRetainedDays"], out var rDays) && rDays > 0)
{
    retainedDays = rDays;
}

int maxTotalMb = 100;
if (int.TryParse(tempConfig["SessionCleaner:LogMaxTotalSizeMb"], out var mMb) && mMb > 0)
{
    maxTotalMb = mMb;
}

// Лимит размера одного файла (общий лимит / количество дней)
long maxFileSizeBytes = Math.Max(5_000_000, (long)maxTotalMb * 1024 * 1024 / Math.Max(1, retainedDays));
string logFilePath = Path.Combine(logsDirectory, "cleaner-.log");

// Автоматическая очистка устаревших логов при старте службы
try
{
    var dirInfo = new DirectoryInfo(logsDirectory);
    var oldFiles = dirInfo.GetFiles("*.log")
        .Where(f => f.LastWriteTime < DateTime.Now.AddDays(-retainedDays))
        .ToList();

    foreach (var f in oldFiles)
    {
        try { f.Delete(); } catch { }
    }

    // Проверка суммарного объема
    var allFiles = dirInfo.GetFiles("*.log").OrderBy(f => f.LastWriteTime).ToList();
    long totalBytes = allFiles.Sum(f => f.Length);
    long maxBytes = (long)maxTotalMb * 1024 * 1024;
    while (totalBytes > maxBytes && allFiles.Count > 1)
    {
        var oldest = allFiles[0];
        allFiles.RemoveAt(0);
        totalBytes -= oldest.Length;
        try { oldest.Delete(); } catch { }
    }
}
catch { }

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(minLevel)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        restrictedToMinimumLevel: minLevel,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .WriteTo.File(
        path: logFilePath,
        restrictedToMinimumLevel: minLevel,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: retainedDays,
        fileSizeLimitBytes: maxFileSizeBytes,
        rollOnFileSizeLimit: true,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

try
{
    var builder = Host.CreateDefaultBuilder(args)
        .UseWindowsService(options =>
        {
            options.ServiceName = "OneSSessionMonitor";
        })
        .ConfigureAppConfiguration((hostingContext, config) =>
        {
            config.SetBasePath(AppContext.BaseDirectory);
            config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            config.AddEnvironmentVariables(prefix: "ONEC_CLEANER_");
            config.AddCommandLine(args);
        })
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
            services.Configure<SessionMonitorOptions>(context.Configuration.GetSection(SessionMonitorOptions.SectionName));

            services.AddHttpClient();
            services.AddSingleton<RacProcessExecutor>();
            services.AddSingleton<IRasClient, RasClient>();
            services.AddSingleton<ISessionFilter, SessionFilter>();
            services.AddSingleton<ISlkClient, SlkClient>();
            services.AddSingleton<ISessionMonitorService, DefaultSessionMonitorService>();
            services.AddSingleton<CleanerState>();

            services.AddHostedService<SessionMonitorWorker>();
        });

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Критическая ошибка запуска службы OneSSessionMonitor: {Message}", ex.Message);
    try
    {
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "crash.log"),
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] КРИТИЧЕСКИЙ СБОЙ ЗАПУСКА:\n{ex}\n"
        );
    }
    catch { }
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}