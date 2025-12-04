#!/usr/bin/env python3
"""Interactive CLI menu for testing NATS HTTP Gateway APIs."""
import json
import os
import sys
from datetime import datetime, timezone
from typing import Any, Dict, Optional
import uuid

import requests

# Configuration
BASE_URL = os.getenv("GATEWAY_BASE_URL", "http://localhost:8080").rstrip("/")
DEFAULT_STREAM = os.getenv("GATEWAY_STREAM", "events")
DEFAULT_SUBJECT = os.getenv("GATEWAY_SUBJECT", "events.demo")
DEFAULT_CONSUMER = "test-consumer"


class Colors:
    """ANSI color codes for terminal output."""
    HEADER = '\033[95m'
    BLUE = '\033[94m'
    CYAN = '\033[96m'
    GREEN = '\033[92m'
    YELLOW = '\033[93m'
    RED = '\033[91m'
    END = '\033[0m'
    BOLD = '\033[1m'


def print_header(text: str):
    """Print a colored header."""
    print(f"\n{Colors.BOLD}{Colors.HEADER}{'='*80}{Colors.END}")
    print(f"{Colors.BOLD}{Colors.HEADER}{text}{Colors.END}")
    print(f"{Colors.BOLD}{Colors.HEADER}{'='*80}{Colors.END}\n")


def print_success(text: str):
    """Print success message."""
    print(f"{Colors.GREEN}{text}{Colors.END}")


def print_error(text: str):
    """Print error message."""
    print(f"{Colors.RED}{text}{Colors.END}")


def print_info(text: str):
    """Print info message."""
    print(f"{Colors.CYAN}{text}{Colors.END}")


def print_curl(method: str, url: str, body: Optional[Dict] = None):
    """Print curl command."""
    print(f"\n{Colors.YELLOW}Curl equivalent:{Colors.END}")
    curl_parts = [f"curl -X {method}"]

    if body:
        curl_parts.append("-H 'Content-Type: application/json'")
        curl_parts.append(f"-d '{json.dumps(body)}'")

    curl_parts.append(f"'{url}'")

    print(f"{Colors.CYAN}{' \\\n  '.join(curl_parts)}{Colors.END}\n")


# Endpoint definitions
ENDPOINTS = {
    "Message Endpoints": None,  # Section header
    "1": {
        "name": "Publish a message to a NATS subject",
        "method": "POST",
        "path": "/api/messages/{subject}",
        "params": {"subject": DEFAULT_SUBJECT},
        "body": {
            "message_id": "<<UUID>>",
            "source": "interactive-tester",
            "data": {
                "test": "message",
                "timestamp": "<<TIMESTAMP>>"
            }
        }
    },
    "2": {
        "name": "Fetch last N messages (ephemeral consumer)",
        "method": "GET",
        "path": "/api/messages/{subjectFilter}",
        "params": {"subjectFilter": DEFAULT_SUBJECT, "limit": "10", "timeout": "5"}
    },
    "3": {
        "name": "Fetch messages from durable consumer",
        "method": "GET",
        "path": "/api/messages/{stream}/consumer/{consumerName}",
        "params": {"stream": DEFAULT_STREAM, "consumerName": DEFAULT_CONSUMER, "limit": "10", "timeout": "5"}
    },

    "Consumer Endpoints": None,  # Section header
    "4": {
        "name": "Get predefined consumer templates",
        "method": "GET",
        "path": "/api/consumers/templates",
        "params": {}
    },
    "5": {
        "name": "List all consumers for a stream",
        "method": "GET",
        "path": "/api/consumers/{stream}",
        "params": {"stream": DEFAULT_STREAM}
    },
    "6": {
        "name": "Get detailed information about a specific consumer",
        "method": "GET",
        "path": "/api/consumers/{stream}/{consumer}",
        "params": {"stream": DEFAULT_STREAM, "consumer": DEFAULT_CONSUMER}
    },
    "7": {
        "name": "Create a new consumer on a stream",
        "method": "POST",
        "path": "/api/consumers/{stream}",
        "params": {"stream": DEFAULT_STREAM},
        "body": {
            "name": DEFAULT_CONSUMER,
            "durable": True,
            "filterSubject": DEFAULT_SUBJECT,
            "deliverPolicy": "all",
            "ackPolicy": "explicit",
            "maxDeliver": 3,
            "ackWait": "00:00:30"
        }
    },
    "8": {
        "name": "Check the health status of a consumer",
        "method": "GET",
        "path": "/api/consumers/{stream}/{consumer}/health",
        "params": {"stream": DEFAULT_STREAM, "consumer": DEFAULT_CONSUMER}
    },
    "9": {
        "name": "Peek at messages from a consumer without acknowledging them",
        "method": "GET",
        "path": "/api/consumers/{stream}/{consumer}/messages",
        "params": {"stream": DEFAULT_STREAM, "consumer": DEFAULT_CONSUMER, "limit": "10"}
    },
    "10": {
        "name": "Reset or replay messages from a consumer",
        "method": "POST",
        "path": "/api/consumers/{stream}/{consumer}/reset",
        "params": {"stream": DEFAULT_STREAM, "consumer": DEFAULT_CONSUMER},
        "body": {"action": "reset"}
    },
    "11": {
        "name": "Get metrics history for a consumer",
        "method": "GET",
        "path": "/api/consumers/{stream}/{consumer}/metrics/history",
        "params": {"stream": DEFAULT_STREAM, "consumer": DEFAULT_CONSUMER, "samples": "5"}
    },
    "12": {
        "name": "Delete a consumer from a stream",
        "method": "DELETE",
        "path": "/api/consumers/{stream}/{consumer}",
        "params": {"stream": DEFAULT_STREAM, "consumer": DEFAULT_CONSUMER}
    }
}


def show_menu():
    """Display the main menu."""
    print_header("NATS HTTP Gateway API Tester")
    print(f"Base URL: {Colors.CYAN}{BASE_URL}{Colors.END}")
    print(f"Default Stream: {Colors.CYAN}{DEFAULT_STREAM}{Colors.END}")
    print(f"Default Subject: {Colors.CYAN}{DEFAULT_SUBJECT}{Colors.END}")
    print(f"Default Consumer: {Colors.CYAN}{DEFAULT_CONSUMER}{Colors.END}\n")

    for key, endpoint in ENDPOINTS.items():
        if endpoint is None:  # Section header
            print(f"\n{Colors.BOLD}{key}:{Colors.END}")
        else:
            print(f"  {Colors.BOLD}{key}.{Colors.END} {endpoint['name']}")

    print(f"\n  {Colors.BOLD}c.{Colors.END} Change configuration")
    print(f"  {Colors.BOLD}q.{Colors.END} Quit\n")


def edit_params(params: Dict[str, str]) -> Dict[str, str]:
    """Allow user to edit parameters."""
    print(f"\n{Colors.YELLOW}Current parameters:{Colors.END}")
    for key, value in params.items():
        print(f"  {key}: {Colors.CYAN}{value}{Colors.END}")

    print(f"\n{Colors.YELLOW}Press Enter to keep current value, or type new value:{Colors.END}")

    new_params = {}
    for key, value in params.items():
        user_input = input(f"{key} [{value}]: ").strip()
        new_params[key] = user_input if user_input else value

    return new_params


def edit_body(body: Dict) -> Dict:
    """Allow user to edit request body."""
    print(f"\n{Colors.YELLOW}Current request body:{Colors.END}")
    body_str = json.dumps(body, indent=2)
    print(f"{Colors.CYAN}{body_str}{Colors.END}")

    print(f"\n{Colors.YELLOW}Options:{Colors.END}")
    print("  1. Keep as-is")
    print("  2. Edit JSON manually")

    choice = input("\nChoice [1]: ").strip()

    if choice == "2":
        print(f"\n{Colors.YELLOW}Enter new JSON (type 'END' on a line by itself to finish):{Colors.END}")
        lines = []
        while True:
            line = input()
            if line.strip() == "END":
                break
            lines.append(line)

        try:
            return json.loads('\n'.join(lines))
        except json.JSONDecodeError as e:
            print_error(f"Invalid JSON: {e}")
            print("Using original body.")
            return body

    return body


def build_url(endpoint: Dict, params: Dict[str, str]) -> tuple:
    """Build URL from endpoint and parameters."""
    path = endpoint["path"]
    query_params = {}

    # Separate path params from query params
    import re
    path_param_names = [m.group(1) for m in re.finditer(r'\{(\w+)\}', path)]

    # Replace path parameters
    for param_name in path_param_names:
        if param_name in params:
            path = path.replace(f"{{{param_name}}}", params[param_name])

    # Build query parameters (everything else)
    for key, value in params.items():
        if key not in path_param_names and value:
            query_params[key] = value

    url = BASE_URL + path
    if query_params:
        query_string = "&".join(f"{k}={v}" for k, v in query_params.items())
        url += f"?{query_string}"

    return url, query_params


def execute_endpoint(endpoint: Dict):
    """Execute the selected endpoint."""
    print_header(endpoint["name"])

    # Get parameters
    params = endpoint.get("params", {}).copy()

    if params:
        print(f"{Colors.YELLOW}Edit parameters? (y/N):{Colors.END} ", end="")
        if input().strip().lower() == 'y':
            params = edit_params(params)

    # Get body
    body = None
    if "body" in endpoint:
        body = endpoint["body"].copy()

        # Replace placeholders
        body_str = json.dumps(body)
        body_str = body_str.replace('"<<UUID>>"', f'"{str(uuid.uuid4())}"')
        body_str = body_str.replace('"<<TIMESTAMP>>"', f'"{datetime.now(timezone.utc).isoformat()}"')
        body = json.loads(body_str)

        print(f"\n{Colors.YELLOW}Request body:{Colors.END}")
        print(f"{Colors.CYAN}{json.dumps(body, indent=2)}{Colors.END}")

        print(f"\n{Colors.YELLOW}Edit body? (y/N):{Colors.END} ", end="")
        if input().strip().lower() == 'y':
            body = edit_body(body)

    # Build URL
    url, _ = build_url(endpoint, params)

    # Show curl
    print_curl(endpoint["method"], url, body)

    # Ask to execute
    print(f"{Colors.YELLOW}Execute request? (Y/n):{Colors.END} ", end="")
    if input().strip().lower() == 'n':
        print("Skipped.")
        return

    # Execute
    try:
        print(f"\n{Colors.CYAN}Executing...{Colors.END}")

        headers = {"Content-Type": "application/json"}
        method = endpoint["method"]

        if method == "GET":
            response = requests.get(url, headers=headers, timeout=30)
        elif method == "POST":
            response = requests.post(url, json=body, headers=headers, timeout=30)
        elif method == "DELETE":
            response = requests.delete(url, headers=headers, timeout=30)
        else:
            response = requests.request(method, url, json=body, headers=headers, timeout=30)

        # Display response
        print(f"\n{Colors.BOLD}Status: {response.status_code}{Colors.END}")

        if response.status_code >= 400:
            print_error(f"Error: {response.status_code}")
        else:
            print_success(f"Success: {response.status_code}")

        print(f"\n{Colors.BOLD}Response:{Colors.END}")

        try:
            json_response = response.json()
            print(json.dumps(json_response, indent=2))
        except:
            print(response.text)

    except requests.RequestException as e:
        print_error(f"\nRequest failed: {e}")


def change_config():
    """Change global configuration."""
    global BASE_URL, DEFAULT_STREAM, DEFAULT_SUBJECT, DEFAULT_CONSUMER

    print_header("Change Configuration")

    print(f"Base URL [{BASE_URL}]: ", end="")
    new_url = input().strip()
    if new_url:
        BASE_URL = new_url.rstrip("/")

    print(f"Default Stream [{DEFAULT_STREAM}]: ", end="")
    new_stream = input().strip()
    if new_stream:
        DEFAULT_STREAM = new_stream

    print(f"Default Subject [{DEFAULT_SUBJECT}]: ", end="")
    new_subject = input().strip()
    if new_subject:
        DEFAULT_SUBJECT = new_subject

    print(f"Default Consumer [{DEFAULT_CONSUMER}]: ", end="")
    new_consumer = input().strip()
    if new_consumer:
        DEFAULT_CONSUMER = new_consumer

    print_success("\nConfiguration updated!")


def main():
    """Main interactive loop."""
    while True:
        show_menu()

        choice = input(f"{Colors.BOLD}Select option:{Colors.END} ").strip()

        if choice.lower() == 'q':
            print("\nGoodbye!")
            break
        elif choice.lower() == 'c':
            change_config()
        elif choice in ENDPOINTS and ENDPOINTS[choice] is not None:
            execute_endpoint(ENDPOINTS[choice])
        else:
            print_error("Invalid choice. Please try again.")

        input(f"\n{Colors.YELLOW}Press Enter to continue...{Colors.END}")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n\nInterrupted. Goodbye!")
        sys.exit(0)
