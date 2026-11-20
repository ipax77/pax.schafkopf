"""Export a frozen pair for dependency-free C# CPU inference; no training state is modified."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import uuid
import numpy as np
from train_sample import ENCODER, RULES, FEATURES, KINDS, atomic_json


def source_pair(source):
    source = source.expanduser().resolve()
    if source.is_dir() and (source / "best.json").is_file():
        return source / json.loads((source / "best.json").read_text())["checkpoint"] / "pair.json"
    if source.is_dir() and (source / "models/pair.json").is_file():
        return source / "models/pair.json"
    return source / "pair.json" if source.is_dir() else source


def export(source, output, tf):
    requested = source
    # Capture both immutable Keras files before loading. A trainer may select a new best
    # and prune the previous pair while this reader starts; retry the whole snapshot.
    for attempt in range(3):
        try:
            source = source_pair(requested)
            manifest = json.loads(source.read_text())
            snapshots = {kind: (source.parent / manifest[kind]).read_bytes() for kind in KINDS}
            break
        except FileNotFoundError:
            if attempt == 2:
                raise
    if (manifest.get("encoder_version"), manifest.get("rules_version"), manifest.get("feature_count"), manifest.get("reward_scale")) != (ENCODER, RULES, FEATURES, 100):
        raise ValueError("Incompatible source pair")
    output = output.expanduser().resolve()
    if output.exists():
        raise ValueError("Export directory must be new; a running server pins its original pair")
    temporary = output.with_name(output.name + ".tmp-" + uuid.uuid4().hex[:8])
    temporary.mkdir(parents=True)
    hashes = {}
    for kind, (actions, width, _) in KINDS.items():
        snapshot = temporary / f"source-{kind}.keras"
        snapshot.write_bytes(snapshots[kind])
        model = tf.keras.models.load_model(snapshot, compile=False)
        snapshot.unlink()
        dense = [layer for layer in model.layers if isinstance(layer, tf.keras.layers.Dense)]
        hidden = [layer for layer in dense if layer.name not in ("policy", "value")]
        if len(dense) != 4 or len(hidden) != 2 or any(layer.get_config()["activation"] != "relu" for layer in hidden):
            raise ValueError("Unsupported model architecture")
        if model.input_shape != (None, FEATURES) or model.output_shape != [(None, actions), (None, 1)]:
            raise ValueError("Incompatible model shape")
        layers = hidden + [model.get_layer("policy"), model.get_layer("value")]
        if any(layer.get_config()["activation"] != "linear" for layer in layers[2:]):
            raise ValueError("Policy/value heads must be linear")
        expected_shapes = [(FEATURES, width), (width, width), (width, actions), (width, 1)]
        path = temporary / f"{kind}.bin"
        with path.open("wb") as stream:
            stream.write(struct.pack("<5i", 0x31504b53, FEATURES, width, width, actions))
            for layer, shape in zip(layers, expected_shapes, strict=True):
                kernel, bias = layer.get_weights()
                if kernel.shape != shape or bias.shape != (shape[1],) or not np.isfinite(kernel).all() or not np.isfinite(bias).all():
                    raise ValueError("Invalid dense weights")
                stream.write(kernel.T.astype("<f4").tobytes())
                stream.write(bias.astype("<f4").tobytes())
        hashes[kind] = hashlib.sha256(path.read_bytes()).hexdigest()
    pair_id = hashlib.sha256((hashes["bidding"] + hashes["playing"]).encode()).hexdigest()
    atomic_json(temporary / "pair.json", dict(schema_version=1, native_format="dense-f32-v1", encoder_version=ENCODER,
        rules_version=RULES, feature_count=FEATURES, reward_scale=100, selection="masked-argmax", pair_id=pair_id,
        bidding="bidding.bin", playing="playing.bin", bidding_sha256=hashes["bidding"], playing_sha256=hashes["playing"],
        source_pair=str(source), source_versions=manifest.get("versions"), experimental=True, live_promoted=False))
    temporary.rename(output)
    return output / "pair.json"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="PPO run (best validated pair), imitation run, checkpoint directory, or pair.json")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    os.environ["CUDA_VISIBLE_DEVICES"] = "-1"
    os.environ["TF_CPP_MIN_LOG_LEVEL"] = "3"
    import tensorflow as tf
    print(export(args.source, args.output, tf))


if __name__ == "__main__":
    main()
