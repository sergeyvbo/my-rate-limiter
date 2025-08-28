using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace DistributedRateLimiter.Tests;

public class TestWebApplicationFactory: WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IConnectionMultiplexer>();

            services.TryAddSingleton<IConnectionMultiplexer>(
                ConnectionMultiplexer.Connect("localhost:6379"));
        });
    }
}
