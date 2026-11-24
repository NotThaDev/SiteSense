using SiteSense.Api.Store;

namespace SiteSense.StoreCrashWriter;

// Child process for the store crash test: saves forever, printing each completed save.
public static class CrashWriter
{
    public static async Task Main(string[] args)
    {
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
                Counter = (current?.Version ?? 0) + 1,
                Payload = payload,
            }, CancellationToken.None);
            Console.WriteLine(i + 1);
        }
    }
}
