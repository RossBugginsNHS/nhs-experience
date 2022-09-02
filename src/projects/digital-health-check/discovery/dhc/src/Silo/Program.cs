﻿
using System;
using System.Net;
using System.Threading.Tasks;
using dhc;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

var builder = Host.CreateDefaultBuilder(args);



builder.ConfigureServices((hostContext, services) =>
    {
        
   
services.AddStackExchangeRedisCache(options =>
 {
    
     options.Configuration = hostContext.Configuration.GetSection("Redis")["ConnectionString"];
     options.InstanceName = "dhcsilo";
 });


        services.AddHealthCheck((config) =>
        {

            config
                .AddWebBmiProvider(hostContext.Configuration)
                .AddWebBpProvider(hostContext.Configuration)
                .AddPostCodeApi(hostContext.Configuration);

            config.Services.AddSingleton<RabbitMqClient>();
            config.Services.AddSingleton<RabbitMqChannel>();
        });

        services.Configure<OrleansConnection>(hostContext.Configuration.GetSection(OrleansConnection.Position));
        services.Configure<RabbitMqSettings>(hostContext.Configuration.GetSection(RabbitMqSettings.Location));
        services.Configure<EventStoreSettings>(hostContext.Configuration.GetSection(EventStoreSettings.Position));
        services.AddHostedService<GracefulShutdownHosted>();
    })
    
    .UseOrleans((hostContext, builder) =>
    {
        
        var orleansConnection = new OrleansConnection();
        hostContext.Configuration.GetSection(OrleansConnection.Position).Bind(orleansConnection);
        
        var hostEntry = Dns.GetHostEntry(orleansConnection.DbHost);
        var ip = hostEntry.AddressList[0];

        //var primarySiloEndpoint = new IPEndPoint(ip, 11112);
        builder.AddAdoNetGrainStorageAsDefault(options =>
        {
            options.Invariant = "System.Data.SqlClient";
            options.ConnectionString = $"Server={orleansConnection.DbHost};Database=orleans;User Id=sa;Password=Yukon900;";
            options.UseJsonFormat = true;
        });

        builder.UseAdoNetClustering(options =>
        {
            options.Invariant = "System.Data.SqlClient";
            options.ConnectionString = $"Server={orleansConnection.DbHost};Database=orleans;User Id=sa;Password=Yukon900;";
        });

       // builder.UseDevelopmentClustering(primarySiloEndpoint)
        builder.Configure<ClusterOptions>(options =>
        {
            options.ClusterId = "dev";
            options.ServiceId = "OrleansBasics";
        })
           .Configure<EndpointOptions>(options =>
                {
                    var host = Dns.GetHostEntry(Dns.GetHostName());
                    options.AdvertisedIPAddress =host.AddressList[0];
                    options.SiloPort = 11111;
                    options.GatewayPort = 30000;
                    options.SiloListeningEndpoint = new IPEndPoint(IPAddress.Any, 11111);
                    options.GatewayListeningEndpoint = new IPEndPoint(IPAddress.Any, 30000);
                })
        .ConfigureApplicationParts(parts => parts.AddApplicationPart(typeof(HealthCheckGrain).Assembly).WithReferences())
        .ConfigureLogging(logging => logging.AddConsole());
      
    });


var app = builder.Build();

var loggerTest = app.Services.GetService<ILogger<OrleansConnection>>();
loggerTest.LogTrace("Test a trace message 123");
loggerTest.LogInformation("Test an info message 123");
loggerTest.LogWarning("Test a warning message 123");
loggerTest.LogError("Test an error message 123");

await app.RunAsync();

public class GracefulShutdownHosted : IHostedService
{
    ISiloHost _siloHost;
    ILogger<GracefulShutdownHosted> _logger;
    public GracefulShutdownHosted(ISiloHost siloHost, ILogger<GracefulShutdownHosted> logger)
    {
        _siloHost = siloHost;
        _logger = logger;
    }
    public Task StartAsync(CancellationToken cancellationToken)
    {
       return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Shutting down silo host gracefully");
       await _siloHost.StopAsync();
    }
}