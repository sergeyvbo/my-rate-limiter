using StackExchange.Redis;

namespace DistributedRateLimiter.Repositories;

public class RedisRepository
{
    private readonly IDatabase _db;

    

    public RedisRepository(IDatabase db)
    {
        _db = db;
    }

    public async Task<(bool isAllowed, int tokensLeft)> ExecuteScriptAsync(
        string script,
        string resourceId, 
        int capacity, 
        int refillRatePerSecond)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var requestedTokens = 1;
        var key = $"ratelimit:{resourceId}";

        var result = (RedisResult[])await _db.ScriptEvaluateAsync(
            script,
            [key],
            [capacity, refillRatePerSecond, now, requestedTokens]);

        var isAllowed = (bool)result[0];
        var tokensLeft = (int)result[1];

        return (isAllowed, tokensLeft);

    }
}