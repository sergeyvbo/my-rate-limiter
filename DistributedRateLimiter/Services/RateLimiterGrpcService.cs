using Grpc.Core;
using Ratelimiter;
using StackExchange.Redis;

namespace DistributedRateLimiter.Services;

public class RateLimiterGrpcService : RateLimiter.RateLimiterBase
{
    private readonly RateLimiterService _rateLimiterService;
    private readonly ILogger<RateLimiterGrpcService> _logger;

    public RateLimiterGrpcService(RateLimiterService rateLimiterService, ILogger<RateLimiterGrpcService> logger)
    {
        _rateLimiterService = rateLimiterService;
        _logger = logger;
    }

    public override async Task<RateLimitResponse> Check(RateLimitRequest request, ServerCallContext context)
    {
        try
        {
            var (isAllowed, tokensLeft) = await _rateLimiterService.IsRequestAllowedAsync(request.ResourceId);

            return new RateLimitResponse
            {
                IsAllowed = isAllowed,
                TokensLeft = tokensLeft
            };
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Error communicating with Redis for resource: {ResourceId}", request.ResourceId);

            throw new RpcException(new Status(StatusCode.Unavailable, "The rate limiting service is temporarily unavailable. Please try again later."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred for resource: {ResourceId}", request.ResourceId);
            throw new RpcException(new Status(StatusCode.Internal, "An internal error occurred."));
        }
    }
}