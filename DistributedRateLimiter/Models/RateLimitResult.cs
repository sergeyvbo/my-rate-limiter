public class RateLimitResult
{
    public bool IsAllowed { get; }
    public int TokensLeft { get; }

    public RateLimitResult(bool isAllowed, int tokensLeft)
    {
        IsAllowed = isAllowed;
        TokensLeft = tokensLeft;
    }
}