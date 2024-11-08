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

        _logger.LogInformation($"Optimizely initialized with SDK Key '{sdkKey}'");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        const int iterations = 1000;
        _logger.LogInformation($"Running {iterations} iterations, with timing output at the end...");

        var runStopwatch = Stopwatch.StartNew();

        var tasks = new List<Task>();
        var results = new ConcurrentDictionary<int, int>();
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            tasks.Add(Task.Run(async () =>
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation($"Cancellation requested before iteration {iteration} started.");
                    return;
                }

                var taskStopwatch = Stopwatch.StartNew();

                var accountIdGuid = Guid.NewGuid().ToString(); // Simulate an account identifier
                var attributes = new UserAttributes
                {
                    { "accountIdGuid", accountIdGuid },
                    { "ring", "us" }
                };

                var userContext = _optimizelyInstance.CreateUserContext($"user{iteration}", attributes);

                try
                {
                    _ = await Task.Run(() => userContext?.DecideAll(), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation($"Iteration {iteration} was canceled.");
                    return;
                }

                taskStopwatch.Stop();
                var elapsedTime = taskStopwatch.ElapsedMilliseconds;

                _logger.LogInformation($"Iteration {iteration} took {elapsedTime} ms");

                results.TryAdd(iteration, (int)elapsedTime);
            }, stoppingToken));
        }

        var benchmarkTask = Task.WhenAll(tasks);

        await Task.WhenAny(benchmarkTask, Task.Delay(Timeout.Infinite, stoppingToken));

        runStopwatch.Stop();

        if (benchmarkTask.IsCanceled)
        {
            _logger.LogInformation("Cancellation requested before completion. ");
        }

        if (results.IsEmpty)
        {
            _logger.LogInformation("No iterations completed successfully.");
            return;
        }

        var sortedResults = results.Values.OrderBy(x => x).ToList();
        var medianIndex = sortedResults.Count / 2;

        _logger.LogInformation($"Finished running {results.Count} iterations after {runStopwatch.ElapsedMilliseconds} ms");
        _logger.LogInformation($"Average time per iteration: {results.Values.Average()} ms");
        _logger.LogInformation($"Median time per iteration: {sortedResults.ElementAt(medianIndex)} ms");
        _logger.LogInformation($"Fastest time per iteration: {results.Values.Min()} ms");
        _logger.LogInformation($"Slowest time per iteration: {results.Values.Max()} ms");
    }
}
