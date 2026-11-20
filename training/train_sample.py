"""Bounded conservative-heuristic imitation run. This is a warm start, not PPO or a live release."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
ENCODER = "schafkopf-observation-v1"
RULES = "schafkopf-long-v1"
FEATURES = 1748
KINDS = {"bidding": (17, 128, 0.0001), "playing": (32, 256, 0.0003)}


def atomic_json(path, value):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    temporary.replace(path)


class Progress:
    def __init__(self, run):
        self.run = run
        self.started = time.monotonic()
        self.log = (run / "progress.log").open("a", encoding="utf-8", buffering=1)

    def emit(self, message, **status):
        line = f"[{datetime.now().astimezone().strftime('%H:%M:%S')}] {message}"
        print(line, flush=True)
        self.log.write(line + "\n")
        if status:
            atomic_json(self.run / "status.json", dict(status, updated_at_utc=datetime.now(timezone.utc).isoformat(),
                session_elapsed_seconds=round(time.monotonic() - self.started, 2)))

    def command(self, args, env):
        with subprocess.Popen([str(a) for a in args], cwd=ROOT, env=env, stdout=subprocess.PIPE,
                              stderr=subprocess.STDOUT, text=True, bufsize=1) as process:
            try:
                for line in process.stdout:
                    self.emit(line.rstrip())
                if process.wait() != 0:
                    raise RuntimeError(f"Command failed ({process.returncode}): {args[0]}")
            except BaseException:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                raise


def load_dataset(directory, np):
    manifest = json.loads((directory / "dataset.json").read_text())
    if (manifest["encoder_version"], manifest["rules_version"], manifest["feature_count"]) != (ENCODER, RULES, FEATURES):
        raise ValueError("Dataset encoder/rules/shape mismatch")
    dtype = np.dtype([("features", "<f4", (FEATURES,)), ("legal", "<u4"), ("action", "<i4"), ("value", "<f4")])
    data = {(split, kind): [] for split in ("training", "validation") for kind in KINDS}
    for shard in manifest["shards"]:
        path = directory / shard["file"]
        if path.parent.resolve() != directory.resolve() or path.stat().st_size != shard["rows"] * dtype.itemsize:
            raise ValueError("Invalid shard path or size")
        with path.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        if digest != shard["sha256"]:
            raise ValueError(f"Corrupt shard: {path}")
        data[shard["split"], shard["model"]].append(np.memmap(path, dtype=dtype, mode="r"))
    if any(not rows for rows in data.values()):
        raise ValueError("Need bidding and playing examples in both splits; increase --games/--validation-games")
    return manifest, data


def batches(shards, size, actions, np, seed=None):
    rng = np.random.default_rng(seed)
    order = rng.permutation(len(shards)) if seed is not None else range(len(shards))
    for index in order:
        shard = shards[index]
        indices = rng.permutation(len(shard)) if seed is not None else np.arange(len(shard))
        for first in range(0, len(shard), size):
            rows = shard[indices[first:first + size]]
            mask = ((rows["legal"][:, None] >> np.arange(actions, dtype=np.uint32)) & 1).astype(bool)
            labels = np.asarray(rows["action"], dtype=np.int32)
            if np.any(labels < 0) or np.any(labels >= actions) or not np.all(mask[np.arange(len(rows)), labels]):
                raise ValueError("Illegal demonstration label or empty mask")
            x = np.array(rows["features"], dtype=np.float32)
            values = np.array(rows["value"], dtype=np.float32)
            if not np.isfinite(x).all() or not np.isfinite(values).all():
                raise ValueError("Nonfinite demonstration")
            yield x, mask, labels, values


def build_model(kind, tf):
    actions, width, learning_rate = KINDS[kind]
    inputs = tf.keras.Input((FEATURES,), name="observation")
    hidden = tf.keras.layers.Dense(width, activation="relu")(inputs)
    hidden = tf.keras.layers.Dense(width, activation="relu")(hidden)
    outputs = [tf.keras.layers.Dense(actions, name="policy")(hidden), tf.keras.layers.Dense(1, name="value")(hidden)]
    model = tf.keras.Model(inputs, outputs, name=kind)
    model.compile(optimizer=tf.keras.optimizers.Adam(learning_rate=learning_rate, epsilon=1e-5, clipnorm=0.5))
    model.optimizer.build(model.trainable_variables)
    return model


def loss_terms(logits, predicted, mask, labels, values, tf):
    tf.debugging.assert_equal(tf.reduce_all(tf.reduce_any(mask, axis=1)), True, "Empty legal mask")
    tf.debugging.assert_equal(tf.reduce_all(tf.gather(mask, labels, axis=1, batch_dims=1)), True, "Illegal label")
    masked = tf.where(mask, logits, tf.constant(-1e9, logits.dtype))
    choices = tf.cast(tf.reduce_sum(tf.cast(mask, tf.int32), axis=1) > 1, tf.float32)
    ce = tf.nn.sparse_softmax_cross_entropy_with_logits(labels=labels, logits=masked)
    squared = tf.square(tf.squeeze(predicted, axis=1) - values)
    policy_loss = tf.math.divide_no_nan(tf.reduce_sum(ce * choices), tf.reduce_sum(choices))
    loss = policy_loss + 0.5 * tf.reduce_mean(squared)
    correct = tf.cast(tf.argmax(masked, axis=1, output_type=tf.int32) == labels, tf.float32)
    # Aggregate sums so forced moves and short batches do not inflate accuracy.
    totals = tf.stack([tf.reduce_sum(ce * choices), tf.reduce_sum(correct * choices), tf.reduce_sum(choices),
                       tf.reduce_sum(squared), tf.cast(tf.shape(labels)[0], tf.float32)])
    return loss, totals


def step_functions(model, tf):
    @tf.function(reduce_retracing=True)
    def train(x, mask, labels, values):
        with tf.GradientTape() as tape:
            logits, predicted = model(x, training=True)
            loss, totals = loss_terms(logits, predicted, mask, labels, values, tf)
        gradients = tape.gradient(loss, model.trainable_variables)
        tf.debugging.assert_all_finite(loss, "Nonfinite loss")
        for gradient in gradients:
            tf.debugging.assert_all_finite(gradient, "Nonfinite gradient")
        model.optimizer.apply_gradients(zip(gradients, model.trainable_variables))
        return totals

    @tf.function(reduce_retracing=True)
    def validate(x, mask, labels, values):
        logits, predicted = model(x, training=False)
        _, totals = loss_terms(logits, predicted, mask, labels, values, tf)
        tf.debugging.assert_all_finite(totals, "Nonfinite validation metrics")
        return totals
    return train, validate


def metrics(totals):
    ce, correct, choices, mse, rows = [float(v) for v in totals]
    return {"policy_loss": ce / max(choices, 1), "choice_accuracy": correct / max(choices, 1),
            "value_mse": mse / max(rows, 1), "choice_rows": int(choices), "rows": int(rows),
            "forced_fraction": 1 - choices / max(rows, 1)}


def save_model(model, path):
    temporary = path.with_name(path.stem + ".tmp.keras")
    model.save(temporary)
    temporary.replace(path)


def checkpoint(run, models, state, tf, np, fixture):
    directory = run / "checkpoints" / f"epoch-{state['epoch']:04d}-{uuid.uuid4().hex[:8]}"
    temporary = directory.with_name(directory.name + ".tmp")
    temporary.mkdir(parents=True)
    for kind, model in models.items():
        model.save(temporary / f"{kind}.keras")
        shutil.copyfile(run / "models" / f"best-{kind}.keras", temporary / f"best-{kind}.keras")
        restored = tf.keras.models.load_model(temporary / f"{kind}.keras")
        for expected, actual in zip(model(fixture, training=False), restored(fixture, training=False)):
            np.testing.assert_allclose(expected.numpy(), actual.numpy(), rtol=1e-5, atol=1e-6)
        if int(restored.optimizer.iterations.numpy()) != int(model.optimizer.iterations.numpy()):
            raise RuntimeError("Optimizer state failed checkpoint round-trip")
        for expected, actual in zip(model.optimizer.variables, restored.optimizer.variables, strict=True):
            np.testing.assert_array_equal(expected.numpy(), actual.numpy())
    atomic_json(temporary / "state.json", state)
    temporary.rename(directory)
    atomic_json(run / "latest.json", {"checkpoint": str(directory.relative_to(run))})


def evaluate(run, cli, env, models, config, progress, tf, np):
    progress.emit("Evaluating saved pair against the conservative teacher on separate deal seeds", phase="evaluation")
    args = [env["DOTNET_ROOT"] + "/dotnet", str(cli), "evaluate-neural", "--seeds", str(config["eval_seeds"]),
            "--master", str(config["seed"]), "--start", "1000000"]
    # stderr goes directly to the terminal; stdout is the versioned inference protocol.
    with subprocess.Popen(args, cwd=ROOT, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, bufsize=1) as process:
        report = None
        try:
            for line in process.stdout:
                message = json.loads(line)
                if message["kind"] == "hello":
                    if (message["protocol"], message["encoder_version"], message["rules_version"], message["feature_count"]) != (1, ENCODER, RULES, FEATURES):
                        raise ValueError("Inference protocol mismatch")
                elif message["kind"] == "act":
                    requests = message["requests"]
                    actions = [None] * len(requests)
                    for kind, (count, _, _) in KINDS.items():
                        indices = [i for i, request in enumerate(requests) if request["model"] == kind]
                        if not indices:
                            continue
                        x = np.asarray([requests[i]["features"] for i in indices], dtype=np.float32)
                        logits = models[kind](x, training=False)[0].numpy()
                        if not np.isfinite(logits).all() or logits.shape != (len(indices), count):
                            raise ValueError("Invalid neural output")
                        for j, i in enumerate(indices):
                            legal = sorted(requests[i]["legal"])
                            if not legal or min(legal) < 0 or max(legal) >= count:
                                raise ValueError("Invalid engine mask")
                            actions[i] = int(legal[int(np.argmax(logits[j, legal]))])
                    process.stdin.write(json.dumps({"batch_id": message["batch_id"], "actions": actions}) + "\n")
                    process.stdin.flush()
                elif message["kind"] == "result":
                    report = message["report"]
                elif message["kind"] == "progress":
                    progress.emit(f"EVAL {message['completed']}/{message['total']} candidate games "
                                  f"({100 * message['completed'] / message['total']:.1f}%)", phase="evaluation",
                                  completed_games=message["completed"], total_games=message["total"])
                else:
                    raise ValueError("Unknown inference message")
            if process.wait() != 0 or report is None:
                raise RuntimeError("Neural evaluation failed")
        except BaseException:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
            raise
    atomic_json(run / "evaluation.json", report)
    payout, difference = report["candidate_payout"], report["paired_improvement"]
    progress.emit(f"EVAL complete: {report['completed_games']} paired games; payout {payout['mean']:.2f}; "
                  f"improvement {difference['mean']:.2f} [{difference['lower95']:.2f}, {difference['upper95']:.2f}]; "
                  "zero illegal actions. Smoke result only; not promoted.", phase="evaluation-complete", **report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="New, empty run directory (default: ~/.local/share/pax-schafkopf/runs/<timestamp>)")
    parser.add_argument("--resume", type=Path, help="Resume an existing run from its latest complete epoch")
    parser.add_argument("--games", type=int, default=2000)
    parser.add_argument("--validation-games", type=int, default=256)
    parser.add_argument("--epochs", type=int, default=3, help="Total epochs, including epochs already completed on resume")
    parser.add_argument("--batch-size", type=int, default=512)
    parser.add_argument("--eval-seeds", type=int, default=32)
    parser.add_argument("--seed", type=int, default=20261120)
    parser.add_argument("--gpu-memory-mb", type=int, default=2048)
    args = parser.parse_args()
    if platform.system() != "Linux" or "microsoft" not in platform.release().lower():
        parser.error("Run inside WSL using bash training/run-sample.sh")
    if args.output and args.resume:
        parser.error("Use --output or --resume, not both")
    if not (1 <= args.games <= 100000 and 1 <= args.validation_games < 1000000 and 1 <= args.epochs <= 100 and
            1 <= args.batch_size <= 8192 and 2 <= args.eval_seeds <= 10000 and 256 <= args.gpu_memory_mb <= 8192 and 0 <= args.seed < 2**64):
        parser.error("Invalid run size, seed, batch size, epoch count, evaluation size, or GPU memory limit")
    run = (args.resume or args.output or Path.home() / ".local/share/pax-schafkopf/runs" /
           (datetime.now().strftime("sample-%Y%m%d-%H%M%S-") + uuid.uuid4().hex[:6])).resolve()
    if args.resume:
        config = json.loads((run / "run.json").read_text())
        if config["encoder_version"] != ENCODER or config["rules_version"] != RULES or config["script_version"] != 1:
            raise ValueError("Run version mismatch")
    else:
        run.mkdir(parents=True, exist_ok=False)
        config = {"script_version": 1, "encoder_version": ENCODER, "rules_version": RULES, "kind": "heuristic-imitation",
                  "games": args.games, "validation_games": args.validation_games, "batch_size": args.batch_size,
                  "eval_seeds": args.eval_seeds, "seed": args.seed, "gpu_memory_mb": args.gpu_memory_mb,
                  "created_at_utc": datetime.now(timezone.utc).isoformat()}
        atomic_json(run / "run.json", config)
    import fcntl
    with (run / "run.lock").open("w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        progress = Progress(run)
        try:
            progress.emit(f"RUN {run}", phase="preflight", run=str(run), target_epochs=args.epochs)
            progress.emit(f"Watch: bash training/watch-training.sh '{run}'")
            progress.emit(f"Resume: bash training/run-sample.sh --resume '{run}' --epochs {args.epochs}")
            env = dict(os.environ, DOTNET_ROOT=str(Path.home() / ".dotnet"),
                       PATH=str(Path.home() / ".dotnet") + os.pathsep + os.environ.get("PATH", ""), TF_CPP_MIN_LOG_LEVEL="2")
            environment_file = run / ("environment-resume-" + datetime.now().strftime("%Y%m%d-%H%M%S") + ".json" if args.resume else "environment.json")
            progress.command([sys.executable, ROOT / "training/check_environment.py", "--output", environment_file], env)
            progress.emit("Building C# rules/data/evaluation tools", phase="build")
            progress.command(["bash", ROOT / "training/dotnet.sh", "build", ROOT / "src/sk.training/sk.training.csproj",
                              "-c", "Release", "--artifacts-path", run / "build", "-v", "quiet"], env)
            cli = run / "build/bin/sk.training/release/sk.training.dll"
            dataset = run / "data"
            if not (dataset / "dataset.json").exists():
                if dataset.exists():
                    # Preserve interrupted output for inspection; never append to partial shards.
                    dataset.rename(run / ("partial-data-" + uuid.uuid4().hex[:8]))
                progress.emit(f"Generating {config['games']} training + {config['validation_games']} held-out games", phase="data")
                progress.command([env["DOTNET_ROOT"] + "/dotnet", cli, "demonstrate", "--output", dataset,
                                  "--train-games", config["games"], "--validation-games", config["validation_games"], "--master", config["seed"]], env)
            os.environ["TF_CPP_MIN_LOG_LEVEL"] = "2"
            import numpy as np
            import tensorflow as tf
            gpus = tf.config.list_physical_devices("GPU")
            if not gpus:
                raise RuntimeError("No TensorFlow GPU available")
            tf.config.set_visible_devices(gpus[0], "GPU")
            tf.config.set_logical_device_configuration(gpus[0], [tf.config.LogicalDeviceConfiguration(memory_limit=config["gpu_memory_mb"])])
            tf.keras.utils.set_random_seed(config["seed"] % 2**31)
            manifest, data = load_dataset(dataset, np)
            progress.emit(f"GPU memory cap: {config['gpu_memory_mb']} MiB; {FEATURES} private features; "
                          f"{sum(len(s) for shards in data.values() for s in shards):,} decisions", phase="training")
            models_dir = run / "models"
            models_dir.mkdir(exist_ok=True)
            if (run / "latest.json").exists():
                checkpoint_dir = run / json.loads((run / "latest.json").read_text())["checkpoint"]
                state = json.loads((checkpoint_dir / "state.json").read_text())
                models = {kind: tf.keras.models.load_model(checkpoint_dir / f"{kind}.keras") for kind in KINDS}
                for kind in KINDS:
                    # Roll back a partially completed epoch's best files along with the optimizer.
                    shutil.copyfile(checkpoint_dir / f"best-{kind}.keras", models_dir / f"best-{kind}.keras")
                progress.emit(f"Restored epoch {state['epoch']} and optimizer steps {state['optimizer_steps']}")
            else:
                models = {kind: build_model(kind, tf) for kind in KINDS}
                state = {"epoch": 0, "best": {}, "best_epoch": {}, "optimizer_steps": {kind: 0 for kind in KINDS}}
            functions = {kind: step_functions(model, tf) for kind, model in models.items()}
            fixture = np.array(data["validation", "bidding"][0]["features"][:4])
            device = models["playing"](fixture, training=False)[0].device
            if "GPU" not in device:
                raise RuntimeError(f"Policy forward pass did not use the GPU: {device}")
            progress.emit(f"Policy tensors on {device}")
            metrics_path = run / "metrics.jsonl"
            for epoch in range(state["epoch"], max(state["epoch"], args.epochs) + 1):
                if epoch == state["epoch"] and state["best"]:
                    continue
                for kind, (actions, _, _) in KINDS.items():
                    train, validate = functions[kind]
                    if epoch > 0:
                        total_batches = sum(math.ceil(len(s) / config["batch_size"]) for s in data["training", kind])
                        total = np.zeros(5, dtype=np.float64)
                        started = time.monotonic()
                        for batch, values in enumerate(batches(data["training", kind], config["batch_size"], actions, np,
                                                              config["seed"] + epoch * 2 + int(kind == "playing")), 1):
                            total += train(*values).numpy()
                            if batch == 1 or batch % 10 == 0 or batch == total_batches:
                                current = metrics(total)
                                elapsed = time.monotonic() - started
                                eta = elapsed / batch * (total_batches - batch)
                                progress.emit(f"TRAIN {kind:7} epoch {epoch}/{args.epochs} batch {batch}/{total_batches} "
                                              f"({100 * batch / total_batches:.0f}%) | loss {current['policy_loss']:.4f} | "
                                              f"choice accuracy {current['choice_accuracy']:.1%} | ETA {eta:.0f}s",
                                              phase="training", model=kind, epoch=epoch, epochs=args.epochs,
                                              batch=batch, batches=total_batches, eta_seconds=eta, **current)
                    total = np.zeros(5, dtype=np.float64)
                    for values in batches(data["validation", kind], config["batch_size"], actions, np):
                        total += validate(*values).numpy()
                    current = metrics(total)
                    progress.emit(f"VALID {kind:7} epoch {epoch}: loss {current['policy_loss']:.4f} | "
                                  f"choice accuracy {current['choice_accuracy']:.1%} | value MSE {current['value_mse']:.4f}",
                                  phase="validation", model=kind, epoch=epoch, **current)
                    with metrics_path.open("a", encoding="utf-8") as stream:
                        stream.write(json.dumps(dict(model=kind, epoch=epoch, split="validation", **current)) + "\n")
                    if kind not in state["best"] or current["policy_loss"] < state["best"][kind]:
                        state["best"][kind] = current["policy_loss"]
                        state["best_epoch"][kind] = epoch
                        save_model(models[kind], models_dir / f"best-{kind}.keras")
                state["epoch"] = epoch
                state["optimizer_steps"] = {kind: int(model.optimizer.iterations.numpy()) for kind, model in models.items()}
                checkpoint(run, models, state, tf, np, fixture)
                progress.emit(f"CHECKPOINT epoch {epoch} saved; model predictions and optimizer state verified after reload", phase="checkpoint", **state)
                if epoch >= 2 and all(epoch - state["best_epoch"][kind] >= 2 for kind in KINDS):
                    progress.emit("Early stop: neither model improved validation loss for two epochs")
                    break
            best_models = {kind: tf.keras.models.load_model(models_dir / f"best-{kind}.keras") for kind in KINDS}
            pair = {"schema_version": 1, "encoder_version": ENCODER, "rules_version": RULES, "feature_count": FEATURES,
                    "algorithm": "masked-heuristic-imitation", "teacher": manifest["teacher"], "best_epoch": state["best_epoch"],
                    "bidding": "best-bidding.keras", "playing": "best-playing.keras", "live_promoted": False,
                    "selection": "masked-argmax", "reward_scale": 100}
            atomic_json(models_dir / "pair.json", pair)
            evaluate(run, cli, env, best_models, config, progress, tf, np)
            progress.emit(f"COMPLETE: {run}\nModels: {models_dir}\nMetrics: {metrics_path}\nEvaluation: {run / 'evaluation.json'}",
                          phase="complete", completed_epochs=state["epoch"], best_epoch=state["best_epoch"], run=str(run), live_promoted=False)
            return 0
        except KeyboardInterrupt:
            progress.emit("INTERRUPTED. Resume from the last complete epoch using --resume.", phase="interrupted")
            return 130
        except Exception as error:
            progress.emit(f"FAILED: {type(error).__name__}: {error}", phase="failed", error=str(error))
            raise
        finally:
            progress.log.close()


if __name__ == "__main__":
    sys.exit(main())
