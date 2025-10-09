# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY PaymentPublisher.csproj .
RUN dotnet restore

# Copy source and build
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine
WORKDIR /app

# Create non-root user
RUN adduser -D -u 1000 payment

# Copy published application
COPY --from=build /app/publish .

# Set ownership
RUN chown -R payment:payment /app

USER payment

# Environment variables (can be overridden)
ENV NATS_URL=nats://localhost:4222
ENV HOSTNAME=payment-publisher
ENV PUBLISH_INTERVAL=5.0

ENTRYPOINT ["dotnet", "PaymentPublisher.dll"]
