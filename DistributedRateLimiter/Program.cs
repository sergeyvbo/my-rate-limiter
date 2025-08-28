using DistributedRateLimiter.Configuration;
using DistributedRateLimiter.Repositories;
using DistributedRateLimiter.Services;
using DistributedRateLimiter.Strategies;
using StackExchange.Redis;

public class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddGrpc();

        var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
        builder.Services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConnectionString));

        builder.Services.AddScoped(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

        builder.Services.AddScoped<RedisRepository>();
        builder.Services.AddScoped<IRateLimitingStrategy, TokenBucketStrategy>();
        builder.Services.AddScoped<RateLimiterService>();

        var rateLimiterSettings = builder.Configuration.GetSection("RateLimiterSettings");
        builder.Services.Configure<RateLimiterSettings>(rateLimiterSettings);

        var app = builder.Build();

        app.MapGrpcService<RateLimiterGrpcService>();

        app.Run();
    }
}