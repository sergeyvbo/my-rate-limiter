using DistributedRateLimiter.Repositories;

namespace DistributedRateLimiter.Services;

public class RateLimiterService
{
    private readonly RedisRepository _redisRepository;
    private readonly int _capacity = 100; // Временно, потом перенесем в конфиг
    private readonly int _refillRate = 10; // Временно, потом перенесем в конфиг

    public RateLimiterService(RedisRepository redisRepository)
    {
        _redisRepository = redisRepository;
    }

    public Task<(bool isAllowed, int tokensLeft)> IsRequestAllowedAsync(string resourceId)
    {
        return _redisRepository.ApplyTokenBucketLogic(resourceId, _capacity, _refillRate);
    }
}
