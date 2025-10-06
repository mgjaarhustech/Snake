# clients/python/test_rest.py
import random, requests, time

BASE = "http://localhost:8080/v1"
OBS_TYPES = ["RawState", "Dense32", "Dense11", "Dense28Ego", "Raycasts19"]
ACTIONS   = ["Straight", "TurnRight", "TurnLeft"]

def spec():
    r = requests.get(f"{BASE}/spec"); r.raise_for_status()
    return r.json()

def reset(seed, obs_type):
    r = requests.post(f"{BASE}/reset", json={"seed": seed, "obs_type": obs_type})
    r.raise_for_status(); return r.json()

def step(action):
    r = requests.post(f"{BASE}/step", json={"action": action})
    r.raise_for_status(); return r.json()

if __name__ == "__main__":
    print("SPEC:", spec())

    for ot in OBS_TYPES:
        print("\n-- Test", ot)
        s = reset(seed=123, obs_type=ot)
        obs = s["obs"]
        if obs.get("dense") is not None:
            print("dense length:", len(obs["dense"]["data"]))
        elif obs.get("raw") is not None:
            rs = obs["raw"]
            print("raw step:", rs.get("step"), "body len:", len(rs.get("body", [])))
        else:
            print("No obs payload? got:", obs)

        for _ in range(5):
            s = step(random.choice(ACTIONS))
        print("final score/len/death/steps:", s["score"], s["length"], s["death"], s["steps"])
        time.sleep(0.05)
