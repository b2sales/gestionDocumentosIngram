using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using GestionDocumentos.Core.Abstractions;
using GestionDocumentos.Core.Email;
using GestionDocumentos.Core.Logging;
using GestionDocumentos.Core.Services;
using GestionDocumentos.Gre;
using GestionDocumentos.Host;
using GestionDocumentos.Idoc;

var runAsWindowsService =
    !args.Contains("--console", StringComparer.OrdinalIgnoreCase) && OperatingSystem.IsWindows();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

// En modo servicio: ruido del framework/runtime silenciado, propio visible en Information hacia arriba.
// En modo consola: dejamos el default (Information global) para facilitar depuración local.
if (runAsWindowsService)
{
    builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
    builder.Logging.AddFilter("System", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
    builder.Logging.AddFilter("GestionDocumentos", LogLevel.Information);
}

builder.Logging.AddFilter<EventLogLoggerProvider>(level => level >= LogLevel.Warning);
builder.Services.Configure<EventLogSettings>(settings =>
{
    settings.SourceName = "GestionDocumentos";
    settings.LogName = "Application";
});

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "Parametros.json"),
    optional: false,
    reloadOnChange: true);

builder.Services.Configure<SmtpErrorEmailOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<ErrorFileLogOptions>(builder.Configuration.GetSection("ErrorFileLog"));
builder.Services.AddSingleton<ErrorEmailQueue>();
builder.Services.AddSingleton<SmtpErrorEmailSender>();
builder.Services.AddHostedService<ErrorEmailQueueProcessorHostedService>();
builder.Services.AddSingleton<ILoggerProvider, ErrorEmailLoggerProvider>();
builder.Services.AddSingleton<ILoggerProvider, ErrorFileLoggerProvider>();

builder.Services.AddSingleton<IParameterProvider, JsonParameterProvider>();

builder.Services.Configure<ReconciliationOptions>(builder.Configuration.GetSection("Reconciliation"));
builder.Services.Configure<HeartbeatOptions>(builder.Configuration.GetSection("Heartbeat"));
builder.Services.AddSingleton<WatcherStatusRegistry>();

builder.Services.AddGrePipeline();
builder.Services.AddIdocPipeline();

builder.Services.AddHostedService<GreWatcherHostedService>();
builder.Services.AddHostedService<IdocWatcherHostedService>();
builder.Services.AddHostedService<DailyReconciliationHostedService>();
builder.Services.AddHostedService<HeartbeatHostedService>();

if (runAsWindowsService)
{
    builder.Services.AddWindowsService(o => o.ServiceName = "GestionDocumentos");
}

var host = builder.Build();
await host.RunAsync();
