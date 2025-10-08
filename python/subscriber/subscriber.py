#!/usr/bin/env python3
"""
NATS Subscriber - Subscribes to NATS subjects and processes messages with structured JSON logging
"""

import asyncio
import json
import logging
import os
import sys
import time
from datetime import datetime, timezone
from typing import Dict, Any

import nats
from nats.aio.msg import Msg
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
    logger = logging.getLogger("nats-subscriber")
    logger.setLevel(logging.INFO)

    # Console handler with JSON formatting
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(JSONFormatter())
    logger.addHandler(handler)

    return logger


class NATSSubscriber:
    """NATS Subscriber that receives and processes messages"""

    def __init__(
        self,
        nats_url: str,
        subject: str,
        hostname: str,
        queue_group: str = None,
    ):
        self.nats_url = nats_url
        self.subject = subject
        self.hostname = hostname
        self.queue_group = queue_group
        self.nc = None
        self.logger = setup_logging()
        self.message_count = 0
        self.error_count = 0
        self.start_time = time.time()
        self.last_message_time = None
        self.total_latency_ms = 0.0

    async def connect(self) -> None:
        """Connect to NATS server with retry logic"""
        self.logger.info(
            "Connecting to NATS",
            extra={
                "extra_fields": {
                    "nats_url": self.nats_url,
                    "subject": self.subject,
                    "hostname": self.hostname,
                    "queue_group": self.queue_group,
                }
            },
        )

        try:
            self.nc = await nats.connect(
                servers=[self.nats_url],
                name=f"subscriber-{self.hostname}",
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

    async def message_handler(self, msg: Msg) -> None:
        """Handle incoming messages"""
        receive_time = datetime.now(timezone.utc)
        self.message_count += 1
        self.last_message_time = receive_time

        try:
            # Parse message payload
            payload = json.loads(msg.data.decode())

            # Calculate latency
            message_timestamp = datetime.fromisoformat(payload.get("timestamp", receive_time.isoformat()))
            latency_ms = (receive_time - message_timestamp).total_seconds() * 1000
            self.total_latency_ms += latency_ms

            self.logger.info(
                "Message received",
                extra={
                    "extra_fields": {
                        "message_id": payload.get("message_id", "unknown"),
                        "subject": msg.subject,
                        "size_bytes": len(msg.data),
                        "source": payload.get("source", "unknown"),
                        "sequence": payload.get("sequence", 0),
                        "latency_ms": round(latency_ms, 2),
                        "event_type": payload.get("data", {}).get("event_type", "unknown"),
                        "reply_to": msg.reply,
                    }
                },
            )

            # Log metrics every 50 messages
            if self.message_count % 50 == 0:
                self.log_metrics()

        except json.JSONDecodeError as e:
            self.error_count += 1
            self.logger.error(
                "Failed to decode message JSON",
                extra={
                    "extra_fields": {
                        "error": str(e),
                        "subject": msg.subject,
                        "data_preview": msg.data[:100].decode(errors="ignore"),
                    }
                },
            )
        except Exception as e:
            self.error_count += 1
            self.logger.error(
                "Error processing message",
                extra={
                    "extra_fields": {
                        "error": str(e),
                        "error_type": type(e).__name__,
                        "subject": msg.subject,
                    }
                },
            )

    async def subscribe(self) -> None:
        """Subscribe to NATS subject"""
        self.logger.info(
            "Subscribing to subject",
            extra={
                "extra_fields": {
                    "subject": self.subject,
                    "queue_group": self.queue_group,
                }
            },
        )

        try:
            await self.nc.subscribe(
                self.subject,
                queue=self.queue_group,
                cb=self.message_handler,
            )

            self.logger.info(
                "Subscription active",
                extra={
                    "extra_fields": {
                        "subject": self.subject,
                        "queue_group": self.queue_group,
                    }
                },
            )

        except Exception as e:
            self.logger.error(
                "Failed to subscribe",
                extra={
                    "extra_fields": {
                        "error": str(e),
                        "subject": self.subject,
                    }
                },
            )
            raise

    def log_metrics(self) -> None:
        """Log current metrics"""
        uptime = time.time() - self.start_time
        messages_per_sec = self.message_count / uptime if uptime > 0 else 0
        avg_latency = (
            self.total_latency_ms / self.message_count if self.message_count > 0 else 0
        )

        self.logger.info(
            "Subscriber metrics",
            extra={
                "extra_fields": {
                    "total_messages": self.message_count,
                    "total_errors": self.error_count,
                    "uptime_seconds": round(uptime, 2),
                    "messages_per_second": round(messages_per_sec, 2),
                    "average_latency_ms": round(avg_latency, 2),
                    "error_rate": round(
                        (self.error_count / self.message_count * 100)
                        if self.message_count > 0
                        else 0,
                        2,
                    ),
                }
            },
        )

    async def metrics_loop(self) -> None:
        """Periodically log metrics"""
        while True:
            await asyncio.sleep(60)  # Log metrics every minute
            self.log_metrics()

    async def run(self) -> None:
        """Main run method"""
        await self.connect()
        await self.subscribe()

        # Start metrics logging task
        metrics_task = asyncio.create_task(self.metrics_loop())

        try:
            # Keep running
            while True:
                await asyncio.sleep(1)
        except KeyboardInterrupt:
            self.logger.info("Shutting down subscriber...")
        finally:
            metrics_task.cancel()
            await self.close()

    async def close(self) -> None:
        """Close NATS connection"""
        if self.nc:
            self.log_metrics()
            await self.nc.drain()
            await self.nc.close()
            self.logger.info("Subscriber shutdown complete")


async def main():
    """Main entry point"""
    # Configuration from environment variables
    nats_url = os.getenv("NATS_URL", "nats://localhost:4222")
    subject = os.getenv("NATS_SUBJECT", "events.test")
    hostname = os.getenv("HOSTNAME", "subscriber")
    queue_group = os.getenv("QUEUE_GROUP", None)

    subscriber = NATSSubscriber(
        nats_url=nats_url,
        subject=subject,
        hostname=hostname,
        queue_group=queue_group,
    )

    await subscriber.run()


if __name__ == "__main__":
    asyncio.run(main())
