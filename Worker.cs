using System.Collections.Concurrent;
using System.Diagnostics;
using OptimizelySDK;
using OptimizelySDK.Config;
using OptimizelySDK.Entity;

namespace Optimizely_Decision_Worker_Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly Optimizely _optimizelyInstance;

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;

        var sdkKey = configuration["Optimizely:SdkKey"];
        var configManager = new HttpProjectConfigManager.Builder()
            .WithSdkKey(sdkKey)
            .Build(false);

        _optimizelyInstance = OptimizelyFactory.NewDefaultInstance(configManager);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        const int iterations = 1000;
        var runStopwatch = Stopwatch.StartNew();

        var tasks = new List<Task>();
        var results = new ConcurrentDictionary<int, int>();

        _logger.LogInformation($"Running {iterations} iterations, with timing output at the end...");

        for (int i = 0; i < iterations; i++)
        {
            int iteration = i;
            tasks.Add(Task.Run(async () =>
            {
                var taskStopwatch = Stopwatch.StartNew();

                var accountIdGuid = Guid.NewGuid().ToString(); // Simulate an account identifier
                var attributes = new UserAttributes
                {
                    { "accountIdGuid", accountIdGuid },
                    { "ring", "us" }
                };

                var userContext = _optimizelyInstance.CreateUserContext($"user{iteration}", attributes);

                _ = await Task.Run(() => userContext?.DecideAll());

                taskStopwatch.Stop();

                results.TryAdd(iteration, (int)taskStopwatch.ElapsedMilliseconds);
            }, stoppingToken));
        }

        var benchmarkTask = Task.WhenAll(tasks);

        await Task.WhenAny(benchmarkTask, Task.Delay(Timeout.Infinite, stoppingToken));

        if (benchmarkTask.IsCompleted)
        {
            runStopwatch.Stop();
            _logger.LogInformation($"Finished running {iterations} iterations after {runStopwatch.ElapsedMilliseconds} ms");
            _logger.LogInformation($"Average time per iteration: {results.Values.Average()} ms");
            _logger.LogInformation($"Median time per iteration: {results.Values.OrderBy(x => x).ElementAt(iterations / 2)} ms");
            _logger.LogInformation($"Fastest time per iteration: {results.Values.Min()} ms");
            _logger.LogInformation($"Slowest time per iteration: {results.Values.Max()} ms");
        }
        else
        {
            _logger.LogInformation("Cancellation requested before completion. Stopped");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }
}
