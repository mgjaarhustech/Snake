# 🐍 Snake AI Tournament — Environment & APIs

Deterministic **Snake** environment with **gRPC** and a simple **REST** mirror.  
Students write agents; this repo provides the environment server, encoders, and tiny example clients/tests.

---

## Table of Contents

- [Requirements](#requirements)
- [Run the Server](#run-the-server)
  - [A) .NET (direct)](#a-net-direct)
  - [B) Docker](#b-docker)
- [Swagger](#swagger)
- [API Surface](#api-surface)
  - [Actions (discrete)](#actions-discrete)
  - [Reward Signals](#reward-signals)
  - [Observation Types (ObsType)](#observation-types-obstype)
  - [Raycasts19 Details](#raycasts19-details)
- [REST (JSON) Endpoints](#rest-json-endpoints)
  - [Examples (curl)](#examples-curl)
- [gRPC Overview](#grpc-overview)
  - [Requesting Raw Frames via gRPC (`with_raw`)](#requesting-raw-frames-via-grpc-with_raw)
- [Clients & Quickstarts](#clients--quickstarts)
  - [Python (REST) minimal client](#python-rest-minimal-client)
  - [Node/JS (REST) minimal client](#nodejs-rest-minimal-client)
  - [C# (REST) minimal client](#c-rest-minimal-client)
- [Smoke Tests](#smoke-tests)
- [Determinism](#determinism)
- [Repo Layout](#repo-layout)
- [FAQ / Tips](#faq--tips)

---

## Requirements

- **.NET 8 SDK** (to run the server)
- **Docker** (optional; to run containerized)
- **Python 3.9+** (optional; for example clients/tests)
- Open ports: **8080** (REST) and **50051** (gRPC)

---

## Run the Server

### A) .NET (direct)

```bash
cd env-dotnet
dotnet restore
dotnet build -c Release
dotnet run -c Release -- --cols 30 --rows 30 --timeout-mult 150 --rest-port 8080 --grpc-port 50051
```

### B) Docker
```bash
cd env-dotnet
dotnet publish -c Release -r linux-x64 -p:PublishSingleFile=true
docker build -t snake-env .
docker run -p 8080:8080 -p 50051:50051 snake-env
```

## Swagger
Open **Swagger UI** at:
```bash
http://localhost:8080/swagger
```
You can “play” the environment from here:
1. **GET /v1/spec** to inspect.
2. **POST /v1/reset** to start (choose obs_type).
3. **POST /v1/step** to send numeric actions (0/1/2).


## API Surface

### Actions

**Numeric only** for REST & gRPC:

- ```0``` → Straight
- ```1``` → TurnRight
- ```2``` → TurnLeft

### Reward Signals

Each step returns ```signals``` (length 6) in this fixed order:

```bash
[ eat_food, death, step_cost, toward_food, turning, timeout ]
```

- ```eat_food``` (e): 1.0 on the step you eat, else 0.0.
- ```death``` (d): 1.0 on the terminal step (wall/self/timeout), else 0.0.
- ```step_cost``` (s): 1.0 every step (including terminal). Apply your own weight.
- ```toward_food``` (t): ΔChebyshev / max(cols,rows) ∈ {−1/max, 0, +1/max}.
- ```turning``` (r): 1.0 if you turned this step, else 0.0.
- ```timeout``` (T): 1.0 only on terminal timeout, else 0.0.

### Observation Types (ObsType)

REST request enum casing: **PascalCase** (```Dense11```, ```RawState```, …)
gRPC enum casing (generated stubs): **UPPER_SNAKE_CASE** (```DENSE11```, ```RAW_STATE```, …)

| **Name**      | **Length**     | **Description**      |
| ------------- | ------------- | ------------- |
| Dense11 | 11 | 3 danger flags (F,R,L), dir one-hot [L,R,U,D], food bits [L,R,U,D]. |
| Dense28Ego | 28 | 24 egocentric cells + food relative to heading [ahead, behind, left, right]. |
| Dense32 | 32 | 5×5 ring (excluding center): blocked=1, plus dir one-hot + food bits. |
| Raycasts19 | 19 | 8 dirs × (frac_to_wall, frac_to_body) = 16 + 3 food features. |
| RawState | - | Full board geometry: head, body, food, grid size, dir string. |
| RGB | - | NOT IMPLEMENTED |

### Raycasts19 Details
Order: **N, NE, E, SE, S, SW, W, NW**; each contributes two floats: ```frac_to_wall```, then ```frac_to_body```.
- ```frac_to_wall = steps_to_wall_from_head / max_steps_to_wall_global_in_that_direction```
  - Axial max: ```cols-1``` (E/W), ```rows-1``` (N/S)
  - Diagonal max: ```min(cols-1, rows-1)```
- ```frac_to_body = steps_to_first_body / same_max```, or ```1.0 if``` no body before wall.
- Food (last 3):
  - ```dx_norm = (food.x - head.x)/(cols-1) ∈ [-1,1]```
  - ```dy_norm = (food.y - head.y)/(rows-1) ∈ [-1,1]```
  - ```cheb_norm = chebyshev(head,food)/max(cols,rows) ∈ [0,1]```


## REST (JSON) Endpoints
- **GET** ```/v1/spec``` → environment spec
- **POST** ```/v1/reset``` → body ```{ "seed": <uint64>, "obs_type": "<PascalCase>" }```
- **POST** ```/v1/step``` → ```body { "action": <int 0/1/2> }```
- **POST** ```/v1/reset_many``` → body ```{ "seeds"?: [uint64], "obs_type": "<PascalCase>", "count": <int>, "session"?: "<string>" }```
  - Returns ```{ "session": "<id>", "envs": [StepResponse, ...] }```
- **POST** ```/v1/step_many``` → body ```{ "session": "<id>", "actions": [<int> ...] }```
  - If ```actions``` length is 1 → broadcast to all envs.
  - Returns ```{ "session": "<id>", "envs": [StepResponse, ...] }```
- **POST** ```/v1/reset_many_combo``` → body
  - ```{ "seeds": [1,2,...], "obs_type": "Dense11", "count": 8, "session": "" }```
  - Returns a normal ManyResponse but each envs[i] contains rawForRender (a RawState) alongside the chosen obs. 
- **POST** /v1/step_many_combo → body
  - ```{ "session": "<id>", "actions": [1] }```   // broadcast or per-env
  - Returns ManyResponse with envs[i].rawForRender populated.
  - Note: ```/v1/reset_many``` and ```/v1/step_many``` do not include raw frames; use the ```*_many_combo``` endpoints when you need obs and raw per env.
- **Echo behavior**: after a snake dies in an env: ```done=true```, ```signals=[0,0,0,0,0,0]```, and the **last obs** is repeated until you ```reset_many```.

Naming note: in REST combo endpoints the envelope field is raw_for_render (snake_case).
In gRPC, StepResponse.raw_for_render shows up in protobuf-JSON as rawForRender (camelCase).
@markdown

**StepResponse (REST) shape**

```json
{
  "obs": {
    "type": "DENSE11",
    "dense": { "data": [ ... ] }
  },
  "signals": [e, d, s, t, r, T],
  "done": true,
  "score": 0,
  "length": 3,
  "death": "",
  "steps": 15,

  // Present on /reset_combo and /step_combo (and on gRPC when with_raw=true):
  "rawForRender": {
    "cols": 30,
    "rows": 30,
    "step": 15,
    "head": { "x": 16, "y": 14 },
    "dir": "RIGHT",
    "body": [ { "x": 15, "y": 14 }, { "x": 14, "y": 14 } ],
    "food": { "x": 24, "y": 5 }
  }
}
```

### StepResponse (protobuf-JSON)
```json
{
  "obs": {
    "type": "DENSE11",       // proto enum name (UPPER_SNAKE_CASE)
    "dense": { "data": [ ... ] }   // or "raw": { ... } for RawState
  },
  "signals": [e, d, s, t, r, T],
  "done": true|false,
  "score": 0,
  "length": 3,
  "death": "",               // "", "wall", "self", "timeout"
  "steps": 15
}
```

#### New convenience endpoints (obs + raw frame for rendering)

These mirror `reset`/`step`, but also return a `RawState` frame for rendering,
so clients don’t have to switch obs types. The response is an envelope:

- `POST /v1/reset_combo`
  - **Request body**:
    ```json
    { "seed": 123, "obs_type": "Dense11" }
    ```
  - **Response**:
    ```json
    {
      "step": { /* StepResponse */ },
      "raw_for_render": { /* RawState */ }
    }
    ```

- `POST /v1/step_combo`
  - **Request body**:
    ```json
    { "action": 1 }   // 0=Straight, 1=TurnRight, 2=TurnLeft
    ```
  - **Response**:
    ```json
    {
      "step": { /* StepResponse */ },
      "raw_for_render": { /* RawState */ }
    }```


- `POST /v1/reset_combo_many`
  - **Request body**:
    ```json
    {
    "seeds": [1, 2, 3, 4],     // optional; server fills if omitted or shorter than count
    "obs_type": "Dense11",
    "count": 4,                // required (>0)
    "session": ""              // optional; empty/new = create new session id, else replace that session
    }
    ```
  - **Response**:
    ```json
    {
    "session": "abc123...",    // pool id (save this and reuse in step calls)
    "envs": [
    {
      "step": { /* StepResponse (done=false, initial obs) */ },
      "raw_for_render": { /* RawState */ }
    }
    // ... repeated count times
    ]
    } ```

Step the whole pool and get both obs and raw for each env.
If actions length is 1, it’s broadcast to all envs; otherwise it must equal the pool size.
- `POST /v1/step_combo_many`
  - **Request body**:
    ```json
    {
    "session": "abc123...",    // from reset_combo_many
    "actions": [1]             // broadcast "TurnRight" to all envs (0/1/2)
    }
    ```

  - **Response:**
    ```json
    {
    "session": "abc123...",
    "envs": [
    {
      "step": { /* StepResponse (echo rules apply after death) */ },
      "raw_for_render": { /* RawState */ }
    }
    // ... one per env
    ]
    }
    ```

    

> Note: `raw_for_render` is **not** part of `StepResponse`. It only appears in the combo
> responses (and in gRPC if you set `with_raw=true` on single-env `Reset/Step`).

### Examples (curl)
**Spec**
```bash
curl -s http://localhost:8080/v1/spec | jq
```

**Start single env (Dense11)**
```bash
curl -s -X POST http://localhost:8080/v1/reset \
  -H 'content-type: application/json' \
  -d '{"seed":123, "obs_type":"Dense11"}' | jq
```

**Step (numeric action)**
```bash
curl -s -X POST http://localhost:8080/v1/step \
  -H 'content-type: application/json' \
  -d '{"action":1}' | jq
```

**Create a pool of 8 envs (Dense11)**
```bash
curl -s -X POST http://localhost:8080/v1/reset_many \
  -H 'content-type: application/json' \
  -d '{"seeds":[1,2,3,4,5,6,7,8], "obs_type":"Dense11", "count":8, "session":""}' | jq
```

**Step pool (broadcast TurnRight)**
```bash
curl -s -X POST http://localhost:8080/v1/step_many \
  -H 'content-type: application/json' \
  -d '{"session":"<paste-session>", "actions":[1]}' | jq
```

**Reset with raw (Dense11)**
```bash
curl -s -X POST http://localhost:8080/v1/reset_combo \
  -H 'content-type: application/json' \
  -d '{"seed":123, "obs_type":"Dense11"}' | jq
```
**Step with raw (numeric action)**
```bash
curl -s -X POST http://localhost:8080/v1/step_combo \
  -H 'content-type: application/json' \
  -d '{"action":1}' | jq
```

**Reset a pool with raw frames**
```bash
curl -s -X POST http://localhost:8080/v1/reset_many_combo \
  -H 'content-type: application/json' \
  -d '{"seeds":[1,2,3,4], "obs_type":"Dense11", "count":4, "session":""}' | jq
```

**Step a pool (broadcast TurnRight) with raw frames**
```bash
curl -s -X POST http://localhost:8080/v1/step_many_combo \
  -H 'content-type: application/json' \
  -d '{"session":"<paste-session>", "actions":[1]}' | jq
```

gRPC has the equivalent toggle via ```with_raw: bool``` on ```ResetRequest```/```StepRequest```; REST uses separate endpoints for simplicity.

## gRPC Overview

- Service: ```snake.v1.SnakeEnv```
- Methods: ```GetSpec```, ```Reset```, ```Step```, ```ResetMany```, ```StepMany```
- Numeric actions: ```StepRequest.action: int32``` (0/1/2)
- **Vector (pool):**:
  - ```ResetManyRequest { repeated uint64 seeds; ObsType obs_type; int32 count; string session; }```
  - ```StepManyRequest { repeated int32 actions; string session; }```
- Generate Python stubs after editing proto:
```bash
python -m grpc_tools.protoc -I api/proto \
  --python_out=clients/python --grpc_python_out=clients/python \
  api/proto/snake/v1/env.proto
```
- Generate JS (+d.ts) stubs
```bash
npm i -D grpc-tools grpc_tools_node_protoc_ts @grpc/grpc-js
npx grpc_tools_node_protoc \
  --js_out=import_style=commonjs,binary:./clients/js-grpc \
  --grpc_out=grpc_js:./clients/js-grpc \
  -I ./api/proto ./api/proto/snake/v1/env.proto
npx grpc_tools_node_protoc_ts \
  --ts_out=grpc_js:./clients/js-grpc \
  -I ./api/proto ./api/proto/snake/v1/env.proto

```


### Requesting Raw Frames via gRPC (```with_raw```)
If you’ve applied the proto update and rebuilt stubs:

- ```ResetRequest.with_raw: bool```
- ```StepRequest.with_raw: bool```
- ```StepResponse.raw_for_render: RawState``` (present if ```with_raw=true```)

Example (Python):
```python
resp = stub.ResetMany(env_pb2.ResetManyRequest(
    seeds=[1,2,3,4], obs_type=env_pb2.DENSE11, count=4, session="", with_raw=True))
print(resp.session, len(resp.envs), resp.envs[0].raw_for_render.cols)

resp = stub.StepMany(env_pb2.StepManyRequest(
    session=resp.session, actions=[1], with_raw=True))
print(resp.envs[0].raw_for_render.head.x, resp.envs[0].steps)
```
For REST, use ```/v1/reset_many_combo``` and ```/v1/step_many_combo``` to get raw frames for every env.

## Clients & Quickstarts

### Python (REST) minimal client
```clients/python/snake_rest_dense11.py``` provides:
  - ```spec()```, ```reset(seed)```, ```step(action)``` with **Dense11** and numeric actions (0/1/2).
  - No learning logic; students add DQN/NE around it.

### Node/JS (REST) minimal client
- ```clients/js/snakeRestDense11.mjs``` provides:
  - ```spec()```, ```reset(seed)```, ```step(action)``` with **Dense11** and numeric actions.
  - Requires Node 18+ (global ```fetch```). ES modules.

### C# (REST) minimal client
```clients/csharp/SnakeRestDense11Client.cs``` provides:
  - ```SpecAsync()```, ```ResetAsync(seed)```, ```StepAsync(action)``` with **Dense11** and numeric actions.

## Smoke Tests
From repo root:

### REST
```bash
python clients/python/test_rest.py
```

Covers spec, determinism (same seed → same initial obs), single-env step, pool reset/step, and **echo-after-death**.

### gRPC
```bash
# make sure Python stubs are generated (see gRPC Overview)
python clients/python/test_grpc.py
```
Covers the same for gRPC.

## Determinism
- **Same seed ⇒ same initial observation** (for each ObsType).
- Tests above verify this (raw equality for RawState, exact float list for dense encodings).

```
snake/
├─ api/
│  ├─ proto/                  # .proto files (gRPC source of truth)
│  ├─ openapi/                # (generated by gateway, optional)
│  └─ docs/                   # spec notes, changelog
├─ Snake.Core/                # core game DLL (no protobuf)
│  ├─ Game/Env.cs             # game logic (seed/reset/step/signals)
│  └─ Game/Obs/…              # encoders (Dense11, Dense28Ego, Dense32, Raycasts19), RawStateEncoder
├─ env-dotnet/                # ASP.NET server (gRPC + REST mirror)
│  ├─ Services/SnakeEnvService.cs
│  ├─ REST/Dto.cs
│  ├─ Program.cs
│  └─ Dockerfile
├─ clients/
│  ├─ python/                 # REST/gRPC minimal clients + tests
│  ├─ js/                     # REST minimal client (ESM)
│  └─ csharp/                 # REST minimal client
├─ tests/                     # (optional) conformance/determinism suites
└─ README.md
```

## FAQ / Tips
- **REST obs type casing:** use **PascalCase** (```"Dense11"```, ```"RawState"```).
- In responses, the enum shows as proto name (e.g., ```"DENSE11"```).
- **Numeric REST actions:** always send ```{ "action": 0|1|2 }```.
- **Pool “echo” behavior:** after an env is terminal, subsequent ```step_many``` returns ```done=true```, ```signals=[0,0,0,0,0,0]```, and repeats the last observation until you ```reset_many```.
- **gRPC stubs:** clients don’t read ```.proto``` at runtime—regenerate after proto changes.
- **Ports in use?** Change ```--rest-port``` / ```--grpc-port``` when starting the server.