using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using SiteSense.Api.Realtime;

namespace SiteSense.Tests.Api.Realtime;

public static class HubClient
{
    public static HubConnection Create(ApiFactory factory, string token)
    {
        factory.CreateClient(); // starts the test server before factory.Server is used
        return new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, SiteHub.RealtimePath), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling; // TestServer has no real sockets
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
    }
}
