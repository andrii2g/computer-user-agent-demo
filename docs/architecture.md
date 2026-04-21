# Architecture

`ComputerUseAgent.Api` owns orchestration and persistence. `ComputerUseAgent.Web` is a thin Razor Pages frontend over HTTP. `ComputerUseAgent.Core` contains the domain contracts and orchestration logic. `ComputerUseAgent.Infrastructure` implements Docker, SQLite, workspace IO, IDs, time, and the OpenAI Responses API client.
