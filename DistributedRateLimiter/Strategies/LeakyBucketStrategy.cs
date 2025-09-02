using DistributedRateLimiter.Configuration;
using DistributedRateLimiter.Repositories;

namespace DistributedRateLimiter.Strategies;

public class LeakyBucketStrategy : IRateLimitingStrategy
{
    private readonly RedisRepository _redisRepository;
    public string Name => "LeakyBucket";

    private const string LeakyBucketLuaScript = """
    
        -- KEYS[1] - ключ ресурса
        -- ARGV[1] - capacity
        -- ARGV[2] - leakRate
        -- ARGV[3] - now
        -- ARGV[4] - requested_tokens

        local bucket = redis.call('HGETALL', KEYS[1])

        local capacity = tonumber(ARGV[1])
        local leak_rate = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])
        local requested = tonumber(ARGV[4])

        local tokens = tonumber(redis.call("HGET", KEYS[1], "tokens") or "0")
        local last_ts = tonumber(redis.call("HGET", KEYS[1], "last_leak_timestamp") or now)

        local elapsed_ms = now - last_ts
        local elapsed = elapsed_ms / 1000.0
        if elapsed > 0 then
            local leaked = elapsed * leak_rate
            tokens = math.max(0, tokens - leaked)
            last_ts = now
        end

        local allowed
        local tokens_left

        if tokens + requested <= capacity then
            tokens = tokens + requested
            allowed = 1
            tokens_left = capacity - tokens
            redis.call("HSET", KEYS[1], "tokens", tokens, "last_leak_timestamp", last_ts)
        else
            allowed = 0
            tokens_left = capacity - tokens
        end

        return {allowed, tokens_left}

        """;

    public LeakyBucketStrategy(RedisRepository redisRepository)
    {
        _redisRepository = redisRepository;
    }

    public async Task<(bool isAllowed, int tokensLeft)> IsRequestAllowedAsync(string resourceId, RateLimitPolicySettings policy)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var requestedTokens = 1;

        var result = await _redisRepository.ExecuteScriptAsync(
            LeakyBucketLuaScript,
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