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

## API Surface

### Actions

- Straight (0)
- TurnRight (1)
- TurnLeft (2)

### Reward Signals

Fixed order in every StepResponse.signals:
```bash
[ eat_food, death, step_cost, toward_food, turning, timeout ]
```

### Observation Types (ObsType)

REST request enum casing: use PascalCase (RawState, Dense28Ego, …).
gRPC (Python stubs) enum casing: use ALL_CAPS (RAW_STATE, DENSE28_EGO, …).

| Name      | Length     | Description      |
| ------------- | ------------- | ------------- |
| RawState | - | Full board geometry: head, body, food, grid size, dir string. |
| Dense32 | 32 | 5×5 ring (excluding center): blocked=1, plus dir one-hot + food bits. |
| Dense11 | 11 | 3 danger flags (F,R,L), dir one-hot [L,R,U,D], food bits [L,R,U,D]. |
| Dense28Ego | 28 | 24 egocentric cells + food relative to heading [ahead, behind, left, right]. |
| Raycasts19 | 19 | 8 dirs × (frac_to_wall, frac_to_body) = 16 + 3 food features. |
| RGB | - | NOT IMPLEMENTET |

### Raycasts19 Details
Order: N, NE, E, SE, S, SW, W, NW; each contributes two floats: ```frac_to_wall```, then ```frac_to_body```.
- ```frac_to_wall = steps_to_wall_from_head / max_steps_to_wall_global_in_that_direction```
  - Axial max: cols-1 (E/W), rows-1 (N/S)
  - Diagonal max: min(cols-1, rows-1)
- frac_to_body = steps_to_first_body / same_max, or 1.0 if no body before wall.
- Food (last 3):
  - dx_norm = (food.x - head.x)/(cols-1) ∈ [-1,1]
  - dy_norm = (food.y - head.y)/(rows-1) ∈ [-1,1]
  - cheb_norm = chebyshev(head,food)/max(cols,rows) ∈ [0,1]

  ## REST (JSON) Quickstart

The REST endpoints return protobuf JSON with default values included (so keys like score, steps are always present).
Use PascalCase for enum strings in requests: RawState, Dense28Ego, Raycasts19, Straight, TurnRight, TurnLeft.

### Endpoints:
- GET /v1/spec → environment spec & supported obs list
- POST /v1/reset → { "seed": <uint64>, "obs_type": "<PascalCase>" }
- POST /v1/step → { "action": "<PascalCase>" }
- POST /v1/reset_many / POST /v1/step_many → basic vectorized mirror (single-env proxy)

### Minimal Python (REST):
```python
# clients/python/test_rest.py
import requests, random

BASE = "http://localhost:8080/v1"
OBS_TYPES = ["RawState", "Dense32", "Dense11", "Dense28Ego", "Raycasts19"]
ACTIONS   = ["Straight", "TurnRight", "TurnLeft"]

print("SPEC:", requests.get(f"{BASE}/spec").json())

for ot in OBS_TYPES:
    r = requests.post(f"{BASE}/reset", json={"seed": 123, "obs_type": ot}).json()
    obs = r["obs"]
    if obs.get("dense"):
        print(ot, "dense length:", len(obs["dense"]["data"]))
    else:
        print(ot, "raw step:", obs["raw"]["step"], "body len:", len(obs["raw"]["body"]))
    for _ in range(5):
        s = requests.post(f"{BASE}/step", json={"action": random.choice(ACTIONS)}).json()
    print("final:", s["score"], s["length"], s["death"], s["steps"])
```

### gRPC Quickstart (Python)

1. Generate stubs (only if you plan to write Python gRPC agents or after changing env.proto):
```python
# from repo root
python -m pip install grpcio grpcio-tools
python -m grpc_tools.protoc -I api/proto \
  --python_out=clients/python \
  --grpc_python_out=clients/python \
  api/proto/snake/v1/env.proto

# ensure packages exist for imports
# (create once if the folders don't already contain __init__.py)
# clients/python/snake/__init__.py
# clients/python/snake/v1/__init__.py
```
2. Minimal Python (gRPC):
```python
# clients/python/test_grpc.py
import grpc, random
from snake.v1 import env_pb2, env_pb2_grpc
from google.protobuf import empty_pb2

OBS = [env_pb2.RAW_STATE, env_pb2.DENSE32, env_pb2.DENSE11, env_pb2.DENSE28_EGO, env_pb2.RAYCASTS19]
ACT = [env_pb2.STRAIGHT, env_pb2.TURN_RIGHT, env_pb2.TURN_LEFT]

ch = grpc.insecure_channel("localhost:50051")
stub = env_pb2_grpc.SnakeEnvStub(ch)
print("SPEC:", stub.GetSpec(empty_pb2.Empty()))

for ot in OBS:
    s = stub.Reset(env_pb2.ResetRequest(seed=123, obs_type=ot))
    if s.obs.WhichOneof("payload") == "dense":
        print(ot, "dense length:", len(s.obs.dense.data))
    else:
        print(ot, "raw step:", s.obs.raw.step, "body len:", len(s.obs.raw.body))
    for _ in range(5):
        s = stub.Step(env_pb2.StepRequest(action=random.choice(ACT)))
    print("final:", s.score, s.length, s.death, s.steps)
```
***Note:*** Python gRPC uses the ***proto*** enum names (RAW_STATE, DENSE28_EGO, …), not PascalCase.

## Determinism Check

### Same seed ⇒ same initial observation (per ObsType).

- **REST:**
```bash
python clients/python/test_determinism_rest.py
```
- **gRPC:**
```bash
# ensure PYTHONPATH includes clients/python if needed
python clients/python/test_determinism_grpc.py
```
Each script calls Reset(seed=SEED) twice for every ObsType and checks the two observations match
(exact for RawState, float-equality with tiny tolerance for dense vectors).

## JS Client (Node) — Quickstart

We provide a minimal REST client for **Dense11** at: clients/js/snakeRestDense11.mjs

It exposes:
- `spec()` → GET `/v1/spec`
- `reset(seed)` → POST `/v1/reset` (returns `Float32Array(11)`)
- `step(action)` → POST `/v1/step` (returns `{ obs, signals, done, score, length, death, steps }`)
- `ACTIONS = ["Straight","TurnRight","TurnLeft"]`
- `OBS_TYPE = "Dense11"`

> Runs in **Node 18+** (built-in `fetch`). Use **.mjs** (ES modules).

### 1) Prerequisites
- Node.js **18+**: `node -v`
- Server running (REST on **:8080**)

### 2) Run the built-in demo
This just takes random actions (no learning).
```bash
node clients/js/snakeRestDense11.mjs
```
### 3) Use it in your own agent
Create my_agent.mjs (ESM) in your project root:
```js
import SnakeRestDense11, { ACTIONS } from './clients/js/snakeRestDense11.mjs';

const env = new SnakeRestDense11('http://localhost:8080/v1');

const spec = await env.spec();
console.log('SPEC:', spec);

// Deterministic reset
let state = await env.reset(123);   // Float32Array(11)

// TODO: initialize your model / replay buffer here

let done = false;
let episodeReturn = 0;

while (!done) {
  // TODO: choose action index 0..2 from your policy (e.g., epsilon-greedy)
  const action = Math.floor(Math.random() * ACTIONS.length);

  const tr = await env.step(action);
  // tr.obs -> next Dense11 (Float32Array(11))
  // tr.signals -> [eat_food, death, step_cost, toward_food, turning, timeout]

  // TODO: compute your reward (from tr.signals or your own shaping)
  // TODO: store (state, action, reward, tr.obs, tr.done) in replay
  // TODO: optimize your network on batches

  state = tr.obs;
  episodeReturn += 0; // <- replace with your reward
  done = tr.done;
}

console.log('Episode finished.');
```
Run it: node my_agent.mjs

### 5) Reward & learning are up to you
- The client does not compute rewards. Use tr.signals:
```[ eat_food, death, step_cost, toward_food, turning, timeout ]``` or derive your own reward shaping.
- Build your own DQN/A2C/evolution logic around reset/step.

## C#
1. Create a console app (once):
```bash
dotnet new console -n SnakeJsClientDemo
cd SnakeJsClientDemo
```
2. Drop the file above in clients/csharp/SnakeRestDense11Client.cs (or directly in the project folder).
If you keep the #if DEMO block, compile with -define:DEMO to run the demo.

3. Build & run:
```bash
# run the server first (REST on :8080)
dotnet run -c Release --project ../env-dotnet -- --cols 40 --rows 30 --timeout-mult 150 --rest-port 8080 --grpc-port 50051

# in another terminal, run the demo client (if DEMO block kept)
dotnet run -p SnakeJsClientDemo -c Release -property:DefineConstants=DEMO
```
## FAQ / Tips

- If REST Reset returns a dense obs when you asked for RawState, you likely sent UPPER_CASE ("RAW_STATE").
- REST expects PascalCase ("RawState").
- gRPC clients don’t read the proto at runtime; they use generated stubs.
Regenerate stubs if api/proto/snake/v1/env.proto changes.
- If ports are busy, change --rest-port / --grpc-port when starting the server.
- ResetMany/StepMany are basic mirrors for now; full vectorization can be added later.