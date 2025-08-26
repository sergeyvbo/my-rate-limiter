using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Ratelimiter;

// Определяем наш gRPC клиент, сгенерированный из .proto файла
using RateLimiterClient = Ratelimiter.RateLimiter.RateLimiterClient;

namespace DistributedRateLimiter.Tests.Services;

public class RateLimiterGrpcServiceTests
    : IClassFixture<WebApplicationFactory<Program>> // Используем WebApplicationFactory для запуска сервиса в памяти
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimiterGrpcServiceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Check_ShouldReturnIsAllowedTrue_ForFirstRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        var grpcChannel = GrpcChannel.ForAddress(client.BaseAddress, new GrpcChannelOptions
        {
            HttpClient = client
        });
        var grpcClient = new RateLimiterClient(grpcChannel);
        var resourceId = $"user:{Guid.NewGuid()}";

        // Act
        var response = await grpcClient.CheckAsync(new RateLimitRequest { ResourceId = resourceId });

        // Assert
        Assert.True(response.IsAllowed);
        Assert.True(response.TokensLeft > 0);
    }
}