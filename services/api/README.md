# API service

The service exposes a public health endpoint (with `api_version` and an advertised `features` list) and Cognito-protected device enrollment, task management, screenshot proposal and voice transcription routes. The API can return one model action proposal (with the model's short stated intent for display), a model-reported completion result, or a request for missing task details. It never executes desktop input; Windows local supervision validates and dispatches each action under the owner-submitted task grant and provides STOP.

```powershell
python -m uvicorn services.api.app.main:app --host 127.0.0.1 --port 8765
python -m pytest services/api/tests
```

The fake provider and task state machine are local orchestration primitives. They do not call a hosted model or execute desktop input. The live provider uses Gemini 3.5 Flash-Lite desktop computer use with automatic SDK tool execution disabled, `LOW` thinking, and the unsupported predefined functions excluded. Proposal requests may carry a bounded step history recorded by the device and the active window title; both are labelled as untrusted data in the prompt. Screenshots are not stored in the task journal. The model prompt treats the owner instruction as the task grant; untrusted screen content cannot extend it.

`POST /v1/transcriptions` accepts one push-to-talk PCM WAV clip (at most 60 seconds) from an enrolled device, transcribes it with Gemini (`AUTOBOTS_TRANSCRIPTION_MODEL_ID`, defaulting to the action model), and returns the text. Audio is not stored, the route cannot create or start a task, and spend is tracked in a separate `speech_reservations` ledger (`AUTOBOTS_MAX_SPEECH_COST_USD_PER_REQUEST`, `_PER_DAY`, `_PER_MONTH`; defaults $0.01, $0.10 and $2.00).
