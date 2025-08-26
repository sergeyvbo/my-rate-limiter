namespace DistributedRateLimiter.Models;

public class RateLimitCheckResponse
{
    public bool IsAllowed { get; set; }
    public int TokensLeft { get; set; }
}