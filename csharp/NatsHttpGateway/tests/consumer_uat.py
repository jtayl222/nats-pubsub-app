#!/usr/bin/env python3
"""Interactive UAT script for NATS HTTP Gateway consumer APIs."""
from __future__ import annotations

import json
import os
import sys
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional

import requests

try:  # pragma: no cover - dependency check for protobuf runtime
    from google.protobuf.timestamp_pb2 import Timestamp  # type: ignore
except ModuleNotFoundError as exc:  # pragma: no cover
    raise SystemExit(
        "Missing dependency 'protobuf'. Install it with 'python -m pip install protobuf'."
    ) from exc

ROOT_DIR = Path(__file__).resolve().parents[1]
if str(ROOT_DIR) not in sys.path:
    sys.path.insert(0, str(ROOT_DIR))

from Examples import message_pb2  # noqa: E402

BASE_URL = os.getenv("GATEWAY_BASE_URL", "http://localhost:8080").rstrip("/")
STREAM = os.getenv("GATEWAY_STREAM", "events")
SUBJECT = os.getenv("GATEWAY_SUBJECT", "events.demo")
CONSUMER_NAME = os.getenv(
    "GATEWAY_CONSUMER",
    f"uat-consumer-{uuid.uuid4().hex[:8]}"
)
HTTP_TIMEOUT = float(os.getenv("GATEWAY_HTTP_TIMEOUT", "30"))
AUTO_ADVANCE = os.getenv("GATEWAY_AUTO_ADVANCE", "false").lower() in {"1", "true", "yes"}
PUBLISH_COUNT = int(os.getenv("GATEWAY_PUBLISH_COUNT", "3"))
LOG_PATH = Path(os.getenv("GATEWAY_UAT_LOG", "consumer_uat_log.json"))

Session = requests.Session()
Session.headers.update({"Content-Type": "application/json"})


@dataclass
class StepResult:
    name: str
    response: Any
    timestamp: str
    expectation: str
    next_action: Optional[str]


STEP_RESULTS: List[StepResult] = []


def banner(title: str) -> None:
    print("\n" + "=" * 80)
    print(title)
    print("=" * 80)


def pause() -> None:
    if AUTO_ADVANCE:
        return
    input("\nPress Enter to continue...")


def request_json(method: str, path: str, **kwargs: Any) -> Any:
    url = f"{BASE_URL}{path}"
    print(f"\n→ {method.upper()} {url}")
    resp = Session.request(method=method, url=url, timeout=HTTP_TIMEOUT, **kwargs)
    print(f"← {resp.status_code}")
    payload: Any
    if resp.headers.get("content-type", "").startswith("application/json"):
        payload = resp.json()
        print(json.dumps(payload, indent=2, sort_keys=True))
    else:
        payload = resp.text
        print(payload)
    resp.raise_for_status()
    return payload


def request_binary(method: str, path: str, *, data: bytes, headers: Optional[Dict[str, str]] = None) -> bytes:
    url = f"{BASE_URL}{path}"
    print(f"\n→ {method.upper()} {url}")
    resp = Session.request(
        method=method,
        url=url,
        timeout=HTTP_TIMEOUT,
        data=data,
        headers=headers,
    )
    print(f"← {resp.status_code} ({len(resp.content)} bytes)")
    resp.raise_for_status()
    return resp.content


def run_step(
    name: str,
    func: Callable[[], Any],
    expectation: str,
    next_action: Optional[str] = None,
    pause_after: bool = True,
) -> Any:
    banner(name)
    result = func()
    timestamp = datetime.now(timezone.utc).isoformat()
    print("\nWhat was tested and what you should see:")
    print(expectation)
    if next_action:
        print(f"\nNext: I will test {next_action}.")
    STEP_RESULTS.append(
        StepResult(
            name=name,
            response=result,
            timestamp=timestamp,
            expectation=expectation,
            next_action=next_action,
        )
    )
    if pause_after:
        pause()
    return result


def step_show_templates() -> Any:
    return request_json("GET", "/api/consumers/templates")


def step_health_check() -> Any:
    return request_json("GET", "/Health")


def step_verify_stream_exists() -> Any:
    return request_json("GET", f"/api/Streams/{STREAM}")


def step_create_consumer() -> Any:
    payload = {
        "name": CONSUMER_NAME,
        "durable": True,
        "filterSubject": SUBJECT,
        "deliverPolicy": "all",
        "ackPolicy": "explicit",
        "maxDeliver": 3,
        "ackWait": "00:00:30"
    }
    return request_json("POST", f"/api/consumers/{STREAM}", json=payload)


def step_list_consumers() -> Any:
    return request_json("GET", f"/api/consumers/{STREAM}")


def step_get_consumer() -> Any:
    return request_json("GET", f"/api/consumers/{STREAM}/{CONSUMER_NAME}")


def step_publish_messages() -> List[Any]:
    print(f"\nPublishing {PUBLISH_COUNT} messages to {SUBJECT}")
    responses: List[Any] = []
    for idx in range(1, PUBLISH_COUNT + 1):
        payload = {
            "message_id": str(uuid.uuid4()),
            "source": "consumer-uat",
            "data": {
                "step": idx,
                "note": "Python UAT message",
                "timestamp": datetime.now(timezone.utc).isoformat()
            }
        }
        responses.append(
            request_json("POST", f"/api/messages/{SUBJECT}", json=payload)
        )
    return responses


def step_fetch_from_consumer(limit: int = 5) -> Any:
    params = {"limit": limit, "timeout": 5}
    return request_json("GET", f"/api/messages/{STREAM}/consumer/{CONSUMER_NAME}", params=params)


def step_publish_protobuf_message() -> Dict[str, Any]:
    msg = message_pb2.PublishMessage(
        message_id=f"proto-{uuid.uuid4().hex[:8]}",
        subject=SUBJECT,
        source="consumer-uat-proto",
    )
    ts = Timestamp()
    ts.FromDatetime(datetime.now(timezone.utc))
    msg.timestamp.CopyFrom(ts)
    msg.data = json.dumps(
        {
            "kind": "proto-uat",
            "note": "PublishMessage dispatched via protobuf endpoint",
            "emitted_at": ts.ToDatetime().isoformat(),
        }
    ).encode("utf-8")

    payload = msg.SerializeToString()
    ack_bytes = request_binary(
        "POST",
        f"/api/proto/protobufmessages/{SUBJECT}",
        data=payload,
        headers={
            "Content-Type": "application/x-protobuf",
            "Accept": "application/x-protobuf",
        },
    )

    ack = message_pb2.PublishAck()
    ack.ParseFromString(ack_bytes)
    ack_dict = {
        "published": ack.published,
        "subject": ack.subject,
        "stream": ack.stream,
        "sequence": ack.sequence,
        "timestamp": ack.timestamp.ToDatetime().isoformat()
        if ack.HasField("timestamp")
        else None,
    }
    print(json.dumps(ack_dict, indent=2))
    return ack_dict


def step_peek_messages(limit: int = 2) -> Any:
    params = {"limit": limit}
    return request_json("GET", f"/api/consumers/{STREAM}/{CONSUMER_NAME}/messages", params=params)


def step_consumer_health() -> Any:
    return request_json("GET", f"/api/consumers/{STREAM}/{CONSUMER_NAME}/health")


def step_consumer_metrics() -> Any:
    params = {"samples": 5}
    return request_json("GET", f"/api/consumers/{STREAM}/{CONSUMER_NAME}/metrics/history", params=params)


def step_reset_consumer() -> Any:
    payload = {"action": "reset"}
    return request_json("POST", f"/api/consumers/{STREAM}/{CONSUMER_NAME}/reset", json=payload)


def step_delete_consumer() -> Any:
    return request_json("DELETE", f"/api/consumers/{STREAM}/{CONSUMER_NAME}")


def step_verify_deletion() -> Dict[str, Any]:
    url = f"/api/consumers/{STREAM}/{CONSUMER_NAME}"
    try:
        request_json("GET", url)
    except requests.HTTPError as exc:
        if exc.response is not None and exc.response.status_code == 404:
            print("Consumer correctly returns 404 after deletion.")
            return {"status": 404, "message": "consumer deleted"}
        raise
    raise RuntimeError("Consumer still exists after deletion")


def write_log() -> None:
    data = [
        {
            "step": result.name,
            "timestamp": result.timestamp,
            "response": result.response,
            "expectation": result.expectation,
            "next_action": result.next_action,
        }
        for result in STEP_RESULTS
    ]
    LOG_PATH.write_text(json.dumps(data, indent=2))
    print(f"\nSaved step log to {LOG_PATH}")


def print_intro() -> None:
    banner("NATS HTTP Gateway Consumer UAT")
    print("Configuration:")
    print(json.dumps({
        "base_url": BASE_URL,
        "stream": STREAM,
        "subject": SUBJECT,
        "consumer": CONSUMER_NAME,
        "auto_advance": AUTO_ADVANCE,
        "publish_count": PUBLISH_COUNT,
        "log_path": str(LOG_PATH)
    }, indent=2))
    pause()


def main() -> None:
    print_intro()

    try:
        health = run_step(
            "Gateway Health",
            step_health_check,
            expectation=(
                "Look for status 'healthy', `nats_connected: true`, and `jetstream_available: true`. "
                "If any field is false, the gateway isn't ready for UAT."
            ),
            next_action="whether the configured stream exists",
            pause_after=False,
        )
    except requests.HTTPError as exc:
        print("Gateway health endpoint unavailable. Ensure the service is running at", BASE_URL)
        sys.exit(1)

    if not (
        isinstance(health, dict)
        and health.get("status", "").lower() == "healthy"
        and health.get("nats_connected")
        and health.get("jetstream_available")
    ):
        print("Gateway reported unhealthy status; fix NATS connection before running UAT.")
        sys.exit(1)

    try:
        run_step(
            "Verify Stream Exists",
            step_verify_stream_exists,
            expectation=(
                "Stream metadata should load with subjects array and message counts. "
                "If this 404s, create the stream (e.g., via CLI) or adjust GATEWAY_STREAM."
            ),
            next_action="the consumer template catalog",
            pause_after=False,
        )
    except requests.HTTPError as exc:
        status = exc.response.status_code if exc.response is not None else "unknown"
        print(
            f"Stream '{STREAM}' not accessible (status {status}). "
            "Create it in JetStream or set GATEWAY_STREAM before rerunning."
        )
        sys.exit(1)

    run_step(
        "Templates",
        step_show_templates,
        expectation=(
            "You should see a JSON array of predefined consumer configs. "
            "These are reference payloads only—you can copy one into the create-step body if desired."
        ),
        next_action="creating a durable consumer",
    )
    run_step(
        "Create Consumer",
        step_create_consumer,
        expectation=(
            "Expect HTTP 201 with the consumer's configuration echoed back. "
            "Verify `durable` is true and `filterSubject` matches the SUBJECT value."
        ),
        next_action="listing all consumers on the stream",
    )
    run_step(
        "List Consumers",
        step_list_consumers,
        expectation=(
            "You should see the new consumer inside the `consumers` collection. "
            "This confirms JetStream registered it on the stream."
        ),
        next_action="retrieving detailed consumer info",
    )
    run_step(
        "Get Consumer",
        step_get_consumer,
        expectation=(
            "Detailed state data should appear, including delivery lag and ack metrics. "
            "This endpoint is useful for debugging configuration mismatches."
        ),
        next_action="publishing sample messages",
    )
    run_step(
        "Publish Messages",
        step_publish_messages,
        expectation=(
            "Each POST should return `published: true` with incrementing sequences. "
            "This seeds the stream so the consumer has data to read."
        ),
        next_action="publishing via the protobuf endpoint",
    )
    run_step(
        "Publish Protobuf Message",
        step_publish_protobuf_message,
        expectation=(
            "Expect a protobuf PublishAck decoded above showing `published: true`. "
            "This proves binary clients using message.proto can interact with the gateway."
        ),
        next_action="fetching via the durable consumer",
    )
    run_step(
        "Fetch via Durable Consumer",
        step_fetch_from_consumer,
        expectation=(
            "Expect a list of messages limited by the query params. "
            "Sequence numbers should align with the publishes, proving stateful delivery."
        ),
        next_action="peeking without moving the consumer cursor",
    )
    run_step(
        "Peek Messages",
        step_peek_messages,
        expectation=(
            "Peek responses show the next messages without moving the consumer cursor. "
            "You should still be able to fetch the same messages later."
        ),
        next_action="checking consumer health",
    )
    run_step(
        "Consumer Health",
        step_consumer_health,
        expectation=(
            "Look for lag, pending, and inactivity indicators. "
            "Useful to confirm the consumer is caught up after fetches."
        ),
        next_action="reviewing consumer metrics history",
    )
    run_step(
        "Consumer Metrics History",
        step_consumer_metrics,
        expectation=(
            "The snapshot should show delivery + ack counters. "
            "Values help baseline performance for future UAT runs."
        ),
        next_action="resetting the consumer position",
    )
    run_step(
        "Reset Consumer",
        step_reset_consumer,
        expectation=(
            "Response should confirm the reset action. "
            "Afterward, fetching should replay from the start of the stream."
        ),
        next_action="fetching again to verify the reset",
    )
    run_step(
        "Fetch After Reset",
        step_fetch_from_consumer,
        expectation=(
            "Sequences should restart from the earliest available message, proving the reset succeeded."
        ),
        next_action="deleting the consumer",
    )
    run_step(
        "Delete Consumer",
        step_delete_consumer,
        expectation=(
            "Look for a confirmation payload (or 200) showing JetStream removed the durable consumer."
        ),
        next_action="verifying the consumer now returns 404",
    )
    run_step(
        "Verify Deletion",
        step_verify_deletion,
        expectation=(
            "A 404 response indicates cleanup worked. "
            "If not, manual deletion may be required before rerunning the script."
        ),
        next_action=None,
        pause_after=False,
    )

    write_log()
    print("\nUAT flow complete.")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nAborted by user.")
        sys.exit(130)
