using DistributedRateLimiter.Configuration;
using DistributedRateLimiter.Repositories;

namespace DistributedRateLimiter.Strategies;

public class TokenBucketStrategy : IRateLimitingStrategy
{
    private readonly RedisRepository _redisRepository;
    public string Name => "TokenBucket";

    private const string TokenBucketLuaScript = """
    
        -- KEYS[1] - ключ ресурса (например, "ratelimit:user:123")
        -- ARGV[1] - емкость ведра (capacity)
        -- ARGV[2] - скорость пополнения в секунду (refillRate)
        -- ARGV[3] - текущее время в Unix-секундах (now)
        -- ARGV[4] - количество запрашиваемых токенов (обычно 1)

        -- Получаем текущее состояние ведра
        local bucket = redis.call('HGETALL', KEYS[1])

        local capacity = tonumber(ARGV[1])
        local refill_rate = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])
        local requested_tokens = tonumber(ARGV[4])

        local tokens
        local last_refill_timestamp

        -- Если ведра еще не существует, создаем его
        if #bucket == 0 then
            tokens = capacity
            last_refill_timestamp = now
        else
            -- Redis возвращает массив [ключ1, значение1, ключ2, значение2]
            -- Нам нужно найти 'tokens' и 'last_refill_timestamp'
            for i = 1, #bucket, 2 do
                if bucket[i] == 'tokens' then
                    tokens = tonumber(bucket[i+1])
                elseif bucket[i] == 'last_refill_timestamp' then
                    last_refill_timestamp = tonumber(bucket[i+1])
                end
            end
        end

        -- Рассчитываем, сколько времени прошло и сколько токенов нужно добавить
        local time_passed = now - last_refill_timestamp
        if time_passed > 0 then
            local new_tokens = time_passed * refill_rate
            tokens = math.min(capacity, tokens + new_tokens)
            last_refill_timestamp = now
        end

        local is_allowed
        local tokens_left

        -- Проверяем, достаточно ли токенов
        if tokens >= requested_tokens then
            is_allowed = 1 -- true
            tokens_left = tokens - requested_tokens
            -- Обновляем состояние в Redis
            redis.call('HSET', KEYS[1], 'tokens', tokens_left, 'last_refill_timestamp', last_refill_timestamp)
        else
            is_allowed = 0 -- false
            tokens_left = tokens
        end

        -- Возвращаем результат: [is_allowed, tokens_left]
        return {is_allowed, tokens_left}

    """;

    public TokenBucketStrategy(RedisRepository redisRepository)
    {
        _redisRepository = redisRepository;
    }

    public async Task<(bool isAllowed, int tokensLeft)> IsRequestAllowedAsync(string resourceId, RateLimitPolicySettings policy)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var requestedTokens = 1;

        var result = await _redisRepository.ExecuteScriptAsync(
            TokenBucketLuaScript,
            resourceId,
            policy.Capacity,
            policy.RefillRatePerSecond,
            now,
            requestedTokens);

        var isAllowed = (bool)result[0];
        var tokensLeft = (int)result[1];

        return (isAllowed, tokensLeft);
    }
}
