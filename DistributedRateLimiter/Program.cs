using DistributedRateLimiter.Configuration;
using DistributedRateLimiter.Monitoring;
using DistributedRateLimiter.Repositories;
using DistributedRateLimiter.Services;
using DistributedRateLimiter.Strategies;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Prometheus;
using StackExchange.Redis;

public class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(8080, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
            options.ListenAnyIP(8081, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1;
            });
        });

        builder.Services.AddGrpc();

        var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
        builder.Services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConnectionString));
        builder.Services.AddSingleton<RateLimiterMetrics>();

        builder.Services.AddScoped(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

        builder.Services.AddScoped<RedisRepository>();
        builder.Services.AddScoped<IRateLimitingStrategy, TokenBucketStrategy>();
        builder.Services.AddScoped<RateLimiterService>();

        var rateLimiterSettings = builder.Configuration.GetSection("RateLimiterSettings");
        builder.Services.Configure<RateLimiterSettings>(rateLimiterSettings);

        var app = builder.Build();
        app.UseRouting();
        app.MapMetrics();
        app.MapGrpcService<RateLimiterGrpcService>();

        app.Run();
    }
}