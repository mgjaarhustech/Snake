import random
import grpc
from google.protobuf import empty_pb2

# Generated from your proto (after grpc_tools.protoc):
# files should live under clients/python/snake/v1/
from snake.v1 import env_pb2, env_pb2_grpc

OBS_TYPES = ["RAW_STATE", "DENSE32", "DENSE11", "DENSE28_EGO", "RAYCASTS19"]
ACTIONS = ["STRAIGHT", "TURN_RIGHT", "TURN_LEFT"]

def enum_value(mod, name):
    # In Python, enums are module-level ints (e.g., env_pb2.DENSE11)
    return getattr(mod, name)

def main():
    channel = grpc.insecure_channel("localhost:50051")
    stub = env_pb2_grpc.SnakeEnvStub(channel)

    sp = stub.GetSpec(empty_pb2.Empty())
    print("SPEC:", {"cols": sp.cols, "rows": sp.rows, "timeout_mult": sp.timeout_mult,
                    "supported_obs": list(sp.supported_obs), "reward_signals": list(sp.reward_signals)})

    for ot in OBS_TYPES:
        print("\n-- Test", ot)
        req = env_pb2.ResetRequest(seed=123, obs_type=enum_value(env_pb2, ot))
        s = stub.Reset(req)

        obs = s.obs
        which = obs.WhichOneof("payload")
        if which == "dense":
            print("dense length:", len(obs.dense.data))
        elif which == "raw":
            print("raw step:", obs.raw.step, "body len:", len(obs.raw.body))
        else:
            print("no payload?")

        # take a few steps
        for _ in range(5):
            a = enum_value(env_pb2, random.choice(ACTIONS))
            s = stub.Step(env_pb2.StepRequest(action=a))

        print("final score/len/death/steps:", s.score, s.length, s.death, s.steps)

if __name__ == "__main__":
    main()
