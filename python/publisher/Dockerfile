FROM python:3.11-slim

LABEL maintainer="NATS Team"
LABEL description="NATS Publisher with structured JSON logging"

# Set working directory
WORKDIR /app

# Install dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy application
COPY publisher.py .

# Make script executable
RUN chmod +x publisher.py

# Create non-root user
RUN useradd -m -u 1000 publisher && \
    chown -R publisher:publisher /app

USER publisher

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD python -c "import sys; sys.exit(0)"

# Run publisher
CMD ["python", "-u", "publisher.py"]
