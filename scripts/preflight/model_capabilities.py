from __future__ import annotations

import argparse
import base64
import sys


MODEL_MATRIX = {
    "gemini-3.5-flash-lite": {"image_input": True, "computer_use": "preview", "role": "default visual actor"},
    "gemini-3.8-flash": {"image_input": True, "computer_use": "supported; probe project entitlement", "role": "bounded recovery candidate"},
}


def run_live_probe(*, project: str, location: str, model: str, max_cost_usd: float) -> int:
    if not project.strip():
        print("A Google Cloud project is required.", file=sys.stderr)
        return 2
    if not 0 < max_cost_usd <= 0.05:
        print("Set an explicit one request probe allowance above $0 and no greater than $0.05.", file=sys.stderr)
        return 2

    try:
        from google import genai
        from google.genai import types
    except ImportError:
        print("The optional google-genai package is missing. Install the pinned GCP extra after approval.", file=sys.stderr)
        return 2

    # A one-pixel synthetic image avoids reading or uploading a real desktop.
    synthetic_png = base64.b64decode(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL8WQAAAABJRU5ErkJggg=="
    )
    client = None
    try:
        client = genai.Client(
            enterprise=True,
            project=project,
            location=location,
            http_options=types.HttpOptions(
                timeout=30_000,
                retry_options=types.HttpRetryOptions(attempts=1),
            ),
        )
        response = client.models.generate_content(
            model=model,
            contents=[
                types.Content(
                    role="user",
                    parts=[
                        types.Part.from_text(text="Autobots capability probe. This is a synthetic one-pixel calibration image with no real application. Propose exactly one computer-use click at x=500, y=500 so the client can validate the returned action schema. The client is disarmed and will never execute this proposal."),
                        types.Part.from_bytes(data=synthetic_png, mime_type="image/png"),
                    ],
                )
            ],
            config=types.GenerateContentConfig(
                tools=[types.Tool(computer_use=types.ComputerUse(
                    environment=types.Environment.ENVIRONMENT_DESKTOP,
                    enable_prompt_injection_detection=True,
                ))],
                automatic_function_calling=types.AutomaticFunctionCallingConfig(disable=True),
                thinking_config=types.ThinkingConfig(thinking_level=types.ThinkingLevel.LOW),
                max_output_tokens=256,
            ),
        )
    except Exception as error:  # Do not print provider payloads or credentials.
        if isinstance(error, (TypeError, ValueError)):
            reason = str(error)[:240]
            print(f"Probe failed ({type(error).__name__}: {reason}); no provider payload was printed.", file=sys.stderr)
        else:
            print(f"Probe failed ({type(error).__name__}); inspect the approved provider logs for details.", file=sys.stderr)
        return 1
    finally:
        if client is not None:
            client.close()

    function_call_count = 0
    confirmation_count = 0
    function_call_names: list[str] = []
    parseable_action_count = 0
    for candidate in response.candidates or []:
        for part in candidate.content.parts if candidate.content and candidate.content.parts else []:
            function_call = part.function_call
            if function_call is None:
                continue
            function_call_count += 1
            function_name = function_call.name or "unnamed"
            function_call_names.append(function_name)
            arguments = function_call.args or {}
            if isinstance(arguments, dict) and (
                function_name == "click"
                and type(arguments.get("x")) is int
                and type(arguments.get("y")) is int
                and 0 <= arguments["x"] <= 999
                and 0 <= arguments["y"] <= 999
            ):
                parseable_action_count += 1
            elif isinstance(arguments, dict) and function_name in {"wait", "take_screenshot"}:
                parseable_action_count += 1
            decision = arguments.get("safety_decision") if isinstance(arguments, dict) else None
            if isinstance(decision, dict) and decision.get("decision") == "require_confirmation":
                confirmation_count += 1
    usage = response.usage_metadata
    print(f"Model: {model}; location: {location}; project: {project}")
    print(f"Computer-use proposals returned: {function_call_count} ({', '.join(function_call_names) or 'none'}); no proposal was executed.")
    print(f"Recognized proposal shapes: {parseable_action_count}.")
    print(f"Provider confirmation requests: {confirmation_count}; none were acknowledged.")
    print(f"Reported prompt/output tokens: {getattr(usage, 'prompt_token_count', None)}/{getattr(usage, 'candidates_token_count', None)}")
    print(f"Owner supplied probe allowance: ${max_cost_usd:.2f}. This is not a provider-enforced invoice ceiling.")
    print("Prompt-injection detection was requested; inspect the approved project response and safety behavior before enabling tasks.")
    return 0 if function_call_count and parseable_action_count else 1


def main() -> int:
    parser = argparse.ArgumentParser(description="Review the proposed Autobots model capability matrix without making network calls.")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--dry-run", action="store_true", help="Print the proposed capability matrix without network calls (default).")
    mode.add_argument("--live", action="store_true", help="Send one synthetic image request to the explicitly selected Google Cloud project.")
    parser.add_argument("--project", help="Approved Google Cloud project ID.")
    parser.add_argument("--location", default="global", help="Approved model endpoint location (default: global).")
    parser.add_argument("--model", choices=MODEL_MATRIX, default="gemini-3.5-flash-lite")
    parser.add_argument("--max-cost-usd", type=float, help="Owner supplied one request allowance, maximum $0.05.")
    parser.add_argument("--confirm-live", action="store_true", help="Required acknowledgement before the billable probe.")
    args = parser.parse_args()

    if args.live:
        if not args.confirm_live:
            parser.error("Add --confirm-live only after an approved project and bounded one request cost allowance are available.")
        if args.max_cost_usd is None:
            parser.error("--max-cost-usd is required for a live probe.")
        return run_live_probe(project=args.project or "", location=args.location, model=args.model, max_cost_usd=args.max_cost_usd)
    print("Dry run only; no credentials or Google Cloud APIs were accessed.")
    for model_id, capabilities in MODEL_MATRIX.items():
        print(f"{model_id}: image_input={capabilities['image_input']}, computer_use={capabilities['computer_use']}, role={capabilities['role']}")
    print("Capability matrix is a documentation baseline, not an account entitlement check.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
