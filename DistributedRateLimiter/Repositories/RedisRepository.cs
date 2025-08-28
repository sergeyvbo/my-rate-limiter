using StackExchange.Redis;

namespace DistributedRateLimiter.Repositories;

public class RedisRepository
{
    private readonly IDatabase _db;

    

    public RedisRepository(IDatabase db)
    {
        _db = db;
    }

    public async Task<RedisResult[]> ExecuteScriptAsync(
        string script,
        string resourceId, 
        params object[] args)
    {
        var key = $"ratelimit:{resourceId}";
        RedisKey[] redisKeys = [key];
        RedisValue[] redisValues = args.Select(a =>(RedisValue)a.ToString()).ToArray();

        var result = (RedisResult[])await _db.ScriptEvaluateAsync(
            script,
            redisKeys,
            redisValues);

        return (result);

    }
}