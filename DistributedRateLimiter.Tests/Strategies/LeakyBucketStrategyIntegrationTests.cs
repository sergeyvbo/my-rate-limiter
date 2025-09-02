using DistributedRateLimiter.Configuration;
using DistributedRateLimiter.Repositories;
using DistributedRateLimiter.Strategies;
using StackExchange.Redis;

namespace DistributedRateLimiter.Tests.Strategies;

public class LeakyBucketStrategyIntegrationTests
{
    private readonly LeakyBucketStrategy _strategy;
    private readonly RedisRepository _redisRepository;

    public LeakyBucketStrategyIntegrationTests()
    {
        // Настройка Redis подключения
        var redis = ConnectionMultiplexer.Connect("localhost:6379");
        var db = redis.GetDatabase();
        
        // Инициализация RedisRepository
        _redisRepository = new RedisRepository(db);
        
        // Создание экземпляра LeakyBucketStrategy для тестирования
        _strategy = new LeakyBucketStrategy(_redisRepository);
    }

    [Fact]
    public async Task IsRequestAllowedAsync_FirstRequestToEmptyBucket_ShouldBeAllowed()
    {
        // Arrange
        var resourceId = $"leaky_bucket_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 5, RefillRatePerSecond = 1 };

        // Act
        var (isAllowed, tokensLeft) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert
        Assert.True(isAllowed);
        Assert.Equal(4, tokensLeft); // Capacity - 1 (добавленный запрос)
    }

    [Fact]
    public async Task IsRequestAllowedAsync_FillBucketToMaxCapacity_ShouldTrackSlotsCorrectly()
    {
        // Arrange
        var resourceId = $"leaky_bucket_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 3, RefillRatePerSecond = 1 };

        // Act & Assert - заполняем ведро до максимальной емкости
        var (isAllowed1, tokensLeft1) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.True(isAllowed1);
        Assert.Equal(2, tokensLeft1);

        var (isAllowed2, tokensLeft2) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.True(isAllowed2);
        Assert.Equal(1, tokensLeft2);

        var (isAllowed3, tokensLeft3) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.True(isAllowed3);
        Assert.Equal(0, tokensLeft3);
    }

    [Fact]
    public async Task IsRequestAllowedAsync_BucketOverflow_ShouldRejectRequest()
    {
        // Arrange
        var resourceId = $"leaky_bucket_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 2, RefillRatePerSecond = 1 };

        // Act - заполняем ведро до максимума
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Act - пытаемся добавить еще один запрос при переполнении
        var (isAllowed, tokensLeft) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert
        Assert.False(isAllowed);
        Assert.Equal(0, tokensLeft);
    }

    [Fact]
    public async Task IsRequestAllowedAsync_MultipleBuckets_ShouldReturnCorrecttokensLeft()
    {
        // Arrange
        var resourceId1 = $"leaky_bucket_test_{Guid.NewGuid()}";
        var resourceId2 = $"leaky_bucket_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 4, RefillRatePerSecond = 2 };

        // Act - тестируем разные ведра независимо
        var (isAllowed1, tokensLeft1) = await _strategy.IsRequestAllowedAsync(resourceId1, policy);
        var (isAllowed2, tokensLeft2) = await _strategy.IsRequestAllowedAsync(resourceId2, policy);

        // Assert - каждое ведро должно иметь свое состояние
        Assert.True(isAllowed1);
        Assert.Equal(3, tokensLeft1);
        Assert.True(isAllowed2);
        Assert.Equal(3, tokensLeft2);

        // Act - добавляем еще запросы в первое ведро
        await _strategy.IsRequestAllowedAsync(resourceId1, policy);
        var (isAllowed3, tokensLeft3) = await _strategy.IsRequestAllowedAsync(resourceId1, policy);

        // Assert - второе ведро не должно быть затронуто
        Assert.True(isAllowed3);
        Assert.Equal(1, tokensLeft3);

        var (isAllowed4, tokensLeft4) = await _strategy.IsRequestAllowedAsync(resourceId2, policy);
        Assert.True(isAllowed4);
        Assert.Equal(2, tokensLeft4); // Второе ведро должно иметь только один запрос
    }

    [Fact]
    public async Task IsRequestAllowedAsync_LeakRequestsOverTime_ShouldReduceRequestCount()
    {
        // Arrange
        var resourceId = $"leaky_bucket_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 5, RefillRatePerSecond = 2 }; // 2 запроса в секунду утекают

        // Act - заполняем ведро до максимума
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Проверяем, что ведро заполнено
        var (isAllowedBeforeLeak, tokensLeftBeforeLeak) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.False(isAllowedBeforeLeak);
        Assert.Equal(0, tokensLeftBeforeLeak);

        // Act - ждем 1 секунду для утечки (должно утечь 2 запроса)
        await Task.Delay(1100); // Добавляем небольшой буфер для точности времени

        // Act - проверяем состояние после утечки
        var (isAllowedAfterLeak, tokensLeftAfterLeak) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - должно быть место для нового запроса после утечки
        Assert.True(isAllowedAfterLeak);
        Assert.True(tokensLeftAfterLeak > 0); // Должны быть свободные слоты после утечки
    }

    [Fact]
    public async Task IsRequestAllowedAsync_PartialLeakOfRequests_ShouldCalculateCorrectSlots()
    {
        // Arrange
        var resourceId = $"leaky_bucket_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 6, RefillRatePerSecond = 3 }; // 3 запроса в секунду утекают

        // Act - заполняем ведро частично (4 запроса из 6)
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Проверяем начальное состояние
        var (isAllowedInitial, tokensLeftInitial) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.True(isAllowedInitial);
        Assert.Equal(1, tokensLeftInitial); // 6 - 5 = 1 слот

        // Act - ждем 0.5 секунды для частичной утечки (должно утечь ~1.5 запроса, округляем до 1)
        await Task.Delay(600);

        // Act - проверяем состояние после частичной утечки
        var (isAllowedAfterPartialLeak, tokensLeftAfterPartialLeak) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - должно быть больше свободных слотов после частичной утечки
        Assert.True(isAllowedAfterPartialLeak);
        Assert.True(tokensLeftAfterPartialLeak > tokensLeftInitial); // Больше слотов доступно после утечки
    }

    [Fact]
    public async Task IsRequestAllowedAsync_FullLeakWithSufficientTime_ShouldEmptyBucket()
    {
        // Arrange
        var resourceId = $"leaky_bucket_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 4, RefillRatePerSecond = 2 }; // 2 запроса в секунду утекают

        // Act - заполняем ведро полностью
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Проверяем, что ведро заполнено
        var (isAllowedFull, tokensLeftFull) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.False(isAllowedFull);
        Assert.Equal(0, tokensLeftFull);

        // Act - ждем достаточно времени для полной утечки всех запросов (4 запроса / 2 в секунду = 2 секунды + буфер)
        await Task.Delay(2500);

        // Act - проверяем состояние после полной утечки
        var (isAllowedAfterFullLeak, tokensLeftAfterFullLeak) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - ведро должно быть пустым, все слоты доступны
        Assert.True(isAllowedAfterFullLeak);
        Assert.Equal(3, tokensLeftAfterFullLeak); // Capacity - 1 (новый добавленный запрос) = 4 - 1 = 3
    }

    [Fact]
    public async Task IsRequestAllowedAsync_CalculateRemainingSlots_ShouldBeAccurateAfterLeak()
    {
        // Arrange
        var resourceId = $"leaky_bucket_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 8, RefillRatePerSecond = 4 }; // 4 запроса в секунду утекают

        // Act - добавляем 6 запросов в ведро
        for (int i = 0; i < 6; i++)
        {
            await _strategy.IsRequestAllowedAsync(resourceId, policy);
        }

        // Проверяем начальное состояние (6 запросов в ведре, 2 слота свободны)
        var (isAllowedBefore, tokensLeftBefore) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.True(isAllowedBefore);
        Assert.Equal(1, tokensLeftBefore); // 8 - 7 = 1 слот

        // Act - ждем 1 секунду (должно утечь 4 запроса)
        await Task.Delay(1100);

        // Act - проверяем точность расчета слотов после утечки
        var (isAllowedAfter, tokensLeftAfter) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - после утечки 4 запросов из 7, остается 3 запроса + 1 новый = 4 запроса
        // Свободных слотов: 8 - 4 = 4
        Assert.True(isAllowedAfter);
        Assert.Equal(4, tokensLeftAfter); // Должно быть 4 свободных слота

        // Act - добавляем еще запросы для проверки точности
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        var (isAllowedFinal, tokensLeftFinal) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - теперь должно быть 7 запросов в ведре, 1 слот свободен
        Assert.True(isAllowedFinal);
        Assert.Equal(1, tokensLeftFinal);
    }

    // Edge Cases Tests - Task 4

    [Fact]
    public async Task IsRequestAllowedAsync_MaximumCapacityBucket_ShouldHandleCapacityOfOne()
    {
        // Arrange - тест для поведения при максимальной емкости ведра (capacity = 1)
        var resourceId = $"leaky_bucket_edge_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 1, RefillRatePerSecond = 1 };

        // Act - первый запрос должен заполнить ведро полностью
        var (isAllowed1, tokensLeft1) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - первый запрос разрешен, но слотов не остается
        Assert.True(isAllowed1);
        Assert.Equal(0, tokensLeft1);

        // Act - второй запрос должен быть отклонен
        var (isAllowed2, tokensLeft2) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - второй запрос отклонен
        Assert.False(isAllowed2);
        Assert.Equal(0, tokensLeft2);

        // Act - ждем утечки одного запроса
        await Task.Delay(1100);

        // Act - после утечки должен быть разрешен новый запрос
        var (isAllowed3, tokensLeft3) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - запрос после утечки разрешен
        Assert.True(isAllowed3);
        Assert.Equal(0, tokensLeft3);
    }

    [Fact]
    public async Task IsRequestAllowedAsync_HighLeakRate_ShouldHandleLeakRateGreaterThanCapacity()
    {
        // Arrange - тест для высокой скорости утечки (leak rate > capacity)
        var resourceId = $"leaky_bucket_edge_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 3, RefillRatePerSecond = 5 }; // Скорость утечки больше емкости

        // Act - заполняем ведро полностью
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Проверяем, что ведро заполнено
        var (isAllowedFull, tokensLeftFull) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.False(isAllowedFull);
        Assert.Equal(0, tokensLeftFull);

        // Act - ждем короткое время (меньше секунды)
        await Task.Delay(700); // 0.7 секунды, должно утечь 3.5 запроса, но в ведре только 3

        // Act - проверяем состояние после утечки
        var (isAllowedAfterLeak, tokensLeftAfterLeak) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - все запросы должны утечь, ведро должно быть пустым
        Assert.True(isAllowedAfterLeak);
        Assert.Equal(2, tokensLeftAfterLeak); // Capacity - 1 (новый запрос) = 3 - 1 = 2

        // Act - добавляем еще запросы для проверки нормальной работы
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        var (isAllowedFinal, tokensLeftFinal) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - должно работать нормально после высокой скорости утечки
        Assert.True(isAllowedFinal);
        Assert.Equal(0, tokensLeftFinal); // 3 запроса в ведре, 0 слотов свободно
    }

    [Fact]
    public async Task IsRequestAllowedAsync_SequentialRequestsWithDifferentIntervals_ShouldHandleVariableTimings()
    {
        // Arrange - тест для последовательных запросов с различными временными интервалами
        var resourceId = $"leaky_bucket_edge_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 4, RefillRatePerSecond = 2 };

        // Act - первый запрос
        var (isAllowed1, tokensLeft1) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.True(isAllowed1);
        Assert.Equal(3, tokensLeft1);

        // Act - второй запрос сразу после первого
        var (isAllowed2, tokensLeft2) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.True(isAllowed2);
        Assert.Equal(2, tokensLeft2);

        // Act - ждем 0.5 секунды (должен утечь 1 запрос)
        await Task.Delay(500);
        var (isAllowed3, tokensLeft3) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.True(isAllowed3);
        Assert.True(tokensLeft3 >= 1); // Должно быть больше слотов после частичной утечки

        // Act - ждем 1.5 секунды (должно утечь еще 3 запроса)
        await Task.Delay(1500);
        var (isAllowed4, tokensLeft4) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.True(isAllowed4);
        Assert.Equal(3, tokensLeft4); // Почти пустое ведро после длительной утечки

        // Act - добавляем несколько запросов подряд
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        await _strategy.IsRequestAllowedAsync(resourceId, policy);
        var (isAllowed5, tokensLeft5) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.True(isAllowed5);
        Assert.Equal(0, tokensLeft5); // Ведро заполнено

        // Act - короткий интервал (0.2 секунды)
        await Task.Delay(200);
        var (isAllowed6, tokensLeft6) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.False(isAllowed6); // Недостаточно времени для утечки
        Assert.Equal(0, tokensLeft6);
    }

    [Fact]
    public async Task IsRequestAllowedAsync_ZeroLeakRate_ShouldNeverLeakRequests()
    {
        // Arrange - тест для поведения при нулевой скорости утечки
        var resourceId = $"leaky_bucket_edge_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 3, RefillRatePerSecond = 0 }; // Нулевая скорость утечки

        // Act - заполняем ведро полностью
        var (isAllowed1, tokensLeft1) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.True(isAllowed1);
        Assert.Equal(2, tokensLeft1);

        var (isAllowed2, tokensLeft2) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.True(isAllowed2);
        Assert.Equal(1, tokensLeft2);

        var (isAllowed3, tokensLeft3) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.True(isAllowed3);
        Assert.Equal(0, tokensLeft3);

        // Act - проверяем, что ведро заполнено
        var (isAllowedFull, tokensLeftFull) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        Assert.False(isAllowedFull);
        Assert.Equal(0, tokensLeftFull);

        // Act - ждем длительное время (при нулевой скорости утечки ничего не должно измениться)
        await Task.Delay(2000);

        // Act - проверяем состояние после ожидания
        var (isAllowedAfterWait, tokensLeftAfterWait) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - состояние не должно измениться, так как скорость утечки равна нулю
        Assert.False(isAllowedAfterWait);
        Assert.Equal(0, tokensLeftAfterWait);

        // Act - ждем еще больше времени для подтверждения
        await Task.Delay(3000);
        var (isAllowedFinalCheck, tokensLeftFinalCheck) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - ведро должно оставаться заполненным навсегда при нулевой скорости утечки
        Assert.False(isAllowedFinalCheck);
        Assert.Equal(0, tokensLeftFinalCheck);
    }

    [Fact]
    public async Task IsRequestAllowedAsync_EdgeCaseCapacityAndLeakRateCombinations_ShouldHandleExtremeValues()
    {
        // Arrange - дополнительный тест для экстремальных комбинаций параметров
        var resourceId1 = $"leaky_bucket_edge_test_{Guid.NewGuid()}";
        var resourceId2 = $"leaky_bucket_edge_test_{Guid.NewGuid()}";
        
        // Очень большая емкость с малой скоростью утечки
        var policy1 = new RateLimitPolicySettings { Capacity = 100, RefillRatePerSecond = 1 };
        
        // Малая емкость с очень большой скоростью утечки
        var policy2 = new RateLimitPolicySettings { Capacity = 2, RefillRatePerSecond = 100 };

        // Act & Assert - тест с большой емкостью
        var (isAllowed1, tokensLeft1) = await _strategy.IsRequestAllowedAsync(resourceId1, policy1);
        Assert.True(isAllowed1);
        Assert.Equal(99, tokensLeft1);

        // Заполняем несколько слотов
        for (int i = 0; i < 5; i++)
        {
            await _strategy.IsRequestAllowedAsync(resourceId1, policy1);
        }

        var (isAllowedAfterFill, tokensLeftAfterFill) = await _strategy.IsRequestAllowedAsync(resourceId1, policy1);
        Assert.True(isAllowedAfterFill);
        Assert.Equal(93, tokensLeftAfterFill); // 100 - 7 = 93

        // Act & Assert - тест с малой емкостью и высокой скоростью утечки
        await _strategy.IsRequestAllowedAsync(resourceId2, policy2);
        await _strategy.IsRequestAllowedAsync(resourceId2, policy2);

        // Ведро заполнено
        var (isAllowedFull2, tokensLeftFull2) = await _strategy.IsRequestAllowedAsync(resourceId2, policy2);
        Assert.False(isAllowedFull2);
        Assert.Equal(0, tokensLeftFull2);

        // Даже очень короткое ожидание должно очистить ведро из-за высокой скорости утечки
        await Task.Delay(100); // 0.1 секунды, должно утечь 10 запросов

        var (isAllowedAfterQuickLeak, tokensLeftAfterQuickLeak) = await _strategy.IsRequestAllowedAsync(resourceId2, policy2);
        Assert.True(isAllowedAfterQuickLeak);
        Assert.Equal(1, tokensLeftAfterQuickLeak); // Ведро должно быть почти пустым
    }

    // Architecture Compatibility Tests - Task 5

    [Fact]
    public void Name_ShouldReturnLeakyBucket()
    {
        // Arrange & Act
        var strategyName = _strategy.Name;

        // Assert - проверяем, что имя стратегии соответствует требованиям
        Assert.Equal("LeakyBucket", strategyName);
    }

    [Fact]
    public void LeakyBucketStrategy_ShouldImplementIRateLimitingStrategyInterface()
    {
        // Arrange & Act & Assert - проверяем соответствие интерфейсу IRateLimitingStrategy
        Assert.IsAssignableFrom<IRateLimitingStrategy>(_strategy);
        
        // Проверяем, что все методы интерфейса доступны
        Assert.NotNull(_strategy.Name);
        Assert.True(_strategy.GetType().GetMethod("IsRequestAllowedAsync") != null);
        
        // Проверяем сигнатуру метода IsRequestAllowedAsync
        var method = _strategy.GetType().GetMethod("IsRequestAllowedAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<(bool isAllowed, int tokensLeft)>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(RateLimitPolicySettings), parameters[1].ParameterType);
    }

    [Fact]
    public async Task IsRequestAllowedAsync_ShouldWorkWithRateLimitPolicySettingsCapacity()
    {
        // Arrange - тест для работы с RateLimitPolicySettings.Capacity
        var resourceId = $"leaky_bucket_capacity_test_{Guid.NewGuid()}";
        
        // Тестируем различные значения Capacity
        var policies = new[]
        {
            new RateLimitPolicySettings { Capacity = 1, RefillRatePerSecond = 1 },
            new RateLimitPolicySettings { Capacity = 5, RefillRatePerSecond = 1 },
            new RateLimitPolicySettings { Capacity = 10, RefillRatePerSecond = 1 }
        };

        foreach (var policy in policies)
        {
            var testResourceId = $"{resourceId}_{policy.Capacity}";
            
            // Act - заполняем ведро до максимальной емкости
            for (int i = 0; i < policy.Capacity; i++)
            {
                var (isAllowed, tokensLeft) = await _strategy.IsRequestAllowedAsync(testResourceId, policy);
                
                // Assert - каждый запрос до достижения емкости должен быть разрешен
                Assert.True(isAllowed);
                Assert.Equal(policy.Capacity - i - 1, tokensLeft);
            }

            // Act - попытка добавить запрос сверх емкости
            var (isAllowedOverflow, tokensLeftOverflow) = await _strategy.IsRequestAllowedAsync(testResourceId, policy);
            
            // Assert - запрос сверх емкости должен быть отклонен
            Assert.False(isAllowedOverflow);
            Assert.Equal(0, tokensLeftOverflow);
        }
    }

    [Fact]
    public async Task IsRequestAllowedAsync_ShouldWorkWithRateLimitPolicySettingsRefillRatePerSecond()
    {
        // Arrange - тест для работы с RateLimitPolicySettings.RefillRatePerSecond как скорости утечки
        var resourceId = $"leaky_bucket_refill_test_{Guid.NewGuid()}";
        
        // Тестируем различные значения RefillRatePerSecond (используется как leak rate)
        var policies = new[]
        {
            new RateLimitPolicySettings { Capacity = 4, RefillRatePerSecond = 1 }, // Медленная утечка
            new RateLimitPolicySettings { Capacity = 4, RefillRatePerSecond = 2 }, // Средняя утечка
            new RateLimitPolicySettings { Capacity = 4, RefillRatePerSecond = 4 }  // Быстрая утечка
        };

        for (int policyIndex = 0; policyIndex < policies.Length; policyIndex++)
        {
            var policy = policies[policyIndex];
            var testResourceId = $"{resourceId}_{policyIndex}";
            
            // Act - заполняем ведро полностью
            for (int i = 0; i < policy.Capacity; i++)
            {
                await _strategy.IsRequestAllowedAsync(testResourceId, policy);
            }

            // Проверяем, что ведро заполнено
            var (isAllowedFull, tokensLeftFull) = await _strategy.IsRequestAllowedAsync(testResourceId, policy);
            Assert.False(isAllowedFull);
            Assert.Equal(0, tokensLeftFull);

            // Act - ждем 1 секунду для утечки
            await Task.Delay(1100);

            // Act - проверяем состояние после утечки
            var (isAllowedAfterLeak, tokensLeftAfterLeak) = await _strategy.IsRequestAllowedAsync(testResourceId, policy);

            // Assert - количество доступных слотов должно зависеть от RefillRatePerSecond
            Assert.True(isAllowedAfterLeak);
            
            // Для разных скоростей утечки ожидаем разное количество доступных слотов
            if (policy.RefillRatePerSecond == 1)
            {
                // При скорости 1 запрос/сек, после 1 секунды должен утечь 1 запрос
                // Остается 3 запроса + 1 новый = 4, свободных слотов = 0
                Assert.True(tokensLeftAfterLeak >= 0);
            }
            else if (policy.RefillRatePerSecond == 2)
            {
                // При скорости 2 запроса/сек, после 1 секунды должно утечь 2 запроса
                // Остается 2 запроса + 1 новый = 3, свободных слотов = 1
                Assert.True(tokensLeftAfterLeak >= 1);
            }
            else if (policy.RefillRatePerSecond == 4)
            {
                // При скорости 4 запроса/сек, после 1 секунды должно утечь все 4 запроса
                // Остается 0 запросов + 1 новый = 1, свободных слотов = 3
                Assert.Equal(3, tokensLeftAfterLeak);
            }
        }
    }

    [Fact]
    public async Task IsRequestAllowedAsync_ShouldIntegrateWithRedisRepository()
    {
        // Arrange - тест для интеграции с RedisRepository
        var resourceId = $"leaky_bucket_redis_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 3, RefillRatePerSecond = 1 };

        // Act - выполняем операции, которые должны сохраняться в Redis
        var (isAllowed1, tokensLeft1) = await _strategy.IsRequestAllowedAsync(resourceId, policy);
        var (isAllowed2, tokensLeft2) = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - проверяем, что состояние сохраняется между вызовами
        Assert.True(isAllowed1);
        Assert.Equal(2, tokensLeft1);
        Assert.True(isAllowed2);
        Assert.Equal(1, tokensLeft2);

        // Act - создаем новый экземпляр стратегии с тем же RedisRepository
        var newStrategy = new LeakyBucketStrategy(_redisRepository);
        var (isAllowed3, tokensLeft3) = await newStrategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - новый экземпляр должен видеть состояние, сохраненное предыдущим экземпляром
        Assert.True(isAllowed3);
        Assert.Equal(0, tokensLeft3); // Должно быть 3-й запрос в том же ведре

        // Act - проверяем переполнение через новый экземпляр
        var (isAllowed4, tokensLeft4) = await newStrategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - состояние должно быть консистентным между экземплярами
        Assert.False(isAllowed4);
        Assert.Equal(0, tokensLeft4);
    }

    [Fact]
    public async Task IsRequestAllowedAsync_ShouldWorkWithDifferentResourceIds()
    {
        // Arrange - тест для работы с различными resourceId (проверка изоляции ресурсов)
        var resourceId1 = $"leaky_bucket_resource_test_1_{Guid.NewGuid()}";
        var resourceId2 = $"leaky_bucket_resource_test_2_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 2, RefillRatePerSecond = 1 };

        // Act - заполняем первый ресурс полностью
        await _strategy.IsRequestAllowedAsync(resourceId1, policy);
        await _strategy.IsRequestAllowedAsync(resourceId1, policy);
        
        var (isAllowedResource1Full, tokensLeftResource1Full) = await _strategy.IsRequestAllowedAsync(resourceId1, policy);
        
        // Assert - первый ресурс должен быть заполнен
        Assert.False(isAllowedResource1Full);
        Assert.Equal(0, tokensLeftResource1Full);

        // Act - проверяем второй ресурс (должен быть независимым)
        var (isAllowedResource2, tokensLeftResource2) = await _strategy.IsRequestAllowedAsync(resourceId2, policy);
        
        // Assert - второй ресурс должен быть пустым и доступным
        Assert.True(isAllowedResource2);
        Assert.Equal(1, tokensLeftResource2); // Capacity - 1 = 2 - 1 = 1

        // Act - добавляем еще один запрос во второй ресурс
        var (isAllowedResource2Second, tokensLeftResource2Second) = await _strategy.IsRequestAllowedAsync(resourceId2, policy);
        
        // Assert - второй ресурс должен принять еще один запрос
        Assert.True(isAllowedResource2Second);
        Assert.Equal(0, tokensLeftResource2Second);

        // Act - проверяем, что первый ресурс все еще заполнен
        var (isAllowedResource1Check, tokensLeftResource1Check) = await _strategy.IsRequestAllowedAsync(resourceId1, policy);
        
        // Assert - первый ресурс должен оставаться заполненным
        Assert.False(isAllowedResource1Check);
        Assert.Equal(0, tokensLeftResource1Check);
    }

    [Fact]
    public void Constructor_ShouldAcceptRedisRepositoryParameter()
    {
        // Arrange & Act - проверяем, что конструктор принимает RedisRepository
        var redis = ConnectionMultiplexer.Connect("localhost:6379");
        var db = redis.GetDatabase();
        var redisRepository = new RedisRepository(db);
        
        // Act - создаем экземпляр стратегии
        var strategy = new LeakyBucketStrategy(redisRepository);
        
        // Assert - проверяем, что экземпляр создан успешно
        Assert.NotNull(strategy);
        Assert.Equal("LeakyBucket", strategy.Name);
        Assert.IsAssignableFrom<IRateLimitingStrategy>(strategy);
    }

    [Fact]
    public async Task IsRequestAllowedAsync_ShouldReturnCorrectTupleStructure()
    {
        // Arrange - тест для проверки корректной структуры возвращаемого значения
        var resourceId = $"leaky_bucket_tuple_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 3, RefillRatePerSecond = 1 };

        // Act
        var result = await _strategy.IsRequestAllowedAsync(resourceId, policy);

        // Assert - проверяем структуру возвращаемого кортежа
        Assert.IsType<(bool isAllowed, int tokensLeft)>(result);
        
        // Проверяем, что можем получить доступ к полям кортежа
        var (isAllowed, tokensLeft) = result;
        Assert.IsType<bool>(isAllowed);
        Assert.IsType<int>(tokensLeft);
        
        // Проверяем логические значения
        Assert.True(isAllowed); // Первый запрос должен быть разрешен
        Assert.True(tokensLeft >= 0); // Количество слотов не может быть отрицательным
        Assert.True(tokensLeft < policy.Capacity); // Слотов должно быть меньше общей емкости
    }



    // Thread-Safety and Distributed Work Tests - Task 6

    [Fact]
    public async Task IsRequestAllowedAsync_ConcurrentRequestsToSameResource_ShouldHandleAtomically()
    {
        // Arrange - тест для одновременных запросов к одному ресурсу
        var resourceId = $"leaky_bucket_concurrent_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 5, RefillRatePerSecond = 1 };
        var concurrentRequestCount = 10;
        var tasks = new List<Task<(bool isAllowed, int tokensLeft)>>();

        // Act - запускаем множество одновременных запросов к одному ресурсу
        for (int i = 0; i < concurrentRequestCount; i++)
        {
            tasks.Add(_strategy.IsRequestAllowedAsync(resourceId, policy));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - проверяем атомарность операций
        var allowedRequests = results.Count(r => r.isAllowed);
        var rejectedRequests = results.Count(r => !r.isAllowed);

        // Должно быть разрешено ровно столько запросов, сколько позволяет емкость ведра
        Assert.Equal(policy.Capacity, allowedRequests);
        Assert.Equal(concurrentRequestCount - policy.Capacity, rejectedRequests);

        // Проверяем, что количество слотов корректно для разрешенных запросов
        var allowedResults = results.Where(r => r.isAllowed).ToList();
        var expectedtokensLeft = new[] { 4, 3, 2, 1, 0 }; // Для capacity = 5

        // Сортируем результаты по количеству оставшихся слотов для проверки последовательности
        var sortedAllowedResults = allowedResults.OrderByDescending(r => r.tokensLeft).ToList();
        
        for (int i = 0; i < sortedAllowedResults.Count; i++)
        {
            Assert.Equal(expectedtokensLeft[i], sortedAllowedResults[i].tokensLeft);
        }

        // Все отклоненные запросы должны показывать 0 свободных слотов
        var rejectedResults = results.Where(r => !r.isAllowed).ToList();
        Assert.All(rejectedResults, result => Assert.Equal(0, result.tokensLeft));
    }

    [Fact]
    public async Task IsRequestAllowedAsync_AtomicityOfOperations_ShouldMaintainConsistentState()
    {
        // Arrange - тест для проверки атомарности операций
        var resourceId = $"leaky_bucket_atomicity_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 3, RefillRatePerSecond = 1 };
        var iterationCount = 20;

        // Act - выполняем множество итераций с одновременными запросами
        for (int iteration = 0; iteration < iterationCount; iteration++)
        {
            var testResourceId = $"{resourceId}_{iteration}";
            var concurrentTasks = new List<Task<(bool isAllowed, int tokensLeft)>>();

            // Запускаем 6 одновременных запросов (больше чем capacity = 3)
            for (int i = 0; i < 6; i++)
            {
                concurrentTasks.Add(_strategy.IsRequestAllowedAsync(testResourceId, policy));
            }

            var results = await Task.WhenAll(concurrentTasks);

            // Assert - в каждой итерации должно быть ровно 3 разрешенных запроса
            var allowedCount = results.Count(r => r.isAllowed);
            var rejectedCount = results.Count(r => !r.isAllowed);

            Assert.Equal(3, allowedCount); // Ровно capacity запросов должно быть разрешено
            Assert.Equal(3, rejectedCount); // Остальные должны быть отклонены

            // Проверяем консистентность состояния - последний разрешенный запрос должен показывать 0 слотов
            var allowedResults = results.Where(r => r.isAllowed).ToList();
            Assert.Equal(0, allowedResults.Last().tokensLeft);

            // Все отклоненные запросы должны показывать 0 слотов
            var rejectedResults = results.Where(r => !r.isAllowed).ToList();
            Assert.All(rejectedResults, result => Assert.Equal(0, result.tokensLeft));
        }
    }

    [Fact]
    public async Task IsRequestAllowedAsync_DifferentResourceIds_ShouldMaintainIndependentState()
    {
        // Arrange - тест для работы с различными resourceId
        var baseResourceId = $"leaky_bucket_multi_resource_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 2, RefillRatePerSecond = 1 };
        var resourceCount = 5;
        var requestsPerResource = 3; // Больше чем capacity для проверки изоляции

        var allTasks = new List<Task<(string resourceId, bool isAllowed, int tokensLeft)>>();

        // Act - создаем одновременные запросы к разным ресурсам
        for (int resourceIndex = 0; resourceIndex < resourceCount; resourceIndex++)
        {
            var resourceId = $"{baseResourceId}_{resourceIndex}";
            
            for (int requestIndex = 0; requestIndex < requestsPerResource; requestIndex++)
            {
                allTasks.Add(
                    _strategy.IsRequestAllowedAsync(resourceId, policy)
                        .ContinueWith(t => (resourceId, t.Result.isAllowed, t.Result.tokensLeft))
                );
            }
        }

        var allResults = await Task.WhenAll(allTasks);

        // Assert - группируем результаты по ресурсам и проверяем изоляцию
        var resultsByResource = allResults.GroupBy(r => r.resourceId).ToList();

        Assert.Equal(resourceCount, resultsByResource.Count());

        foreach (var resourceGroup in resultsByResource)
        {
            var resourceResults = resourceGroup.ToList();
            
            // Для каждого ресурса должно быть ровно capacity разрешенных запросов
            var allowedForResource = resourceResults.Count(r => r.isAllowed);
            var rejectedForResource = resourceResults.Count(r => !r.isAllowed);

            Assert.Equal(policy.Capacity, allowedForResource);
            Assert.Equal(requestsPerResource - policy.Capacity, rejectedForResource);

            // Проверяем правильность подсчета слотов для каждого ресурса независимо
            var allowedResults = resourceResults.Where(r => r.isAllowed).OrderByDescending(r => r.tokensLeft).ToList();
            var expectedSlots = new[] { 1, 0 }; // Для capacity = 2

            for (int i = 0; i < allowedResults.Count; i++)
            {
                Assert.Equal(expectedSlots[i], allowedResults[i].tokensLeft);
            }
        }
    }

    [Fact]
    public async Task IsRequestAllowedAsync_ConsistencyBetweenRequests_ShouldMaintainStateIntegrity()
    {
        // Arrange - тест для проверки консистентности состояния между запросами
        var resourceId = $"leaky_bucket_consistency_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 4, RefillRatePerSecond = 2 };
        var testIterations = 10;

        for (int iteration = 0; iteration < testIterations; iteration++)
        {
            var iterationResourceId = $"{resourceId}_{iteration}";
            
            // Act - последовательно заполняем ведро и проверяем консистентность
            var results = new List<(bool isAllowed, int tokensLeft)>();

            // Заполняем ведро до максимума
            for (int i = 0; i < policy.Capacity; i++)
            {
                var result = await _strategy.IsRequestAllowedAsync(iterationResourceId, policy);
                results.Add(result);

                // Assert - каждый запрос до достижения емкости должен быть разрешен
                Assert.True(result.isAllowed);
                Assert.Equal(policy.Capacity - i - 1, result.tokensLeft);
            }

            // Проверяем переполнение
            var overflowResult = await _strategy.IsRequestAllowedAsync(iterationResourceId, policy);
            Assert.False(overflowResult.isAllowed);
            Assert.Equal(0, overflowResult.tokensLeft);

            // Act - ждем частичной утечки
            await Task.Delay(600); // 0.6 секунды, должен утечь ~1 запрос при скорости 2/сек

            // Проверяем состояние после утечки
            var afterLeakResult = await _strategy.IsRequestAllowedAsync(iterationResourceId, policy);
            Assert.True(afterLeakResult.isAllowed);
            Assert.Equal(0,afterLeakResult.tokensLeft);


            // Act - добавляем еще запросы для проверки консистентности
            await Task.Delay(1600); // 1.6 секунды, должен утечь ~3 запрос при скорости 2/сек
            var additionalResult1 = await _strategy.IsRequestAllowedAsync(iterationResourceId, policy);
            var additionalResult2 = await _strategy.IsRequestAllowedAsync(iterationResourceId, policy);

            // Assert - состояние должно обновляться консистентно
            Assert.True(additionalResult1.isAllowed);
            Assert.Equal(additionalResult2.tokensLeft, Math.Max(0, additionalResult1.tokensLeft - 1));
        }
    }

    [Fact]
    public async Task IsRequestAllowedAsync_DistributedEnvironmentSimulation_ShouldMaintainConsistency()
    {
        // Arrange
        var resourceId = $"leaky_bucket_distributed_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 6, RefillRatePerSecond = 2 };

        // Несколько "экземпляров" стратегии
        var strategies = new List<LeakyBucketStrategy>();
        for (int i = 0; i < 3; i++)
        {
            var redis = ConnectionMultiplexer.Connect("localhost:6379");
            var db = redis.GetDatabase();
            var redisRepository = new RedisRepository(db);
            strategies.Add(new LeakyBucketStrategy(redisRepository));
        }

        // Act – одновременно шлём 12 запросов (3 стратегии × 4 запроса)
        var allTasks = strategies
            .SelectMany((s, strategyIndex) =>
                Enumerable.Range(0, 4).Select(async _ =>
                {
                    var result = await s.IsRequestAllowedAsync(resourceId, policy);
                    return (strategyIndex, result.isAllowed, result.tokensLeft);
                })
            )
            .ToList();

        var allResults = await Task.WhenAll(allTasks);

        // Assert – глобальная атомарность
        var allowedResults = allResults.Where(r => r.isAllowed).ToList();
        var rejectedResults = allResults.Where(r => !r.isAllowed).ToList();

        Assert.Equal(policy.Capacity, allowedResults.Count);
        Assert.Equal(allTasks.Count - policy.Capacity, rejectedResults.Count);

        // Разрешённые пришли хотя бы от двух разных стратегий
        var strategiesUsed = allowedResults.Select(r => r.strategyIndex).Distinct().ToList();
        Assert.True(strategiesUsed.Count > 1, "Запросы должны обрабатываться разными экземплярами стратегии");

        // Все отклонённые должны показывать 0
        Assert.All(rejectedResults, r => Assert.Equal(0, r.tokensLeft));

        // Act – ждём утечки
        await Task.Delay(1100);

        var afterLeakTasks = strategies.Select(s => s.IsRequestAllowedAsync(resourceId, policy)).ToList();
        var afterLeakResults = await Task.WhenAll(afterLeakTasks);

        // Все запросы после утечки должны быть разрешены (т.к. освободилось место)
        Assert.All(afterLeakResults, r => Assert.True(r.isAllowed));
    }

    [Fact]
    public async Task IsRequestAllowedAsync_HighConcurrencyStressTest_ShouldMaintainDataIntegrity()
    {
        // Arrange - стресс-тест с высокой нагрузкой для проверки целостности данных
        var resourceId = $"leaky_bucket_stress_test_{Guid.NewGuid()}";
        var policy = new RateLimitPolicySettings { Capacity = 10, RefillRatePerSecond = 5 };
        var concurrentRequestCount = 50;
        var testRounds = 3;

        for (int round = 0; round < testRounds; round++)
        {
            var roundResourceId = $"{resourceId}_round_{round}";
            var tasks = new List<Task<(bool isAllowed, int tokensLeft)>>();

            // Act - запускаем большое количество одновременных запросов
            for (int i = 0; i < concurrentRequestCount; i++)
            {
                tasks.Add(_strategy.IsRequestAllowedAsync(roundResourceId, policy));
            }

            var results = await Task.WhenAll(tasks);

            // Assert - проверяем целостность данных при высокой нагрузке
            var allowedCount = results.Count(r => r.isAllowed);
            var rejectedCount = results.Count(r => !r.isAllowed);

            // Должно быть разрешено не больше чем capacity запросов
            Assert.True(allowedCount <= policy.Capacity);
            Assert.Equal(concurrentRequestCount - allowedCount, rejectedCount);

            // Проверяем корректность подсчета слотов
            var allowedResults = results.Where(r => r.isAllowed).OrderByDescending(r => r.tokensLeft).ToList();
            
            // Слоты должны уменьшаться последовательно
            for (int i = 0; i < allowedResults.Count - 1; i++)
            {
                Assert.True(allowedResults[i].tokensLeft >= allowedResults[i + 1].tokensLeft);
            }

            // Последний разрешенный запрос должен показывать корректное количество оставшихся слотов
            if (allowedResults.Any())
            {
                var lastAllowed = allowedResults.Last();
                Assert.Equal(policy.Capacity - allowedCount, lastAllowed.tokensLeft);
            }

            // Все отклоненные запросы должны показывать 0 слотов
            var rejectedResults = results.Where(r => !r.isAllowed).ToList();
            Assert.All(rejectedResults, result => Assert.Equal(0, result.tokensLeft));

            // Небольшая пауза между раундами для утечки
            if (round < testRounds - 1)
            {
                await Task.Delay(500);
            }
        }
    }
}