#!/usr/bin/env python3
"""
NATS Publisher - Publishes messages to NATS subjects with structured JSON logging
"""

import asyncio
import json
import logging
import os
import random
import sys
import time
from datetime import datetime, timezone
from typing import Dict, Any

import nats
from nats.errors import ConnectionClosedError, TimeoutError, NoServersError


class JSONFormatter(logging.Formatter):
    """Custom JSON formatter for structured logging to Loki"""

    def format(self, record: logging.LogRecord) -> str:
        log_data = {
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
            "module": record.module,
            "function": record.funcName,
            "line": record.lineno,
        }

        # Add exception info if present
        if record.exc_info:
            log_data["exception"] = self.formatException(record.exc_info)

        # Add extra fields if present
        if hasattr(record, "extra_fields"):
            log_data.update(record.extra_fields)

        return json.dumps(log_data)


def setup_logging() -> logging.Logger:
    """Configure structured JSON logging"""
    logger = logging.getLogger("nats-publisher")
    logger.setLevel(logging.INFO)

    # Console handler with JSON formatting
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(JSONFormatter())
    logger.addHandler(handler)

    return logger


class NATSPublisher:
    """NATS Publisher that sends messages with metrics tracking"""

    def __init__(
        self,
        nats_url: str,
        subject: str,
        hostname: str,
        publish_interval: float = 2.0,
    ):
        self.nats_url = nats_url
        self.subject = subject
        self.hostname = hostname
        self.publish_interval = publish_interval
        self.nc = None
        self.logger = setup_logging()
        self.message_count = 0
        self.error_count = 0
        self.start_time = time.time()

    async def connect(self) -> None:
        """Connect to NATS server with retry logic"""
        self.logger.info(
            "Connecting to NATS",
            extra={
                "extra_fields": {
                    "nats_url": self.nats_url,
                    "subject": self.subject,
                    "hostname": self.hostname,
                }
            },
        )

        try:
            self.nc = await nats.connect(
                servers=[self.nats_url],
                name=f"publisher-{self.hostname}",
                reconnect_time_wait=2,
                max_reconnect_attempts=60,
                ping_interval=20,
                max_outstanding_pings=3,
            )

            self.logger.info(
                "Connected to NATS successfully",
                extra={
                    "extra_fields": {
                        "server_info": str(self.nc.connected_server_version),
                        "client_id": self.nc.client_id,
                    }
                },
            )

        except NoServersError as e:
            self.logger.error(
                "No NATS servers available",
                extra={"extra_fields": {"error": str(e)}},
            )
            raise
        except Exception as e:
            self.logger.error(
                "Failed to connect to NATS",
                extra={"extra_fields": {"error": str(e), "error_type": type(e).__name__}},
            )
            raise

    async def publish_message(self) -> None:
        """Publish a single message with metadata"""
        self.message_count += 1

        # Generate message payload
        message_data = {
            "message_id": f"{self.hostname}-{self.message_count}",
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "source": self.hostname,
            "sequence": self.message_count,
            "data": {
                "event_type": random.choice(
                    ["user.login", "user.logout", "order.created", "payment.processed"]
                ),
                "value": random.randint(1, 1000),
                "random_field": random.choice(["alpha", "beta", "gamma", "delta"]),
            },
        }

        payload = json.dumps(message_data).encode()

        try:
            # Publish with timeout
            await asyncio.wait_for(
                self.nc.publish(self.subject, payload), timeout=5.0
            )

            self.logger.info(
                "Message published",
                extra={
                    "extra_fields": {
                        "message_id": message_data["message_id"],
                        "subject": self.subject,
                        "size_bytes": len(payload),
                        "sequence": self.message_count,
                        "event_type": message_data["data"]["event_type"],
                    }
                },
            )

        except TimeoutError:
            self.error_count += 1
            self.logger.error(
                "Publish timeout",
                extra={
                    "extra_fields": {
                        "message_id": message_data["message_id"],
                        "subject": self.subject,
                    }
                },
            )
        except ConnectionClosedError:
            self.error_count += 1
            self.logger.error("NATS connection closed, attempting reconnect")
            raise
        except Exception as e:
            self.error_count += 1
            self.logger.error(
                "Publish error",
                extra={
                    "extra_fields": {
                        "error": str(e),
                        "error_type": type(e).__name__,
                        "message_id": message_data["message_id"],
                    }
                },
            )

    async def publish_loop(self) -> None:
        """Main publish loop"""
        self.logger.info(
            "Starting publish loop",
            extra={
                "extra_fields": {
                    "interval_seconds": self.publish_interval,
                    "subject": self.subject,
                }
            },
        )

        while True:
            try:
                await self.publish_message()
                await asyncio.sleep(self.publish_interval)

                # Log metrics every 50 messages
                if self.message_count % 50 == 0:
                    self.log_metrics()

            except ConnectionClosedError:
                self.logger.warning("Connection lost, waiting for reconnect...")
                await asyncio.sleep(5)
            except Exception as e:
                self.logger.error(
                    "Unexpected error in publish loop",
                    extra={"extra_fields": {"error": str(e)}},
                )
                await asyncio.sleep(1)

    def log_metrics(self) -> None:
        """Log current metrics"""
        uptime = time.time() - self.start_time
        messages_per_sec = self.message_count / uptime if uptime > 0 else 0

        self.logger.info(
            "Publisher metrics",
            extra={
                "extra_fields": {
                    "total_messages": self.message_count,
                    "total_errors": self.error_count,
                    "uptime_seconds": round(uptime, 2),
                    "messages_per_second": round(messages_per_sec, 2),
                    "error_rate": round(
                        (self.error_count / self.message_count * 100)
                        if self.message_count > 0
                        else 0,
                        2,
                    ),
                }
            },
        )

    async def run(self) -> None:
        """Main run method"""
        await self.connect()

        try:
            await self.publish_loop()
        except KeyboardInterrupt:
            self.logger.info("Shutting down publisher...")
        finally:
            await self.close()

    async def close(self) -> None:
        """Close NATS connection"""
        if self.nc:
            self.log_metrics()
            await self.nc.drain()
            await self.nc.close()
            self.logger.info("Publisher shutdown complete")


async def main():
    """Main entry point"""
    # Configuration from environment variables
    nats_url = os.getenv("NATS_URL", "nats://localhost:4222")
    subject = os.getenv("NATS_SUBJECT", "events.test")
    hostname = os.getenv("HOSTNAME", "publisher")
    interval = float(os.getenv("PUBLISH_INTERVAL", "2.0"))

    publisher = NATSPublisher(
        nats_url=nats_url,
        subject=subject,
        hostname=hostname,
        publish_interval=interval,
    )

    await publisher.run()


if __name__ == "__main__":
    asyncio.run(main())
