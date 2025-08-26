using DistributedRateLimiter.Repositories;
using DistributedRateLimiter.Services;
using StackExchange.Redis;

public class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddGrpc();

        builder.Services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect("localhost:6379"));

        builder.Services.AddScoped(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

        builder.Services.AddScoped<RedisRepository>();
        builder.Services.AddScoped<RateLimiterService>();

        var app = builder.Build();

        app.MapGrpcService<RateLimiterGrpcService>();

        app.Run();
    }
}