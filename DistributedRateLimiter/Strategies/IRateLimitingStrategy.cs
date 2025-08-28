using DistributedRateLimiter.Configuration;

namespace DistributedRateLimiter.Strategies;

public interface IRateLimitingStrategy
{
    string Name { get; }

    Task<(bool isAllowed, int tokensLeft)> IsRequestAllowedAsync(string resourceId, RateLimitPolicySettings policy);
}
