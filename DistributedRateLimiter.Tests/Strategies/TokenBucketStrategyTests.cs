using DistributedRateLimiter.Strategies;

namespace DistributedRateLimiter.Tests.Strategies;

public class TokenBucketStrategyTests
{
    [Fact]
    public void IsRequestAllowed_ShouldReturnTrue_WhenBucketHasEnoughTokens()
    {
        // Arrange (Подготовка)
        var capacity = 10; // Емкость ведра - 10 токенов
        var refillRate = 1; // 1 токен в секунду
        var strategy = new TokenBucketStrategy(capacity, refillRate);
        var resourceId = "user:123";

        // Act (Действие)
        var result = strategy.IsRequestAllowed(resourceId);

        // Assert (Проверка)
        Assert.True(result.IsAllowed);
        Assert.Equal(capacity - 1, result.TokensLeft);
    }

    [Fact]
    public void IsRequestAllowed_ShouldReturnFalse_WhenBucketIsEmpty()
    {
        // Arrange
        var capacity = 1; // Ведро емкостью всего 1 токен
        var refillRate = 1;
        var strategy = new TokenBucketStrategy(capacity, refillRate);
        var resourceId = "user:123";

        strategy.IsRequestAllowed(resourceId); // Первый запрос, который опустошит ведро

        // Act
        var result = strategy.IsRequestAllowed(resourceId); // Второй запрос

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.TokensLeft);
    }

    [Fact]
    public async Task IsRequestAllowed_ShouldRefillTokens_AfterTimePasses()
    {
        // Arrange
        var capacity = 1;
        var refillRatePerSecond = 1; // 1 токен в секунду
        var strategy = new TokenBucketStrategy(capacity, refillRatePerSecond);
        var resourceId = "user:123";

        strategy.IsRequestAllowed(resourceId); // Опустошаем ведро

        // Act
        await Task.Delay(1100); // Ждем чуть больше секунды, чтобы токен восстановился
        var result = strategy.IsRequestAllowed(resourceId);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(0, result.TokensLeft); // После взятия восстановленного токена снова 0
    }
}