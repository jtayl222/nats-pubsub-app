#!/usr/bin/env python3
"""
Example Python WebSocket client for NatsHttpGateway streaming endpoints.

Requirements:
    pip install protobuf websockets

Usage:
    python websocket_client_example.py
"""

import sys
import os
import asyncio
import websockets
from datetime import datetime

# Import generated protobuf classes
try:
    import message_pb2
except ImportError:
    print("Error: message_pb2 not found.")
    print("Generate it with: protoc --python_out=. ../Protos/message.proto")
    sys.exit(1)


class WebSocketClient:
    """Client for streaming messages via WebSocket from NatsHttpGateway"""

    def __init__(self, base_url="http://localhost:5000"):
        self.base_url = base_url
        self.ws_base_url = base_url.replace("http://", "ws://").replace("https://", "wss://")

    async def stream_from_ephemeral_consumer(self, subject_filter="events.>", max_messages=10):
        """Example 1: Stream messages from an ephemeral consumer using subject filter"""
        print(f"=== Example 1: Streaming from Ephemeral Consumer ({subject_filter}) ===")

        ws_url = f"{self.ws_base_url}/ws/websocketmessages/{subject_filter}"
        print(f"Connecting to: {ws_url}")

        try:
            async with websockets.connect(ws_url) as websocket:
                print("✓ WebSocket connected")

                message_count = 0

                while message_count < max_messages:
                    try:
                        # Receive binary protobuf frame
                        frame_bytes = await websocket.recv()

                        # Parse the WebSocketFrame
                        frame = message_pb2.WebSocketFrame()
                        frame.ParseFromString(frame_bytes)

                        if frame.type == message_pb2.CONTROL:
                            self._handle_control_message(frame.control)
                        elif frame.type == message_pb2.MESSAGE:
                            self._handle_stream_message(frame.message)
                            message_count += 1

                    except websockets.exceptions.ConnectionClosed as e:
                        print(f"✓ Connection closed: {e}")
                        break

                print(f"✓ Received {message_count} messages")

        except Exception as e:
            print(f"✗ Error: {e}")

        print()

    async def stream_from_durable_consumer(
        self,
        stream_name="EVENTS",
        consumer_name="my-durable-consumer",
        max_messages=10
    ):
        """Example 2: Stream messages from a durable consumer"""
        print(f"=== Example 2: Streaming from Durable Consumer ({consumer_name}) ===")

        ws_url = f"{self.ws_base_url}/ws/websocketmessages/{stream_name}/consumer/{consumer_name}"
        print(f"Connecting to: {ws_url}")

        try:
            async with websockets.connect(ws_url) as websocket:
                print("✓ WebSocket connected")

                message_count = 0

                while message_count < max_messages:
                    try:
                        # Receive binary protobuf frame
                        frame_bytes = await websocket.recv()

                        # Parse the WebSocketFrame
                        frame = message_pb2.WebSocketFrame()
                        frame.ParseFromString(frame_bytes)

                        if frame.type == message_pb2.CONTROL:
                            self._handle_control_message(frame.control)
                        elif frame.type == message_pb2.MESSAGE:
                            self._handle_stream_message(frame.message)
                            message_count += 1

                    except websockets.exceptions.ConnectionClosed as e:
                        print(f"✓ Connection closed: {e}")
                        break

                print(f"✓ Received {message_count} messages")

        except websockets.exceptions.InvalidStatusCode as e:
            print(f"✗ Error: {e}")
            print(f"  Make sure the durable consumer '{consumer_name}' exists in stream '{stream_name}'")
        except Exception as e:
            print(f"✗ Error: {e}")

        print()

    async def stream_with_timeout(self, subject_filter="events.test", timeout_seconds=30):
        """Example 3: Stream with custom message processing and timeout"""
        print(f"=== Example 3: Streaming with Timeout ({timeout_seconds}s) ===")

        ws_url = f"{self.ws_base_url}/ws/websocketmessages/{subject_filter}"
        print(f"Connecting to: {ws_url}")

        try:
            async with websockets.connect(ws_url) as websocket:
                print("✓ WebSocket connected")
                print(f"  Will disconnect after {timeout_seconds} seconds")

                message_count = 0
                start_time = datetime.now()

                try:
                    while True:
                        # Receive with timeout
                        frame_bytes = await asyncio.wait_for(
                            websocket.recv(),
                            timeout=timeout_seconds
                        )

                        # Parse the WebSocketFrame
                        frame = message_pb2.WebSocketFrame()
                        frame.ParseFromString(frame_bytes)

                        if frame.type == message_pb2.CONTROL:
                            self._handle_control_message(frame.control)
                        elif frame.type == message_pb2.MESSAGE:
                            message_count += 1
                            elapsed = datetime.now() - start_time
                            minutes, seconds = divmod(elapsed.total_seconds(), 60)
                            print(f"  [{int(minutes):02d}:{int(seconds):02d}] Message #{message_count}: "
                                  f"{frame.message.subject} (seq: {frame.message.sequence})")

                except asyncio.TimeoutError:
                    print(f"✓ Timeout reached after {timeout_seconds} seconds")

                except websockets.exceptions.ConnectionClosed as e:
                    print(f"✓ Connection closed: {e}")

                print(f"✓ Stream ended - received {message_count} messages")

        except Exception as e:
            print(f"✗ Error: {e}")

        print()

    def _handle_control_message(self, control):
        """Handle control messages from the server"""
        icons = {
            message_pb2.ERROR: "✗",
            message_pb2.SUBSCRIBE_ACK: "✓",
            message_pb2.CLOSE: "✓",
            message_pb2.KEEPALIVE: "♥"
        }
        icon = icons.get(control.type, "•")

        type_name = message_pb2.ControlType.Name(control.type)
        print(f"{icon} Control [{type_name}]: {control.message}")

    def _handle_stream_message(self, message):
        """Handle stream messages from NATS"""
        print("  Message received:")
        print(f"    Subject:  {message.subject}")
        print(f"    Sequence: {message.sequence}")
        print(f"    Size:     {message.size_bytes} bytes")

        if message.HasField('timestamp'):
            timestamp = datetime.fromtimestamp(message.timestamp.seconds)
            millis = message.timestamp.nanos // 1000000
            print(f"    Time:     {timestamp.strftime('%Y-%m-%d %H:%M:%S')}.{millis:03d}")

        if message.consumer:
            print(f"    Consumer: {message.consumer}")

        # Try to decode data as UTF-8 string
        if message.data:
            try:
                data_str = message.data.decode('utf-8')
                preview = data_str[:100] + "..." if len(data_str) > 100 else data_str
                print(f"    Data:     {preview}")
            except:
                print(f"    Data:     [binary, {len(message.data)} bytes]")

        print()

    async def run_all_examples(self):
        """Run all examples"""
        print(f"WebSocket Python Client - Connecting to {self.base_url}")
        print("=" * 80)
        print()

        try:
            # Example 1: Stream from ephemeral consumer
            await self.stream_from_ephemeral_consumer("events.>", max_messages=5)

            # Example 2: Stream from durable consumer (will fail if consumer doesn't exist)
            # Uncomment if you have a durable consumer set up:
            # await self.stream_from_durable_consumer("EVENTS", "my-durable-consumer", max_messages=5)

            # Example 3: Stream with timeout
            await self.stream_with_timeout("events.test", timeout_seconds=10)

            print("=" * 80)
            print("✓ All examples completed successfully!")

        except Exception as e:
            print(f"✗ Error: {e}")
            print(f"  Make sure NatsHttpGateway is running at {self.base_url}")
            print("  Make sure NATS is running and has messages to stream")


async def main():
    """Main entry point"""
    # Configuration priority: CLI arg > Environment variable > Default
    base_url = (
        sys.argv[1] if len(sys.argv) > 1
        else os.getenv("NATS_GATEWAY_URL", "http://localhost:5000")
    )
    client = WebSocketClient(base_url)
    await client.run_all_examples()


if __name__ == "__main__":
    asyncio.run(main())
