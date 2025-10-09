"""
Minimal REST client for Dense11 (numeric actions 0/1/2).
pip install requests numpy
"""

from __future__ import annotations
from dataclasses import dataclass
from typing import Dict, Any, List, Union
import numpy as np
import requests

OBS_TYPE = "Dense11"
ACTIONS = ("Straight", "TurnRight", "TurnLeft")

@dataclass
class StepResult:
    obs: np.ndarray
    signals: List[float]
    done: bool
    score: int
    length: int
    death: str
    steps: int
    raw: Dict[str, Any]

class SnakeRESTDense11:
    def __init__(self, base_url: str = "http://localhost:8080/v1"):
        self.base = base_url.rstrip("/")

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

    def spec(self) -> Dict[str, Any]:
        r = requests.get(f"{self.base}/spec", timeout=10)
        r.raise_for_status()
        return r.json()

    def reset(self, seed: int) -> np.ndarray:
        body = {"seed": int(seed), "obs_type": OBS_TYPE}
        r = requests.post(f"{self.base}/reset", json=body, timeout=10)
        r.raise_for_status()
        data = r.json()
        dense = data.get("obs", {}).get("dense")
        if dense is None:
            raise ValueError(f"Expected Dense11 payload, got type={data.get('obs',{}).get('type')}")
        vec = np.asarray(dense.get("data", []), dtype=np.float32)
        if vec.shape[0] != 11:
            raise ValueError(f"Dense11 expected length 11, got {vec.shape[0]}")
        return vec

    def step(self, action: Union[int, str]) -> StepResult:
        # send numeric int (0/1/2)
        if isinstance(action, int):
            idx = action
        else:
            idx = self.action_name_to_index(action)
        if not (0 <= idx <= 2):
            raise ValueError("action must be 0,1,2")

        body = {"action": idx}
        r = requests.post(f"{self.base}/step", json=body, timeout=10)
        r.raise_for_status()
        data = r.json()

        dense = data.get("obs", {}).get("dense")
        if dense is None:
            raise ValueError(f"Expected Dense11 payload on step, got type={data.get('obs',{}).get('type')}")
        vec = np.asarray(dense.get("data", []), dtype=np.float32)
        if vec.shape[0] != 11:
            raise ValueError(f"Dense11 expected length 11, got {vec.shape[0]}")

        return StepResult(
            obs=vec,
            signals=list(map(float, data.get("signals", [0,0,0,0,0,0]))),
            done=bool(data.get("done", False)),
            score=int(data.get("score", 0)),
            length=int(data.get("length", 0)),
            death=str(data.get("death", "")),
            steps=int(data.get("steps", 0)),
            raw=data,
        )
