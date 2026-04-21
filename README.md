# ComputerUseAgent.Demo

A .NET 10 demo of an OpenAI Responses API agent that safely executes shell and filesystem actions inside an isolated Docker workspace, with full trace logs and downloadable artifacts.

## Architecture

```text
User -> Web UI -> API -> Orchestrator -> OpenAI Responses API
                             |-> Workspace service
                             |-> Docker sandbox
                             |-> SQLite repository
```

## Prerequisites

- .NET 10 SDK
- Docker
- OpenAI API key

## Run

1. Build the sandbox image:
   `docker build -t computeruseagent-sandbox:local docker/sandbox-python`
2. Set `OpenAI__ApiKey`.
3. Start the API:
   `dotnet run --project src/ComputerUseAgent.Api`
4. Start the web UI:
   `dotnet run --project src/ComputerUseAgent.Web`

## Example prompts

See `examples/prompts/`.

## Safety limits

- Strict command allowlist and blocklist
- Workspace-only file access
- No host command execution
- Docker-only shell execution
- File, output, time, and step caps

