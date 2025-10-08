# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY Publisher.csproj .
RUN dotnet restore

# Copy source code and build
COPY Program.cs .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine
WORKDIR /app

# Create non-root user
RUN adduser -D -u 1000 publisher

# Copy published application
COPY --from=build /app/publish .

# Set ownership
RUN chown -R publisher:publisher /app

USER publisher

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD pgrep -f dotnet || exit 1

ENTRYPOINT ["dotnet", "Publisher.dll"]
