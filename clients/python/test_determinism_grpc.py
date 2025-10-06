import sys, math, grpc
from snake.v1 import env_pb2, env_pb2_grpc  # generated stubs
from google.protobuf import empty_pb2

OBS_TYPES = ["RAW_STATE", "DENSE32", "DENSE11", "DENSE28_EGO", "RAYCASTS19"]  # proto names here
SEED = 123456789

def enum(mod, name): return getattr(mod, name)

def canon(obs):
    which = obs.WhichOneof("payload")
    if which == "dense":
        return ("dense", tuple(float(x) for x in obs.dense.data))
    elif which == "raw":
        head = (obs.raw.head.x, obs.raw.head.y)
        food = (obs.raw.food.x, obs.raw.food.y)
        body = tuple((p.x, p.y) for p in obs.raw.body)
        return ("raw", head, obs.raw.dir, body, food, obs.raw.cols, obs.raw.rows)
    else:
        return ("none",)

def equal(a, b, tol=1e-9):
    if a[0] != b[0]:
        return False
    if a[0] == "dense":
        va, vb = a[1], b[1]
        if len(va) != len(vb): return False
        return all(math.isclose(va[i], vb[i], rel_tol=0, abs_tol=tol) for i in range(len(va)))
    return a == b

def main():
    channel = grpc.insecure_channel("localhost:50051")
    stub = env_pb2_grpc.SnakeEnvStub(channel)

    # warm up (optional)
    stub.GetSpec(empty_pb2.Empty())

    all_ok = True
    for ot in OBS_TYPES:
        req = env_pb2.ResetRequest(seed=SEED, obs_type=enum(env_pb2, ot))
        r1 = stub.Reset(req)
        r2 = stub.Reset(req)
        ok = equal(canon(r1.obs), canon(r2.obs))
        print(f"{ot:12s} same-seed equal? ->", "PASS" if ok else "FAIL")
        if not ok:
            all_ok = False
    sys.exit(0 if all_ok else 1)

if __name__ == "__main__":
    main()
