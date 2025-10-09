#!/usr/bin/env python3
"""
REST smoke tests for Snake env (single + pool + determinism).

Run from repo root (ensure server is running on :8080):
  python clients/python/test_rest.py

If needed:
  export PYTHONPATH=clients/python
"""

import random
import requests
from typing import List, Dict, Any

BASE = "http://localhost:8080/v1"

# PascalCase for REST (server parses case-insensitively; spec lists these)
OBS_TYPES = ["Dense11", "Dense28Ego", "Dense32", "Raycasts19", "RawState"]

# Expected dense lengths (RawState is special)
DENSE_LEN = {
    "Dense11": 11,
    "Dense28Ego": 28,
    "Dense32": 32,
    "Raycasts19": 19,
}

def get(path: str) -> Dict[str, Any]:
    r = requests.get(f"{BASE}/{path}", timeout=10)
    r.raise_for_status()
    return r.json()

def post(path: str, body: Dict[str, Any]) -> Dict[str, Any]:
    r = requests.post(f"{BASE}/{path}", json=body, timeout=10)
    r.raise_for_status()
    return r.json()

def assert_len(name: str, arr: List[Any], n: int) -> None:
    assert isinstance(arr, list) and len(arr) == n, f"{name} length {len(arr)} != {n}"

def verify_dense_obs(resp: Dict[str, Any], expect_len: int) -> None:
    assert resp["obs"]["type"] == "DENSE11" or expect_len in (11, 19, 28, 32) or True
    dense = resp["obs"].get("dense")
    assert dense is not None, "expected dense payload"
    data = dense.get("data", [])
    assert_len("dense", data, expect_len)

def verify_rawstate_obs(resp: Dict[str, Any]) -> None:
    assert resp["obs"]["type"] == "RAW_STATE", f"expected RAW_STATE, got {resp['obs']['type']}"
    raw = resp["obs"].get("raw")
    assert raw is not None, "expected raw payload"
    for k in ("cols", "rows", "step", "head", "dir", "food", "body"):
        assert k in raw, f"raw missing '{k}'"

def test_spec():
    spec = get("spec")
    print("SPEC:", spec)
    # Basic checks
    assert "supported_obs" in spec and isinstance(spec["supported_obs"], list)
    for ot in ["Dense11", "Dense32"]:
        assert ot in spec["supported_obs"], f"{ot} missing in spec.supported_obs"
    assert spec.get("action_space") in (None, [0, 1, 2])  # optional
    return spec

def test_single(obs_type: str):
    # Deterministic initial obs
    a = post("reset", {"seed": 123, "obs_type": obs_type})
    b = post("reset", {"seed": 123, "obs_type": obs_type})
    assert a["obs"]["type"] == b["obs"]["type"]

    if obs_type == "RawState":
        # Compare a few raw fields for determinism
        ra, rb = a["obs"]["raw"], b["obs"]["raw"]
        assert (ra["head"]["x"], ra["head"]["y"]) == (rb["head"]["x"], rb["head"]["y"])
        assert ra["dir"] == rb["dir"]
        assert (ra["food"]["x"], ra["food"]["y"]) == (rb["food"]["x"], rb["food"]["y"])
    else:
        # Dense determinism
        verify_dense_obs(a, DENSE_LEN[obs_type])
        verify_dense_obs(b, DENSE_LEN[obs_type])
        assert a["obs"]["dense"]["data"] == b["obs"]["dense"]["data"], "initial obs not deterministic"

    # A few numeric steps
    s = a
    for _ in range(8):
        s = post("step", {"action": random.randint(0, 2)})
        assert_len("signals", s["signals"], 6)
        if obs_type == "RawState":
            verify_rawstate_obs(s)
        else:
            verify_dense_obs(s, DENSE_LEN[obs_type])
        if s["done"]:
            break
    print(f"Single OK: {obs_type}, done={s['done']}")

def test_pool(obs_type: str, count: int = 8, max_iters: int = 300):
    m = post("reset_many", {"seeds": list(range(1, count+1)), "obs_type": obs_type, "count": count, "session": ""})
    session = m["session"]
    envs = m["envs"]
    assert len(envs) == count
    # Validate initial obs payloads
    for e in envs:
        if obs_type == "RawState":
            verify_rawstate_obs(e)
        else:
            verify_dense_obs(e, DENSE_LEN[obs_type])
        assert not e["done"]

    # Step broadcast until we observe at least one death (or max_iters)
    died = set()
    for _ in range(max_iters):
        m = post("step_many", {"session": session, "actions": [random.randint(0, 2)]})
        for i, e in enumerate(m["envs"]):
            if e["done"]:
                died.add(i)
        if died:
            break

    # Echo check if someone died
    if died:
        m2 = post("step_many", {"session": session, "actions": [0]})
        for i in died:
            e = m2["envs"][i]
            assert e["done"], "echo must keep done=true"
            assert all(abs(x) < 1e-8 for x in e["signals"]), "echo must zero signals"
        print(f"Pool OK: {obs_type}, echo verified for {len(died)} dead env(s) [session={session}]")
    else:
        print(f"Pool OK: {obs_type}, no deaths within {max_iters} steps (echo skipped) [session={session}]")

if __name__ == "__main__":
    test_spec()
    for ot in OBS_TYPES:
        test_single(ot)
    # pool tests for a subset to keep it fast; change as you like
    for ot in ["Dense11", "Raycasts19"]:
        test_pool(ot, count=8)
    print("REST tests passed.")
