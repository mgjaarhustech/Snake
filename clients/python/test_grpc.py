#!/usr/bin/env python3
"""
gRPC smoke tests for Snake env (single + pool + determinism).

Run from repo root after regenerating Python stubs (if needed):
  export PYTHONPATH=clients/python
  python -m grpc_tools.protoc -I api/proto \
      --python_out=clients/python --grpc_python_out=clients/python \
      api/proto/snake/v1/env.proto

Then:
  python clients/python/test_grpc.py
"""

import random
import grpc
from typing import List

from snake.v1 import env_pb2 as pb
from snake.v1 import env_pb2_grpc as pb_grpc

GRPC_TARGET = "localhost:50051"

OBS_ENUMS = [
    pb.ObsType.DENSE11,
    pb.ObsType.DENSE28_EGO,
    pb.ObsType.DENSE32,
    pb.ObsType.RAYCASTS19,
    pb.ObsType.RAW_STATE,
]

DENSE_LEN = {
    pb.ObsType.DENSE11: 11,
    pb.ObsType.DENSE28_EGO: 28,
    pb.ObsType.DENSE32: 32,
    pb.ObsType.RAYCASTS19: 19,
}

def verify_dense_obs(msg: pb.StepResponse, expect_len: int):
    assert msg.obs.type in (pb.ObsType.DENSE11, pb.ObsType.DENSE28_EGO, pb.ObsType.DENSE32, pb.ObsType.RAYCASTS19)
    assert len(msg.obs.dense.data) == expect_len

def verify_rawstate_obs(msg: pb.StepResponse):
    assert msg.obs.type == pb.ObsType.RAW_STATE
    raw = msg.obs.raw
    assert raw.cols > 0 and raw.rows > 0
    assert raw.head.x >= 0 and raw.head.y >= 0
    assert raw.dir in ("RIGHT","DOWN","LEFT","UP")

def test_single(stub: pb_grpc.SnakeEnvStub, obs_type: int):
    # determinism: same seed → same first obs
    a = stub.Reset(pb.ResetRequest(seed=123, obs_type=obs_type))
    b = stub.Reset(pb.ResetRequest(seed=123, obs_type=obs_type))
    assert a.obs.type == b.obs.type

    if obs_type == pb.ObsType.RAW_STATE:
        ra, rb = a.obs.raw, b.obs.raw
        assert (ra.head.x, ra.head.y) == (rb.head.x, rb.head.y)
        assert ra.dir == rb.dir
        assert (ra.food.x, ra.food.y) == (rb.food.x, rb.food.y)
    else:
        verify_dense_obs(a, DENSE_LEN[obs_type])
        verify_dense_obs(b, DENSE_LEN[obs_type])
        assert list(a.obs.dense.data) == list(b.obs.dense.data), "initial obs not deterministic"

    # a few numeric steps
    s = a
    for _ in range(8):
        s = stub.Step(pb.StepRequest(action=random.randint(0, 2)))
        assert len(s.signals) == 6
        if obs_type == pb.ObsType.RAW_STATE:
            verify_rawstate_obs(s)
        else:
            verify_dense_obs(s, DENSE_LEN[obs_type])
        if s.done:
            break
    print(f"gRPC single OK: {pb.ObsType.Name(obs_type)} done={s.done}")

def test_pool(stub: pb_grpc.SnakeEnvStub, obs_type: int, count: int = 8, max_iters: int = 300):
    m = stub.ResetMany(pb.ResetManyRequest(seeds=list(range(1, count+1)), obs_type=obs_type, count=count, session=""))
    session = m.session
    assert len(m.envs) == count
    for e in m.envs:
        if obs_type == pb.ObsType.RAW_STATE:
            verify_rawstate_obs(e)
        else:
            verify_dense_obs(e, DENSE_LEN[obs_type])
        assert not e.done

    died = set()
    for _ in range(max_iters):
        m = stub.StepMany(pb.StepManyRequest(session=session, actions=[random.randint(0,2)]))
        for i, e in enumerate(m.envs):
            if e.done:
                died.add(i)
        if died:
            break

    if died:
        m2 = stub.StepMany(pb.StepManyRequest(session=session, actions=[0]))
        for i in died:
            e = m2.envs[i]
            assert e.done
            assert all(abs(x) < 1e-8 for x in e.signals), "echo must zero signals"
        print(f"gRPC pool OK: {pb.ObsType.Name(obs_type)} echo verified for {len(died)} dead env(s) [session={session}]")
    else:
        print(f"gRPC pool OK: {pb.ObsType.Name(obs_type)} no deaths within {max_iters} steps (echo skipped) [session={session}]")

if __name__ == "__main__":
    with grpc.insecure_channel(GRPC_TARGET) as ch:
        stub = pb_grpc.SnakeEnvStub(ch)
        spec = stub.GetSpec(pb.google_dot_protobuf_dot_empty__pb2.Empty())
        print("SPEC:", {"cols": spec.cols, "rows": spec.rows, "timeout_mult": spec.timeout_mult})

        for ot in OBS_ENUMS:
            test_single(stub, ot)

        # pool tests for a subset to keep it fast; change as you like
        for ot in (pb.ObsType.DENSE11, pb.ObsType.RAYCASTS19):
            test_pool(stub, ot, count=8)

        print("gRPC tests passed.")
