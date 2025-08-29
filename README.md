# Distributed gRPC Rate Limiter

This project is a case study of a distributed rate limiter for LLD and HLD system interview prep. It was created using an LLM as a guide and author of test (for TDD approach).

## Key Features

-   **High Performance**: Utilizes gRPC (HTTP/2 + Protobuf) for low-latency inter-service communication.
-   **Distributed & Scalable**: Stores state centrally in Redis, allowing the service to be scaled horizontally without losing consistency.
-   **Atomicity**: All rate-limiting logic is encapsulated in a Lua script, which executes atomically on the Redis server, ensuring thread safety and preventing race conditions.
-   **Extensibility**: Built on the Strategy Pattern, making it easy to add new rate-limiting algorithms (e.g., Sliding Window, Leaky Bucket) in the future.
-   **Observability**: Integrated with Prometheus for metrics collection and Grafana for visualization, enabling real-time system performance monitoring.
-   **Deployment Ready**: The entire stack (service, Redis, Prometheus, Grafana) is containerized using Docker Compose for easy, reproducible deployments.

## System Architecture

The system consists of the main service, a Redis database for state storage, and a monitoring stack. Clients interact with the service via the gRPC protocol.

```ascii
                               +--------------------------+
                               |   Monitoring Dashboard   |
                               |        (Grafana)         |
                               +-------------^------------+
                                             | (Queries)
+----------------+             +-------------+------------+
|                |  (gRPC)     |                          |
|  Client App    +------------->  Rate Limiter Service    |
| (Node, .NET...)|             |     (ASP.NET Core)       |
|                |             |                          |
+----------------+             +-------------+------------+
                               |             |
                      (Metrics)|             | (Lua Script)
                               v             v
                 +-------------+------------+---------------+
                 |      Prometheus          |     Redis     |
                 +--------------------------+---------------+
```

## Tech Stack

-   **Service**: C# / ASP.NET Core 8
-   **Protocol**: gRPC (Protobuf)
-   **Database**: Redis (for storing counters)
-   **Containerization**: Docker, Docker Compose
-   **Monitoring**: Prometheus, Grafana
-   **Load Testing**: ghz

## Getting Started

### Prerequisites

-   [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
-   [Docker Desktop](https://www.docker.com/products/docker-desktop/)

### Instructions

1.  **Clone the repository:**
    ```bash
    git clone https://github.com/sergeyvbo/my-rate-limiter.git
    cd my-rate-limiter
    ```

2.  **Run the entire stack using Docker Compose:**
    Open a terminal in the project's root folder and run the command:
    ```bash
    docker-compose up --build
    ```
    This command will build the .NET application's image and start all four containers: `rate-limiter`, `redis`, `prometheus`, and `grafana`.

3.  **Verify that the services are available:**
    -   **Rate Limiter (gRPC)**: `localhost:8080`
    -   **Rate Limiter (Metrics)**: `http://localhost:8081/metrics`
    -   **Prometheus**: `http://localhost:9090`
    -   **Grafana**: `http://localhost:3000` (login/password: `admin`/`admin`)

## Using the Service (Client Integration)

To interact with the service, any client will need the `ratelimiter.proto` contract file.

### 1. ASP.NET Core Client

**1. Install the required NuGet packages:**
```bash
dotnet add package Grpc.Net.Client
dotnet add package Google.Protobuf
dotnet add package Grpc.Tools
```

**2. Configure your project file (`.csproj`) to generate the client code:**
Copy the `ratelimiter.proto` file into your client project (e.g., into a `Protos` folder) and add the following section to your `.csproj`:
```xml
<ItemGroup>
  <Protobuf Include="Protos\ratelimiter.proto" GrpcServices="Client" />
</ItemGroup>
```

**3. C# Client Example:**
```csharp
using Grpc.Net.Client;
using Ratelimiter; // The namespace from your .proto file

// Create a channel to connect to the service
using var channel = GrpcChannel.ForAddress("http://localhost:8080");

// Create the gRPC client
var client = new RateLimiter.RateLimiterClient(channel);

// Build the request
var request = new RateLimitRequest
{
    ResourceId = "user:12345"
};

try
{
    // Make the asynchronous call
    var response = await client.CheckAsync(request);

    if (response.IsAllowed)
    {
        Console.WriteLine($"Request allowed! Tokens left: {response.TokensLeft}");
        // ... proceed with the main logic ...
    }
    else
    {
        Console.WriteLine("Request denied! Rate limit exceeded.");
    }
}
catch (Grpc.Core.RpcException ex)
{
    Console.WriteLine($"gRPC Error: {ex.Status.Detail}");
}
```

### 2. Node.js Client

**1. Install the required npm packages:**
```bash
npm install @grpc/grpc-js @grpc/proto-loader
```

**2. JavaScript Client Example:**
Copy the `ratelimiter.proto` file into your Node.js project.

```javascript
const grpc = require('@grpc/grpc-js');
const protoLoader = require('@grpc/proto-loader');

const PROTO_PATH = './ratelimiter.proto'; // Path to your .proto file

// Load the .proto file
const packageDefinition = protoLoader.loadSync(PROTO_PATH, {
    keepCase: true,
    longs: String,
    enums: String,
    defaults: true,
    oneofs: true
});

// Load the package definition
const ratelimiterProto = grpc.loadPackageDefinition(packageDefinition).ratelimiter;

// Create the client
const client = new ratelimiterProto.RateLimiter(
    'localhost:8080',
    grpc.credentials.createInsecure()
);

// Build the request
const request = {
    resource_id: 'user:nodejs-client'
};

// Make the call
client.Check(request, (error, response) => {
    if (error) {
        console.error('gRPC Error:', error.details);
        return;
    }

    if (response.is_allowed) {
        console.log(`Request allowed! Tokens left: ${response.tokens_left}`);
        // ... proceed with the main logic ...
    } else {
        console.log('Request denied! Rate limit exceeded.');
    }
});
```

## Testing

### Running Unit & Integration Tests
You can run all the tests we created with a single command from the root folder:
```bash
dotnet test
```

### Load Testing
The `ghz` tool is used for load testing.

1.  **Install [ghz](https://ghz.sh/docs/install)**.
2.  **Create a `payload.json` file** with the request body:
    ```json
    {
      "resource_id": "load_test_user_1"
    }
    ```
3.  **Run the test** (ensure the path to the `.proto` file is correct):
    ```bash
    ghz --insecure \
        --proto ./DistributedRateLimiter/Protos/ratelimiter.proto \
        --call ratelimiter.RateLimiter.Check \
        -D ./payload.json \
        -c 100 \
        -n 20000 \
        localhost:8080
    ```
    This command will send 20,000 requests with a concurrency of 100.

## Monitoring

During the load test, you can observe the system's state in real-time.

1.  **Prometheus**: Open `http://localhost:9090`.
    -   Navigate to **Status -> Targets** to ensure Prometheus is successfully scraping metrics from your service.
    -   On the **Graph** tab, you can execute queries like `rate(ratelimiter_requests_processed_total[1m])`.

2.  **Grafana**: Open `http://localhost:3000` (login/password: `admin`/`admin`).
    -   **Add Data Source**: `Configuration -> Data Sources -> Add -> Prometheus`. Set the URL to `http://prometheus:9090`.
    -   **Create a Dashboard**: `Dashboards -> New -> Add visualization` and use queries from Prometheus to build your graphs.
