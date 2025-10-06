"""
snake_rest_dense11.py
------------------------------------------------------------
Minimal REST client for the Snake environment using Dense11.

What this gives you:
  - spec()            → GET /v1/spec
  - reset(seed)       → POST /v1/reset  (Dense11 obs)
  - step(action)      → POST /v1/step   (action string or index)
  - constants for actions and obs type
  - strongly-typed StepResult with signals, done, score, etc.

What this does NOT do:
  - no DQN / learning / replay / reward shaping
  - no evaluation logic

You can now build your own RL logic around this.

Requirements:
  pip install requests numpy

Server must be running (default REST port 8080):
  dotnet run -c Release -- --cols 40 --rows 30 --timeout-mult 150 --rest-port 8080 --grpc-port 50051
"""

from __future__ import annotations
from dataclasses import dataclass
from typing import Dict, Any, Iterable, Tuple, List, Union
import numpy as np
import requests


# -------------------------------------------------------------------
# Constants (REST expects PascalCase enum strings)
# -------------------------------------------------------------------
OBS_TYPE = "Dense11"  # we ask the server for Dense11 observations

# Action space (discrete):
# index → name and name → index helpers are provided below
ACTIONS: Tuple[str, str, str] = ("Straight", "TurnRight", "TurnLeft")


# -------------------------------------------------------------------
# Data containers
# -------------------------------------------------------------------
@dataclass
class StepResult:
    """
    Parsed result of a single environment step.

    Fields:
      obs:      np.ndarray shape (11,), dtype float32  (Dense11 vector)
      signals:  list[float], order fixed as:
                  [ eat_food, death, step_cost, toward_food, turning, timeout ]
      done:     bool
      score:    int      (apples eaten so far)
      length:   int      (snake length)
      death:    str      ("", "wall", "self", "timeout")
      steps:    int      (global step counter inside the env)
      raw:      dict     (full unmodified JSON from the server, if needed)
    """
    obs: np.ndarray
    signals: List[float]
    done: bool
    score: int
    length: int
    death: str
    steps: int
    raw: Dict[str, Any]


# -------------------------------------------------------------------
# Client
# -------------------------------------------------------------------
class SnakeRESTDense11:
    """
    Tiny REST wrapper that only handles Dense11.
    If you need RawState or other obs types, make a sibling class.

    Endpoints used:
      - POST {base}/reset   with body {"seed": <uint64>, "obs_type": "Dense11"}
      - POST {base}/step    with body {"action": "<PascalCase action>"}
      - GET  {base}/spec
    """
    def __init__(self, base_url: str = "http://localhost:8080/v1"):
        self.base = base_url.rstrip("/")

    # ---------- helpers ----------
    @staticmethod
    def action_index_to_name(i: int) -> str:
        return ACTIONS[i]

    @staticmethod
    def action_name_to_index(name: str) -> int:
        name = name.strip()
        try:
            return ACTIONS.index(name)
        except ValueError as e:
            raise ValueError(f"Unknown action '{name}'. Valid: {ACTIONS}") from e

    # ---------- endpoints ----------
    def spec(self) -> Dict[str, Any]:
        """GET /v1/spec → dict with cols/rows/timeout_mult/supported_obs/etc."""
        r = requests.get(f"{self.base}/spec", timeout=10)
        r.raise_for_status()
        return r.json()

    def reset(self, seed: int) -> np.ndarray:
        """
        POST /v1/reset → returns the Dense11 observation as np.ndarray (11,)
        NOTE: This does not compute reward. Students decide reward shaping.
        """
        body = {"seed": int(seed), "obs_type": OBS_TYPE}
        r = requests.post(f"{self.base}/reset", json=body, timeout=10)
        r.raise_for_status()
        data = r.json()

        obs = data.get("obs", {})
        dense = obs.get("dense")
        if dense is None:
            # If you see this, you probably sent the wrong obs_type (must be "Dense11")
            raise ValueError(f"Expected Dense11 payload, got: keys={list(obs.keys())}, type={obs.get('type')}")
        vec = np.asarray(dense.get("data", []), dtype=np.float32)
        if vec.shape[0] != 11:
            raise ValueError(f"Dense11 expected length 11, got {vec.shape[0]}")
        return vec

    def step(self, action: Union[int, str]) -> StepResult:
        """
        POST /v1/step → returns StepResult with Dense11 next obs + signals + flags.

        Args:
          action: either int in {0,1,2} or one of {"Straight","TurnRight","TurnLeft"}.

        Returns:
          StepResult (see class docstring).
        """
        if isinstance(action, int):
            action_name = self.action_index_to_name(action)
        else:
            action_name = action

        body = {"action": action_name}
        r = requests.post(f"{self.base}/step", json=body, timeout=10)
        r.raise_for_status()
        data = r.json()

        # Observation (Dense11)
        obs = data.get("obs", {})
        dense = obs.get("dense")
        if dense is None:
            raise ValueError(f"Expected Dense11 payload on step, got: keys={list(obs.keys())}, type={obs.get('type')}")
        vec = np.asarray(dense.get("data", []), dtype=np.float32)
        if vec.shape[0] != 11:
            raise ValueError(f"Dense11 expected length 11, got {vec.shape[0]}")

        # Signals come in a fixed order from the server (length 6)
        signals = data.get("signals", [0, 0, 0, 0, 0, 0])

        return StepResult(
            obs=vec,
            signals=signals,
            done=bool(data.get("done", False)),
            score=int(data.get("score", 0)),
            length=int(data.get("length", 0)),
            death=str(data.get("death", "")),
            steps=int(data.get("steps", 0)),
            raw=data,
        )


# -------------------------------------------------------------------
# Minimal demo (NOT learning) — safe to delete
# -------------------------------------------------------------------
if __name__ == "__main__":
    """
    Example usage:
      - Reset with a seed
      - Take a few random actions (no learning)
      - Print basic info

    Students should delete this and write their own DQN training loop.
    """
    import random

    env = SnakeRESTDense11(base_url="http://localhost:8080/v1")

    print("SPEC:", env.spec())

    # Deterministic reset
    state = env.reset(seed=123)
    print("Reset Dense11 shape:", state.shape)  # (11,)

    # Random play for 10 steps (no reward shaping here)
    for t in range(10):
        a = random.randint(0, 2)  # 0=Straight, 1=TurnRight, 2=TurnLeft
        res = env.step(a)
        print(f"t={t:02d}  a={ACTIONS[a]:>9}  len={res.length:2d}  score={res.score}  "
              f"done={res.done}  death='{res.death}'  steps={res.steps}")
        if res.done:
            break

    print("Done.")
