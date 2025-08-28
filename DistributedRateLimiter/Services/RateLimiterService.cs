using DistributedRateLimiter.Configuration;
using DistributedRateLimiter.Strategies;
using Microsoft.Extensions.Options;

namespace DistributedRateLimiter.Services;

public class RateLimiterService
{
    private readonly IReadOnlyDictionary<string, IRateLimitingStrategy> _strategies;
    private readonly RateLimiterSettings _settings;
    
    public RateLimiterService(IOptions<RateLimiterSettings> options, IEnumerable<IRateLimitingStrategy> strategies)
    {
        _settings = options.Value;
        _strategies = strategies.ToDictionary(s => s.Name, s => s);
    }

    public Task<(bool isAllowed, int tokensLeft)> IsRequestAllowedAsync(string resourceId)
    {
        var policy = _settings.DefaultPolicy;
        var strategyName = "TokenBucket";
        if (!_strategies.TryGetValue(strategyName, out var strategy))
        {
            throw new InvalidOperationException($"Rate limiting strategy '{strategyName}' is not registered.");
        }

        return strategy.IsRequestAllowedAsync(resourceId, policy);
    }
}
