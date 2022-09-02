using System;
using System.Text;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using UnitsNet.Serialization.JsonNet;
using MediatR;
using Orleans;
using Polly.Contrib.WaitAndRetry;
using Polly;

namespace dhc;

public class CalculateHealthCheckRabbitMqListener : RabbitListener
{

    private readonly IPublisher _publisher;
    private readonly ILogger<RabbitListener> _logger;

    // Because the Process function is a delegate callback, if you inject other services directly, they are not in the same scope,
    // To call other Service instances here, you can only get instance objects after IServiceProvider CreateScope
    private readonly IServiceProvider _services;

    public CalculateHealthCheckRabbitMqListener(
        IServiceProvider services,
        RabbitMqChannel model,
        ILogger<RabbitListener> logger,
         IPublisher publisher) : base(model, logger)
    {
        base.RouteKey = "*";
        base.QueueName = "dhc.healthchecks";
        _logger = logger;
        _services = services;
        _publisher = publisher;
    }

    public async override Task<bool> Process(string message, CancellationToken cancellationToken = default)
    {
        var taskMessage = JToken.Parse(message);
        if (taskMessage == null)
        {
            // When false is returned, the message is rejected directly, indicating that it cannot be processed
            return false;
        }
        try
        {


            var delay = Backoff.DecorrelatedJitterBackoffV2(medianFirstRetryDelay: TimeSpan.FromSeconds(1), retryCount: 10);

            var policy = Policy.Handle<Exception>()
            .WaitAndRetryAsync(
                delay,
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    context["totalRetries"] = retryAttempt;
                    context["retryWaitTime"] = timespan;
                    _logger
                        .LogWarning("After {exception} Delaying Rabbit mq calling orleans client for {delay}ms, then making retry {retry}.", outcome, timespan.TotalMilliseconds, retryAttempt);
                }
            );


            var jsonSerializerSettings = new JsonSerializerSettings { Formatting = Formatting.Indented };
            jsonSerializerSettings.Converters.Add(new UnitsNetIQuantityJsonConverter());

            var obj = JsonConvert.DeserializeObject<HealthCheckData>(message, jsonSerializerSettings);

            _logger.LogInformation("calling grains add data");

            await policy.ExecuteAsync(async (ct) =>
            {
                using (var scope = _services.CreateScope())
                {
                    if (!ct.IsCancellationRequested)
                    {
                        var clientFactory = scope.ServiceProvider.GetRequiredService<ClusterClientFactory>();
                        var _client = await clientFactory.GetClientAsync(TimeSpan.FromSeconds(10), ct);
                        var friend = _client.GetGrain<IHealthCheckGrain>(obj.HealthCheckDataId.id);
                        await friend.AddData(obj);
                        _logger.LogInformation("Sent message to Orleans.");
                    }
                }
            }, cancellationToken);


            _logger.LogInformation("calling grains calculate");


            await policy.ExecuteAsync(async (ct) =>
            {
                using (var scope = _services.CreateScope())
                {
                    var clientFactory = scope.ServiceProvider.GetRequiredService<ClusterClientFactory>();
                        var _client = await clientFactory.GetClientAsync(TimeSpan.FromSeconds(10), ct);
                    
                        var friend = _client.GetGrain<IHealthCheckGrain>(obj.HealthCheckDataId.id);
                    
                    var tsc = new GrainCancellationTokenSource();
                    var cancelledSource = new TaskCompletionSource();
                    using var _ = ct.Register(() => cancelledSource.SetResult());

                    var cancellationTokenSource = new CancellationTokenSource();
                    if (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            var statusTask = Task.Run(async () =>
                            {
                                int counter = 1;
                                while (!cancellationTokenSource.Token.IsCancellationRequested)
                                {
                                    _logger.LogDebug("Check {counter}: Waiting on response from grain Calculate for {HealthCheckDataId}", counter, obj.HealthCheckDataId.id);
                                    counter++;
                                    await Task.Delay(1000);
                                }
                                _logger.LogDebug("Status logger for Waiting on response from grain Calculate has finished for {HealthCheckDataId}", obj.HealthCheckDataId.id);
                            });

                            var calculateTask = friend.Calculate(tsc.Token);
                            Task completedTask = await Task.WhenAny(
                               calculateTask,
                               cancelledSource.Task);

                            cancellationTokenSource.Cancel();
                            await statusTask;
                            try
                            {
                                await completedTask;
                            }
                            catch(TimeoutException ex)
                            {
                                if(completedTask == calculateTask)
                                    await tsc.Cancel();
                            }

                            if (completedTask == cancelledSource.Task)
                            {
                                _logger.LogInformation("Sending CANCEL message to Orleans.");
                                await tsc.Cancel();
                                _logger.LogInformation("Sent CANCEL message to Orleans.");

                            }
                            else if (completedTask == calculateTask)
                            {
                                _logger.LogInformation("Sent message to Orleans.");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Exception in grain response");
                            _logger.LogInformation("Sending CANCEL message to Orleans.");
                            await tsc.Cancel();
                            _logger.LogInformation("Sent CANCEL message to Orleans.");

                            throw;
                        }
                    }

                }
            }, cancellationToken);

            _logger.LogInformation("Called grains calculate data");

            return true;
        }

        catch (Exception ex)
        {
            _logger.LogInformation($"Process fail,error:{ex.Message},stackTrace:{ex.StackTrace},message:{message}");
            _logger.LogError(-1, ex, "Process fail");
            return false;
        }

    }
}
