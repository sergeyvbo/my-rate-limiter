using DistributedRateLimiter.Configuration;
using DistributedRateLimiter.Repositories;
using Microsoft.Extensions.Options;

namespace DistributedRateLimiter.Services;

public class RateLimiterService
{
    private readonly RedisRepository _redisRepository;
    private readonly RateLimitPolicySettings _defaultPolicy;

    public RateLimiterService(RedisRepository redisRepository, IOptions<RateLimiterSettings> options)
    {
        _redisRepository = redisRepository;
        _defaultPolicy = options.Value.DefaultPolicy;
    }

    public Task<(bool isAllowed, int tokensLeft)> IsRequestAllowedAsync(string resourceId)
    {
        return _redisRepository.ApplyTokenBucketLogic(resourceId, _defaultPolicy.Capacity, _defaultPolicy.RefillRatePerSecond);
    }
}
