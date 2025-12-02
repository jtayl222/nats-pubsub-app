#!/usr/bin/env python3
"""
Example Python client for NatsHttpGateway protobuf endpoints.

Requirements:
    pip install protobuf requests

Usage:
    python protobuf_client_example.py
"""

import sys
import os
import uuid
from datetime import datetime
import requests

# Import generated protobuf classes
try:
    import message_pb2
except ImportError:
    print("Error: message_pb2 not found.")
    print("Generate it with: protoc --python_out=. ../Protos/message.proto")
    sys.exit(1)


class ProtobufClient:
    """Client for interacting with NatsHttpGateway protobuf endpoints"""

    def __init__(self, base_url="http://localhost:5000"):
        self.base_url = base_url
        self.session = requests.Session()

    def publish_generic_message(self):
        """Example 1: Publish a generic protobuf message"""
        print("=== Example 1: Publishing Generic Message ===")

        # Create a PublishMessage
        msg = message_pb2.PublishMessage()
        msg.message_id = str(uuid.uuid4())
        msg.subject = "events.test"
        msg.source = "python-client"
        msg.timestamp.GetCurrentTime()
        msg.data = b'{"message": "Hello from Python!"}'
        msg.metadata["client"] = "python"
        msg.metadata["version"] = "1.0"

        # Serialize to protobuf binary
        protobuf_bytes = msg.SerializeToString()
        print(f"Protobuf payload size: {len(protobuf_bytes)} bytes")

        # Send to gateway
        response = self.session.post(
            f"{self.base_url}/api/proto/ProtobufMessages/events.test",
            headers={"Content-Type": "application/x-protobuf"},
            data=protobuf_bytes
        )
        response.raise_for_status()

        # Parse response
        ack = message_pb2.PublishAck()
        ack.ParseFromString(response.content)

        print("✓ Published successfully!")
        print(f"  Stream: {ack.stream}")
        print(f"  Sequence: {ack.sequence}")
        print(f"  Subject: {ack.subject}")
        print()

    def publish_user_event(self):
        """Example 2: Publish a UserEvent"""
        print("=== Example 2: Publishing UserEvent ===")

        user_event = message_pb2.UserEvent()
        user_event.user_id = f"user-{uuid.uuid4().hex[:8]}"
        user_event.event_type = "created"
        user_event.email = "pythonuser@example.com"
        user_event.occurred_at.GetCurrentTime()
        user_event.attributes["plan"] = "premium"
        user_event.attributes["language"] = "python"

        response = self.session.post(
            f"{self.base_url}/api/proto/ProtobufMessages/events.user.created/user-event",
            headers={"Content-Type": "application/x-protobuf"},
            data=user_event.SerializeToString()
        )
        response.raise_for_status()

        ack = message_pb2.PublishAck()
        ack.ParseFromString(response.content)

        print("✓ UserEvent published!")
        print(f"  User ID: {user_event.user_id}")
        print(f"  Event Type: {user_event.event_type}")
        print(f"  Stream: {ack.stream}, Sequence: {ack.sequence}")
        print()

    def publish_payment_event(self):
        """Example 3: Publish a PaymentEvent"""
        print("=== Example 3: Publishing PaymentEvent ===")

        payment_event = message_pb2.PaymentEvent()
        payment_event.transaction_id = f"txn-{uuid.uuid4().hex}"
        payment_event.status = "approved"
        payment_event.amount = 99.99
        payment_event.currency = "USD"
        payment_event.card_last_four = "1234"
        payment_event.processed_at.GetCurrentTime()

        response = self.session.post(
            f"{self.base_url}/api/proto/ProtobufMessages/payments.credit_card.approved/payment-event",
            headers={"Content-Type": "application/x-protobuf"},
            data=payment_event.SerializeToString()
        )
        response.raise_for_status()

        ack = message_pb2.PublishAck()
        ack.ParseFromString(response.content)

        print("✓ PaymentEvent published!")
        print(f"  Transaction ID: {payment_event.transaction_id}")
        print(f"  Amount: ${payment_event.amount} {payment_event.currency}")
        print(f"  Status: {payment_event.status}")
        print(f"  Stream: {ack.stream}, Sequence: {ack.sequence}")
        print()

    def fetch_messages(self, subject="events.test", limit=5):
        """Example 4: Fetch messages in protobuf format"""
        print(f"=== Example 4: Fetching Messages ({subject}) ===")

        response = self.session.get(
            f"{self.base_url}/api/proto/ProtobufMessages/{subject}",
            params={"limit": limit},
            headers={"Accept": "application/x-protobuf"}
        )

        if not response.ok:
            print(f"✗ Failed to fetch: {response.status_code}")
            return

        fetch_response = message_pb2.FetchResponse()
        fetch_response.ParseFromString(response.content)

        print(f"✓ Fetched {fetch_response.count} messages from {fetch_response.stream}")
        print(f"  Subject: {fetch_response.subject}")
        print("  Messages:")

        for msg in fetch_response.messages:
            print(f"    [{msg.sequence}] {msg.subject}")
            print(f"        Size: {msg.size_bytes} bytes")

            # Convert timestamp
            timestamp = datetime.fromtimestamp(msg.timestamp.seconds)
            print(f"        Time: {timestamp.strftime('%Y-%m-%d %H:%M:%S')}")

            # Try to decode data
            try:
                data_str = msg.data.decode('utf-8')
                preview = data_str[:50] + "..." if len(data_str) > 50 else data_str
                print(f"        Data: {preview}")
            except:
                print(f"        Data: [binary, {len(msg.data)} bytes]")
        print()

    def run_all_examples(self):
        """Run all examples"""
        print(f"Protobuf Python Client - Connecting to {self.base_url}")
        print("=" * 60)
        print()

        try:
            self.publish_generic_message()
            self.publish_user_event()
            self.publish_payment_event()
            self.fetch_messages("events.test", 5)
            self.fetch_messages("events.user.created", 3)

            print("=" * 60)
            print("✓ All examples completed successfully!")

        except requests.exceptions.ConnectionError:
            print(f"✗ Error: Could not connect to {self.base_url}")
            print("  Make sure NatsHttpGateway is running")
        except Exception as e:
            print(f"✗ Error: {e}")


def main():
    """Main entry point"""
    # Configuration priority: CLI arg > Environment variable > Default
    base_url = (
        sys.argv[1] if len(sys.argv) > 1
        else os.getenv("NATS_GATEWAY_URL", "http://localhost:5000")
    )
    client = ProtobufClient(base_url)
    client.run_all_examples()


if __name__ == "__main__":
    main()
