using System.Text.Json;

namespace SiteSense.Simulator;

public static class SimulatorApp
{
    private const int MaxBuffered = 5000;
    private const int SetupAttempts = 10;

    public static async Task<int> Main(string[] args)
    {
        SimulatorOptions options;
        try
        {
            options = SimulatorOptions.Parse(args, Environment.GetEnvironmentVariable);
        }
        catch (ArgumentException e)
        {
            Console.WriteLine(e.Message);
            Console.WriteLine("Usage: dotnet run --project simulator/SiteSense.Simulator -- [--api URL] [--devices N] [--interval-s S] [--batch-s S] [--duration-s S] [--seed N] [--no-setup]");
            return 2;
        }

        string siteJson, mapJson;
        PathGraph graph;
        try
        {
            siteJson = await File.ReadAllTextAsync(options.Site);
            mapJson = await File.ReadAllTextAsync(options.Map);
            graph = PathGraph.Load(mapJson);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException)
        {
            Console.WriteLine($"Cannot read input: {e.Message}");
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var time = TimeProvider.System;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var api = new ApiClient(http, options, time);

        var devices = new List<SimulatedDevice>();
        var deviceIds = new List<string>();
        var buffers = new Dictionary<string, List<Fix>>();
        for (var i = 0; i < options.Devices; i++)
        {
            var id = $"{options.DevicePrefix}{i + 1:D3}";
            devices.Add(new SimulatedDevice(id, graph, options.Seed + i));
            deviceIds.Add(id);
            buffers[id] = new List<Fix>();
        }

        if (options.Setup)
        {
            try
            {
                // Keycloak or the API may still be starting, so retry the setup a few times.
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await api.SetupAsync(siteJson, mapJson, deviceIds, cts.Token);
                        break;
                    }
                    catch (Exception e) when (attempt < SetupAttempts && (e is HttpRequestException || (e is TaskCanceledException && !cts.IsCancellationRequested)))
                    {
                        Console.WriteLine("Setup failed, retrying in 5 s");
                        await Task.Delay(TimeSpan.FromSeconds(5), time, cts.Token);
                    }
                }
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Setup failed: {e.Message}");
                return 1;
            }
            catch (Exception e) when (e is InvalidOperationException or JsonException)
            {
                Console.WriteLine(e.Message);
                return 1;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                Console.WriteLine("Simulator stopped");
                return 0;
            }
        }

        var startMs = options.StartMs ?? time.GetUtcNow().ToUnixTimeMilliseconds();
        var intervalMs = (long)(options.IntervalS * 1000);
        var batchTicks = Math.Max(1, (int)Math.Round(options.BatchS / options.IntervalS));
        var maxTicks = options.DurationS > 0 ? (long)Math.Ceiling(options.DurationS / options.IntervalS) : long.MaxValue;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.IntervalS), time);
        long tick = 0;
        while (tick < maxTicks)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(cts.Token)) break;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                break;
            }

            var ts = startMs + tick * intervalMs;
            foreach (var device in devices)
            {
                var buffer = buffers[device.DeviceId];
                buffer.Add(device.Step(ts, options.IntervalS));
                if (buffer.Count > MaxBuffered) buffer.RemoveRange(0, buffer.Count - MaxBuffered);
            }
            tick++;

            if (tick % batchTicks == 0)
            {
                try
                {
                    await SendRoundAsync(api, buffers, cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        // Flush what is still buffered, even after Ctrl+C, with its own deadline.
        using var finalCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await SendRoundAsync(api, buffers, finalCts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Final flush timed out");
        }

        Console.WriteLine("Simulator stopped");
        return 0;
    }

    private static async Task SendRoundAsync(ApiClient api, Dictionary<string, List<Fix>> buffers, CancellationToken ct)
    {
        var devices = 0;
        var sent = 0;
        var failures = 0;
        foreach (var (id, buffer) in buffers)
        {
            if (buffer.Count == 0) continue;
            devices++;

            var batch = buffer.ToList();
            switch (await api.SendBatchAsync(id, batch, ct))
            {
                case SendResult.Accepted:
                    sent += batch.Count;
                    buffer.Clear();
                    break;
                case SendResult.Dropped:
                    failures++;
                    buffer.Clear();
                    break;
                default:
                    // Retry: keep the fixes for the next round.
                    failures++;
                    break;
            }
        }
        if (devices > 0) Console.WriteLine($"Round: {devices} devices, {sent} positions sent, {failures} failures");
    }
}
