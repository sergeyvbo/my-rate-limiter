using DistributedRateLimiter.Configuration;
using DistributedRateLimiter.Repositories;
using DistributedRateLimiter.Strategies;
using StackExchange.Redis;

namespace DistributedRateLimiter.Tests.Strategies;

public class TokenBucketStrategyIntegrationTests
{
    private readonly TokenBucketStrategy _strategy;

    public TokenBucketStrategyIntegrationTests()
    {
        var redis = ConnectionMultiplexer.Connect("localhost:6379");
        var db = redis.GetDatabase();
        var repository = new RedisRepository(db);
        _strategy = new TokenBucketStrategy(repository);
    }

    [Fact]
    public async Task IsRequestAllowedAsync_ShouldCorrectlyApplyTokenBucketLogic()
    {
        // Arrange
        var resourceId = $"user:integ_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 10, RefillRatePerSecond = 1 };

        // Act
        var (isAllowed1, tokensLeft1) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert
        Assert.True(isAllowed1);
        Assert.Equal(9, tokensLeft1);

        // Act
        for (int i = 0; i < 9; i++)
        {
            await _strategy.IsRequestAllowedAsync(resourceId, policy);
        }
        var (isAllowed2, tokensLeft2) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert
        Assert.False(isAllowed2);
        Assert.Equal(0, tokensLeft2);
    }
}
