using DistributedRateLimiter.Models;
using Grpc.Core;
using Ratelimiter;

namespace DistributedRateLimiter.Services;

public class RateLimiterGrpcService : RateLimiter.RateLimiterBase
{
    private readonly RateLimiterService _rateLimiterService;

    public RateLimiterGrpcService(RateLimiterService rateLimiterService)
    {
        _rateLimiterService = rateLimiterService;
    }

    public override async Task<RateLimitResponse> Check(RateLimitRequest request, ServerCallContext context)
    {
        var (isAllowed, tokensLeft) = await _rateLimiterService.IsRequestAllowedAsync(request.ResourceId);

        return new RateLimitResponse
        {
            IsAllowed = isAllowed,
            TokensLeft = tokensLeft
        };
    }
}