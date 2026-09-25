# API service

The service exposes a public health endpoint and Cognito-protected device enrollment, task management and screenshot proposal routes. The API can return one model action proposal, a model-reported completion result, or a request for missing task details. It never executes desktop input; Windows local supervision validates and dispatches each action under the owner-submitted task grant and provides STOP.

```powershell
python -m uvicorn services.api.app.main:app --host 127.0.0.1 --port 8765
python -m pytest services/api/tests
```

The fake provider and task state machine are local orchestration primitives. They do not call a hosted model or execute desktop input. The live provider uses Gemini 3.5 Flash-Lite with automatic SDK tool execution disabled. Screenshots are not stored in the task journal. The model prompt treats the owner instruction as the task grant; untrusted screen content cannot extend it.
