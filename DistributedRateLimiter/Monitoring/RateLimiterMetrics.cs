using Prometheus;

namespace DistributedRateLimiter.Monitoring;

public class RateLimiterMetrics
{
    private readonly Counter _requestsProcessed = Metrics.CreateCounter(
        "ratelimiter_requests_processed_total",
        "Total number of requests processed by the rate limiter.",
        new CounterConfiguration
        {
            LabelNames = new[] { "decision" }
        });

    public void IncrementRequestsAllowed()
    {
        _requestsProcessed.WithLabels("allowed").Inc();
    }

    public void IncrementRequestsDenied()
    {
        _requestsProcessed.WithLabels("denied").Inc();
    }
}