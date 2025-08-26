using StackExchange.Redis;
using DistributedRateLimiter.Repositories;

namespace DistributedRateLimiter.Tests.Repositories;

public class RedisRateLimiterRepositoryTests
{
    private readonly IDatabase _redisDb;
    private const string ConnectionString = "localhost:6379"; // Убедитесь, что Redis запущен

    public RedisRateLimiterRepositoryTests()
    {
        var redis = ConnectionMultiplexer.Connect(ConnectionString);
        _redisDb = redis.GetDatabase();
    }

    [Fact]
    public async Task ApplyTokenBucketLogic_ShouldCorrectlyDecrementTokens_AndReturnState()
    {
        // Arrange
        var repository = new RedisRepository(_redisDb);
        var resourceId = $"user:{Guid.NewGuid()}"; // Уникальный ключ для каждого теста
        var capacity = 10;
        var refillRatePerSecond = 1;

        // Act: Первый вызов для нового пользователя
        var (isAllowed1, tokensLeft1) = await repository.ApplyTokenBucketLogic(
            resourceId, capacity, refillRatePerSecond);

        // Assert: Проверяем, что первый запрос успешен
        Assert.True(isAllowed1);
        Assert.Equal(9, tokensLeft1);

        // Act: Истощаем все оставшиеся токены
        for (int i = 0; i < 9; i++)
        {
            await repository.ApplyTokenBucketLogic(resourceId, capacity, refillRatePerSecond);
        }

        // Act: Последний запрос, который должен быть отклонен
        var (isAllowed2, tokensLeft2) = await repository.ApplyTokenBucketLogic(
            resourceId, capacity, refillRatePerSecond);

        // Assert: Проверяем, что запрос отклонен, когда токены закончились
        Assert.False(isAllowed2);
        Assert.Equal(0, tokensLeft2);
    }
}