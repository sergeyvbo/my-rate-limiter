FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["DistributedRateLimiter/DistributedRateLimiter.csproj", "DistributedRateLimiter/"]
RUN dotnet restore "DistributedRateLimiter/DistributedRateLimiter.csproj"

COPY . .
WORKDIR "/src/DistributedRateLimiter"
RUN dotnet build "DistributedRateLimiter.csproj" -c Release -o /app/build

FROM build as publish
RUN dotnet publish "DistributedRateLimiter.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 8080
EXPOSE 8081

ENTRYPOINT [ "dotnet", "DistributedRateLimiter.dll" ]