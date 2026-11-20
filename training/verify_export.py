"""Compare exported C# CPU inference against TensorFlow on saved rollout observations."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import numpy as np
from train_sample import ENCODER, RULES, FEATURES, KINDS, atomic_json


def imitation_observations(run, kind, samples, rng):
    """Use the frozen companion's real validation observations when PPO has no rows for it."""
    config_path = run / "run.json"
    config = json.loads(config_path.read_text()) if config_path.exists() else {}
    source = config.get("warm_start")
    if not source:
        return None, None
    directory = Path(source).parent.parent / "data"
    manifest_path = directory / "dataset.json"
    if not manifest_path.exists():
        return None, None
    manifest = json.loads(manifest_path.read_text())
    if (manifest["encoder_version"], manifest["rules_version"], manifest["feature_count"]) != (ENCODER, RULES, FEATURES):
        raise ValueError("Incompatible imitation observations")
    dtype = np.dtype([("features", "<f4", (FEATURES,)), ("legal", "<u4"), ("action", "<i4"), ("value", "<f4")])
    for shard in manifest["shards"]:
        if shard["split"] != "validation" or shard["model"] != kind:
            continue
        path = directory / shard["file"]
        if path.resolve().parent != directory.resolve() or path.stat().st_size != shard["rows"] * dtype.itemsize:
            raise ValueError("Invalid imitation observation shard")
        rows = np.memmap(path, dtype=dtype, mode="r")
        selected = rows[rng.permutation(len(rows))[:samples]]
        mask = ((selected["legal"][:, None] >> np.arange(KINDS[kind][0], dtype=np.uint32)) & 1).astype(bool)
        return np.array(selected["features"]), mask
    return None, None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run", type=Path)
    parser.add_argument("export", type=Path)
    parser.add_argument("--cli", type=Path, required=True)
    parser.add_argument("--samples", type=int, default=256)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if not 1 <= args.samples <= 10000:
        parser.error("--samples must be between 1 and 10000")
    os.environ["CUDA_VISIBLE_DEVICES"] = "-1"
    os.environ["TF_CPP_MIN_LOG_LEVEL"] = "3"
    import tensorflow as tf
    manifest = json.loads((args.export / "pair.json").read_text())
    source = Path(manifest["source_pair"])
    source_manifest = json.loads(source.read_text())
    rng = np.random.default_rng(72)
    records, expected, masks = [], [], []
    actual_rows = {}
    for kind, (actions, _, _) in KINDS.items():
        x = mask = None
        for data_path in sorted(args.run.glob("rollouts/*/trajectories.npz"), reverse=True):
            metadata = json.loads((data_path.parent / "rollout.json").read_text())
            if metadata["model"] != kind:
                continue
            with np.load(data_path) as data:
                indices = rng.permutation(len(data["action"]))[:args.samples]
                x, mask = data["features"][indices], data["mask"][indices]
            break
        if x is None:
            x, mask = imitation_observations(args.run, kind, args.samples, rng)
        actual_rows[kind] = 0 if x is None else len(x)
        if x is None:
            x = (rng.random((args.samples, FEATURES)) < .1).astype(np.float32)
            mask = np.ones((args.samples, actions), bool)
        model = tf.keras.models.load_model(source.parent / source_manifest[kind], compile=False)
        logits, values = model(x, training=False)
        for i in range(len(x)):
            records.append(json.dumps(dict(model=kind, features=x[i].tolist())))
            expected.append((logits.numpy()[i], float(values.numpy()[i, 0])))
            masks.append(mask[i])
    process = subprocess.run([str(Path.home() / ".dotnet/dotnet"), str(args.cli), "probe-policy", str(args.export / "pair.json")],
                             input="\n".join(records) + "\n", text=True, capture_output=True, check=True)
    responses = [json.loads(line) for line in process.stdout.splitlines()]
    max_logit = max_value = 0.
    disagreements = 0
    for response, (logits, value), mask in zip(responses, expected, masks, strict=True):
        actual = np.asarray(response["logits"])
        np.testing.assert_allclose(actual, logits, rtol=1e-4, atol=1e-4)
        np.testing.assert_allclose(response["value"], value, rtol=1e-4, atol=1e-4)
        max_logit = max(max_logit, float(np.max(np.abs(actual - logits))))
        max_value = max(max_value, abs(response["value"] - value))
        legal = np.flatnonzero(mask)
        disagreements += int(legal[np.argmax(actual[legal])] != legal[np.argmax(logits[legal])])
    if disagreements:
        raise ValueError(f"{disagreements} masked-argmax disagreements")
    result = dict(pair_id=manifest["pair_id"], samples=len(records), real_observation_rows=actual_rows,
                  max_absolute_logit_error=max_logit, max_absolute_value_error=max_value, masked_action_disagreements=disagreements)
    atomic_json(args.output, result)
    print(json.dumps(result))


if __name__ == "__main__":
    main()
