using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using GestionDocumentos.Core.Abstractions;
using GestionDocumentos.Core.Email;
using GestionDocumentos.Core.Services;
using GestionDocumentos.Gre;
using GestionDocumentos.Host;
using GestionDocumentos.Idoc;

var runAsWindowsService =
    !args.Contains("--console", StringComparer.OrdinalIgnoreCase) && OperatingSystem.IsWindows();

var builder = Host.CreateApplicationBuilder(args);

if (runAsWindowsService)
{
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
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
builder.Services.AddSingleton<ErrorEmailQueue>();
builder.Services.AddSingleton<SmtpErrorEmailSender>();
builder.Services.AddHostedService<ErrorEmailQueueProcessorHostedService>();
builder.Services.AddSingleton<ILoggerProvider, ErrorEmailLoggerProvider>();

builder.Services.AddSingleton<IParameterProvider, JsonParameterProvider>();

builder.Services.Configure<ReconciliationOptions>(builder.Configuration.GetSection("Reconciliation"));

builder.Services.AddGrePipeline();
builder.Services.AddIdocPipeline();

builder.Services.AddHostedService<GreWatcherHostedService>();
builder.Services.AddHostedService<IdocWatcherHostedService>();
builder.Services.AddHostedService<DailyReconciliationHostedService>();

if (runAsWindowsService)
{
    builder.Services.AddWindowsService(o => o.ServiceName = "GestionDocumentos");
}

var host = builder.Build();
await host.RunAsync();
