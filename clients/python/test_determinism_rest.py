import requests, math, sys

BASE = "http://localhost:8080/v1"
OBS_TYPES = ["RawState", "Dense32", "Dense11", "Dense28Ego", "Raycasts19"]
SEED = 123456789

def reset(seed, obs_type):
    r = requests.post(f"{BASE}/reset", json={"seed": seed, "obs_type": obs_type})
    r.raise_for_status()
    return r.json()["obs"]  # protobuf JSON (we enabled default values)

def canon(obs):
    """Canonicalize obs payload to a tuple for equality."""
    # oneof: either 'raw' or 'dense'
    if obs.get("dense") is not None:
        data = tuple(float(x) for x in obs["dense"]["data"])
        return ("dense", data)
    elif obs.get("raw") is not None:
        raw = obs["raw"]
        head = (raw["head"]["x"], raw["head"]["y"])
        food = (raw["food"]["x"], raw["food"]["y"])
        body = tuple((p["x"], p["y"]) for p in raw.get("body", []))
        return ("raw", head, raw["dir"], body, food, raw["cols"], raw["rows"])
    else:
        return ("none",)

def equal(a, b, tol=1e-9):
    if a[0] != b[0]:
        return False
    if a[0] == "dense":
        va, vb = a[1], b[1]
        if len(va) != len(vb): return False
        return all(math.isclose(va[i], vb[i], rel_tol=0, abs_tol=tol) for i in range(len(va)))
    return a == b  # raw/none: exact match

def main():
    all_ok = True
    for ot in OBS_TYPES:
        obs1 = reset(SEED, ot)
        obs2 = reset(SEED, ot)
        a, b = canon(obs1), canon(obs2)
        ok = equal(a, b)
        print(f"{ot:12s} same-seed equal? ->", "PASS" if ok else "FAIL")
        if not ok:
            all_ok = False
    sys.exit(0 if all_ok else 1)

if __name__ == "__main__":
    main()
