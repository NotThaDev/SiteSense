using SiteSense.Api.Models;
using SiteSense.Api.Store;

namespace SiteSense.StoreCrashWriter;

// Child process for the store crash test: saves forever, printing each completed save.
public static class CrashWriter
{
    public static async Task Main(string[] args)
    {
        if (args.Length > 1 && args[1] == "append")
        {
            await AppendForever(args[0], args.Length > 2 ? long.Parse(args[2]) : 0);
            return;
        }
        var store = new FileDocumentStore(args[0]);
        var payload = new string('x', 256 * 1024);
        for (var i = 0; ; i++)
        {
            var id = $"doc-{i % 5}";
            var current = await store.GetAsync<CrashDoc>("tenant-a", id, CancellationToken.None);
            await store.SaveAsync("tenant-a", new CrashDoc
            {
                Id = id,
                TenantId = "tenant-a",
                Version = current?.Version ?? 0,
                Counter = (current?.Version ?? 0) + 1,
                Payload = payload,
            }, CancellationToken.None);
            Console.WriteLine(i + 1);
        }
    }

    public const long T0 = 1793268000000;

    // Append mode: batches of 500 positions, one per ms, printing the last counter once the batch is durable.
    private static async Task AppendForever(string root, long start)
    {
        var log = new FileAppendLog(root);
        var counter = start;
        while (true)
        {
            var batch = new List<Position>(500);
            for (var i = 0; i < 500; i++) batch.Add(new Position("dev-1", T0 + ++counter, 45.5, 9.2, 4.5));
            await log.AppendAsync("tenant-a", "dev-1", batch, CancellationToken.None);
            Console.WriteLine(counter);
        }
    }
}
