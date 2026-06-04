using AIHelperNET.Application;
using AIHelperNET.Infrastructure;
using AIHelperNET.Infrastructure.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AIHelperNET.App;

public static class HostConfiguration
{
    public static HostApplicationBuilder ConfigureAIHelper(this HostApplicationBuilder b)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("AIHelperNET", Serilog.Events.LogEventLevel.Debug)
            .WriteTo.File(
                AppPaths.LogFile,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        b.Services.AddSerilog();

        b.Services
            .AddApplication()
            .AddInfrastructure(b.Configuration)
            .AddPresentation();

        b.Services.AddSingleton(TimeProvider.System);

        return b;
    }
}
