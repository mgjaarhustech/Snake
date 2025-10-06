# 🐍 Snake AI Tournament — Environment & APIs

A tiny, deterministic **Snake** environment with **gRPC** and a simple **REST** mirror.  
Students write agents; this repo provides the environment server and example clients.

---

## Table of Contents

- [Requirements](#requirements)
- [Run the Server](#run-the-server)
  - [A) .NET (direct)](#a-net-direct)
  - [B) Docker](#b-docker)
- [API Surface](#api-surface)
  - [Actions](#actions)
  - [Reward Signals](#reward-signals)
  - [Observation Types (ObsType)](#observation-types-obstype)
  - [Raycasts19 Details](#raycasts19-details)
- [REST (JSON) Quickstart](#rest-json-quickstart)
- [gRPC Quickstart (Python)](#grpc-quickstart-python)
- [Determinism Check](#determinism-check)
- [Repo Layout](#repo-layout)
- [FAQ / Tips](#faq--tips)

---

## Requirements

- **.NET 8 SDK** (run the server)
- **Docker** (optional; containerized run)
- **Python 3.9+** (optional; example clients)
- Open ports: **8080** (REST) and **50051** (gRPC)

---

## Run the Server

### A) .NET (direct)

```bash
cd env-dotnet
dotnet restore
dotnet build -c Release
dotnet run -c Release -- --cols 40 --rows 30 --timeout-mult 150 --rest-port 8080 --grpc-port 50051
```

- Swagger (REST): http://localhost:8080/swagger
- gRPC endpoint: localhost:50051

### B) Docker
```bash
cd env-dotnet
dotnet publish -c Release -r linux-x64
docker build -t snake-env .
docker run -p 8080:8080 -p 50051:50051 snake-env
```