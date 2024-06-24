using System.Net;
using App.Metrics;
using App.Metrics.AspNetCore;
using App.Metrics.Formatters.Prometheus;
using ApiGateway.Service;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ocelot.DependencyInjection;
using Ocelot.LoadBalancer.LoadBalancers;
using Ocelot.Middleware;
using Ocelot.Provider.Kubernetes;
using Serilog;
using Serilog.Sinks.Elasticsearch;

const string appName = "api-gateway-service";

Log.Logger = CreateSerilogLogger();

try
{
    Log.Information("Configuring web host ({ApplicationContext})...", appName);

    var builder = WebApplication.CreateBuilder(args);

    builder.WebHost
        .CaptureStartupErrors(false)
        .ConfigureAppMetricsHostingConfiguration(options => { options.MetricsTextEndpoint = "/prometheus"; })
        .ConfigureKestrel(
            (ctx, kestrel) =>
            {
                kestrel.AllowSynchronousIO = true;

                var ports = GetDefinedPorts(ctx.Configuration);
                kestrel.Listen(
                    IPAddress.Any, ports.httpPort, listenOptions => { listenOptions.Protocols = HttpProtocols.Http1AndHttp2; });
            })
        .UseMetricsWebTracking()
        .UseMetrics(
            options => { options.EndpointOptions = endpointOptions => { endpointOptions.MetricsTextEndpointOutputFormatter = new MetricsPrometheusTextOutputFormatter(); }; })
        .UseContentRoot(Directory.GetCurrentDirectory());

    builder.Host.UseSerilog();

    builder.Services
        .AddAuthentication();

    builder.Services
        .AddOcelot()
        .AddKubernetes()
        .Services
        //.AddSingleton<ILoadBalancerCreator, RoundRobinCreator2>()
        .AddEndpointsApiExplorer()
        .AddMetrics(setupMetrics => setupMetrics.OutputMetrics.AsPrometheusPlainText())
        .AddApplicationInsightsTelemetry(builder.Configuration)
        .AddApplicationInsightsKubernetesEnricher()
        .AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy());

    var app = builder.Build();
    app.UseMetricsAllMiddleware();

    app.UseHttpsRedirection();

    app.UseRouting();
    app.UseEndpoints(
        endpoints =>
        {
            endpoints.MapGet("/", () => string.Empty);
            endpoints.MapHealthChecks(
                "/hc", new HealthCheckOptions
                {
                    Predicate = _ => true,
                    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
                });
            endpoints.MapHealthChecks(
                "/liveness", new HealthCheckOptions
                {
                    Predicate = r => r.Name.Contains("self"),
                    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
                });
        });

    await app.UseOcelot();

    app.Run();

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Program terminated unexpectedly ({ApplicationContext})!", appName);
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

Serilog.ILogger CreateSerilogLogger()
{
    var configuration = new ConfigurationBuilder()
       .SetBasePath(Directory.GetCurrentDirectory())
       .AddJsonFile("appsettings.json", false, true)
       .AddEnvironmentVariables().Build();

    var environment = configuration["ASPNETCORE_ENVIRONMENT"]
        ?? Environments.Production;

    var elasticsearchUri = configuration["Elasticsearch_URI"];

    var loggerConfiguration = new LoggerConfiguration()
       .ReadFrom.Configuration(configuration)
       .Enrich.WithProperty("ApplicationContext", appName)
       .Enrich.FromLogContext()
       .Enrich.WithK8sPodName(DownwardApiMethod.EnvironmentVariable, environmentVariableName: "POD_NAME")
       .Enrich.WithK8sPodNamespace(DownwardApiMethod.EnvironmentVariable)
       .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Properties:j}{NewLine}{Exception}");

    if (!string.IsNullOrEmpty(elasticsearchUri))
    {
        loggerConfiguration
           .WriteTo.Elasticsearch(
                new ElasticsearchSinkOptions(new Uri(elasticsearchUri))
                {
                    AutoRegisterTemplate = true,
                    AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv6,
                    IndexFormat = $"bk-{environment.ToLower().Replace(".", "-")}-{DateTime.UtcNow:yyyy-MM}"
                });
    }

    return loggerConfiguration.CreateLogger();
}

(int httpPort, int grpcPort) GetDefinedPorts(IConfiguration config)
{
    var grpcPort = config.GetValue("GRPC_PORT", 81);
    var port = config.GetValue("PORT", 80);

    return (port, grpcPort);
}
