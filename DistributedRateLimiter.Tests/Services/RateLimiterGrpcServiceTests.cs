using Grpc.Net.Client;
using Ratelimiter;

using RateLimiterClient = Ratelimiter.RateLimiter.RateLimiterClient;

namespace DistributedRateLimiter.Tests.Services;

public class RateLimiterGrpcServiceTests
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly GrpcChannel _channel;

    public RateLimiterGrpcServiceTests(TestWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        _channel = GrpcChannel.ForAddress(client.BaseAddress, new GrpcChannelOptions
        {
            HttpClient = client,
        });
    }

    [Fact]
    public async Task Check_ShouldReturnIsAllowedTrue_ForFirstRequest()
    {
        // Arrange
        var grpcClient = new RateLimiterClient(_channel);
        var resourceId = $"user:{Guid.NewGuid()}";

        // Act
        var response = await grpcClient.CheckAsync(new RateLimitRequest { ResourceId = resourceId });

        // Assert
        Assert.True(response.IsAllowed);
        Assert.True(response.TokensLeft > 0);
    }

    [Fact]
    public async Task Check_ShouldReturnIsAllowedFalse_WhenTokensAreExhausted()
    {
        // Arrange
        var grpcClient = new RateLimiterClient(_channel);
        var resourceId = $"user:e2e_{Guid.NewGuid()}";

        // Act
        var depletionTasks = new List<Task>();
        for (int i = 0; i < 111; i++)
        {
            depletionTasks.Add(grpcClient.CheckAsync(new RateLimitRequest { ResourceId = resourceId }).ResponseAsync);
        }

        await Task.WhenAll(depletionTasks);

        var finalResponse = await grpcClient.CheckAsync(new RateLimitRequest { ResourceId = resourceId });

        // Assert
        Assert.False(finalResponse.IsAllowed);
    }
}