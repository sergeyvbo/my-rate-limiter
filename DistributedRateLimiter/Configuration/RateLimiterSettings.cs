namespace DistributedRateLimiter.Configuration;

public class RateLimiterSettings
{
    public RateLimitPolicySettings DefaultPolicy { get; set; }
}

public class RateLimitPolicySettings
{
    public string Redis { get; set; }
    public int Capacity { get; set; }
    public int RefillRatePerSecond { get; set; }
}
