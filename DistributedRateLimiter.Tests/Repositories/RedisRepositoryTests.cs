using StackExchange.Redis;
using DistributedRateLimiter.Repositories;

namespace DistributedRateLimiter.Tests.Repositories;

public class RedisRateLimiterRepositoryTests
{
    private readonly IDatabase _redisDb;
    private readonly RedisRepository _repository;
    private const string ConnectionString = "localhost:6379";

    public RedisRateLimiterRepositoryTests()
    {
        var redis = ConnectionMultiplexer.Connect(ConnectionString);
        _redisDb = redis.GetDatabase();
        _repository = new RedisRepository(_redisDb);
    }

    [Fact]
    public async Task ExecuteScriptAsync_ShouldCorrectlyRunLuaScript_AndReturnParsedResult()
    {
        // Arrange
        // Простой скрипт, который складывает два числа и возвращает результат.
        // Он проверяет, что мы правильно передаем аргументы и парсим ответ.
        const string testScript = @"
            local val1 = tonumber(ARGV[1])
            local val2 = tonumber(ARGV[2])
            return { val1, val2, val1 + val2 }";

        var resourceId = "test:key";
        var val1 = 10;
        var val2 = 20;

        // Act
        var result = await _repository.ExecuteScriptAsync(testScript, resourceId, val1, val2);

        // Assert
        // Проверяем, что скрипт вернул массив из 3-х элементов
        Assert.Equal(3, result.Length);
        // Проверяем, что результат сложения верный
        Assert.Equal(val1 + val2, (int)result[2]);
    }
}