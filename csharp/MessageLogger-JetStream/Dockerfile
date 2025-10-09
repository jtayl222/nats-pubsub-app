# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY MessageLogger.csproj .
RUN dotnet restore

# Copy source and build
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine
WORKDIR /app

# Create non-root user
RUN adduser -D -u 1000 logger

# Copy published application
COPY --from=build /app/publish .

# Set ownership
RUN chown -R logger:logger /app

USER logger

# Environment variables (can be overridden)
ENV NATS_URL=nats://localhost:4222
ENV NATS_SUBJECT=">>"

ENTRYPOINT ["dotnet", "MessageLogger-JetStream.dll"]
