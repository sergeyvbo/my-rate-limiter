namespace DistributedRateLimiter.Configuration;

public class RateLimiterSettings
{
    public RateLimitPolicySettings DefaultPolicy { get; set; }
}

public class RateLimitPolicySettings
{
    public int Capacity { get; set; }
    public int RefillRatePerSecond { get; set; }
}
