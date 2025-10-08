FROM python:3.11-slim

LABEL maintainer="NATS Team"
LABEL description="NATS Subscriber with structured JSON logging"

# Set working directory
WORKDIR /app

# Install dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy application
COPY subscriber.py .

# Make script executable
RUN chmod +x subscriber.py

# Create non-root user
RUN useradd -m -u 1000 subscriber && \
    chown -R subscriber:subscriber /app

USER subscriber

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD python -c "import sys; sys.exit(0)"

# Run subscriber
CMD ["python", "-u", "subscriber.py"]
