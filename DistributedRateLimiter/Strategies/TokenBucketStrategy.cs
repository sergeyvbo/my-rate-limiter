namespace DistributedRateLimiter.Strategies;

public class TokenBucketStrategy(int capacity, int refillRate)
{
    public Dictionary<string, (int Tokens, DateTime LastAccessTime)> Buckets { get; } = [];

    public int Capacity { get; set; } = capacity;
    public int RefillRate { get; } = refillRate;

    public RateLimitResult IsRequestAllowed(string resourceId)
    {
        var requestTime = DateTime.UtcNow;
        var bucket = Buckets.GetValueOrDefault(resourceId, (Tokens: Capacity, LastAccessTime: DateTime.UtcNow));
        var tokensLeft = bucket.Tokens;

        var timePassed = requestTime - bucket.LastAccessTime;
        var secondsPassed = (int)timePassed.TotalSeconds;
        tokensLeft = Math.Min(Capacity, tokensLeft + RefillRate * secondsPassed);

        if (tokensLeft <= 0)
        {
            return new RateLimitResult(false, tokensLeft);
        }

        tokensLeft--;
        Buckets[resourceId] = (Tokens: tokensLeft, LastAccessTime: DateTime.UtcNow);
        return new RateLimitResult(true, tokensLeft);
    }
}