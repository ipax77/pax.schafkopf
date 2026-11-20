"""Warm-started, complete-game PPO. Default: ten small playing updates with a frozen bidder."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import sys
import time
import uuid
import numpy as np
from ppo_math import ppo_terms, summarize
from ppo_rollout import collect
from ppo_run import schedule, select_best, prune
from train_sample import ROOT, ENCODER, RULES, FEATURES, KINDS, Progress, atomic_json, evaluate


def digest_model(model):
    digest = hashlib.sha256()
    for value in model.get_weights():
        digest.update(str(value.shape).encode())
        digest.update(value.tobytes())
    return digest.hexdigest()


def version_pair(models):
    return {kind: digest_model(model) for kind, model in models.items()}


def validate_pair(manifest):
    if (manifest.get("encoder_version"), manifest.get("rules_version"), manifest.get("feature_count"), manifest.get("reward_scale")) != (ENCODER, RULES, FEATURES, 100):
        raise ValueError("Warm-start encoder/rules/features/reward scale mismatch")


def resolve_warm_start(path):
    path = path.expanduser().resolve()
    manifest_path = path if path.is_file() else path / "pair.json"
    if not manifest_path.is_file():
        manifest_path = path / "models/pair.json"
    if not manifest_path.is_file():
        raise ValueError("--warm-start needs an imitation run, model directory, or pair.json")
    manifest = json.loads(manifest_path.read_text())
    validate_pair(manifest)
    files = {kind: (manifest_path.parent / manifest[kind]).resolve() for kind in KINDS}
    if any(not file.is_file() for file in files.values()):
        raise ValueError("Warm-start pair is missing a model")
    return manifest_path, manifest, files


def check_models(models):
    for kind, model in models.items():
        if model.input_shape != (None, FEATURES) or model.output_shape != [(None, KINDS[kind][0]), (None, 1)]:
            raise ValueError(f"Incompatible {kind} network shape")


def checkpoint(run, models, state, rng, config, tf):
    directory = run / "checkpoints" / f"update-{state['update']:04d}-{uuid.uuid4().hex[:8]}"
    temporary = directory.with_name(directory.name + ".tmp")
    temporary.mkdir(parents=True)
    fixture = np.zeros((2, FEATURES), dtype=np.float32)
    for kind, model in models.items():
        model.save(temporary / f"{kind}.keras")
        restored = tf.keras.models.load_model(temporary / f"{kind}.keras")
        for actual, expected in zip(restored(fixture), model(fixture), strict=True):
            np.testing.assert_allclose(actual.numpy(), expected.numpy(), rtol=1e-5, atol=1e-6)
        for actual, expected in zip(restored.optimizer.variables, model.optimizer.variables, strict=True):
            np.testing.assert_array_equal(actual.numpy(), expected.numpy())
    pair = dict(schema_version=1, algorithm="complete-game-ppo", encoder_version=ENCODER, rules_version=RULES,
                feature_count=FEATURES, reward_scale=100, bidding="bidding.keras", playing="playing.keras",
                versions=version_pair(models), selection="masked-argmax", training_selection="masked-categorical-temperature-1", live_promoted=False)
    saved = dict(state, rng_state=rng.bit_generator.state, optimizer_steps={k: int(m.optimizer.iterations.numpy()) for k, m in models.items()},
                 versions=pair["versions"], rules_version=RULES, encoder_version=ENCODER, reward_scale=100, config=config)
    atomic_json(temporary / "state.json", saved)
    atomic_json(temporary / "pair.json", pair)
    temporary.rename(directory)
    atomic_json(run / "latest.json", {"checkpoint": str(directory.relative_to(run)), "update": state["update"]})
    return directory


def step_functions(model, config, tf):
    def terms(x, mask, actions, old, advantages, returns, training):
        logits, predicted = model(x, training=training)
        loss, totals = ppo_terms(logits, predicted, mask, actions, old, advantages, returns, tf,
                                config["clip"], config["entropy_coefficient"], config["value_coefficient"])
        return loss, totals, predicted[:, 0]

    @tf.function(reduce_retracing=True)
    def train(x, mask, actions, old, advantages, returns):
        with tf.GradientTape() as tape:
            loss, totals, _ = terms(x, mask, actions, old, advantages, returns, True)
        gradients = tape.gradient(loss, model.trainable_variables)
        for gradient in gradients:
            tf.debugging.assert_all_finite(gradient, "Nonfinite PPO gradient")
        norm = tf.linalg.global_norm(gradients)
        tf.debugging.assert_all_finite(norm, "Nonfinite gradient norm")
        model.optimizer.apply_gradients(zip(gradients, model.trainable_variables))
        return totals, norm

    @tf.function(reduce_retracing=True)
    def measure(x, mask, actions, old, advantages, returns):
        _, totals, predicted = terms(x, mask, actions, old, advantages, returns, False)
        return totals, predicted
    return train, measure


def update_policy(arrays, functions, kind, config, rng, update, progress):
    train, measure = functions
    count = len(arrays["action"])
    batch_size = config["batch_size"] or (2048 if kind == "playing" else 1024)
    names = ("features", "mask", "action", "old_log_probability", "normalized_advantage", "returns")
    def batch(indices):
        return tuple(arrays[name][indices] for name in names)
    started = time.monotonic()
    history = []
    for epoch in range(1, config["epochs"] + 1):
        order = rng.permutation(count)
        norms = []
        for first in range(0, count, batch_size):
            _, norm = train(*batch(order[first:first + batch_size]))
            norms.append(float(norm.numpy()))
        totals = np.zeros(7, dtype=np.float64)
        predicted = np.empty(count, dtype=np.float32)
        for first in range(0, count, batch_size):
            indices = slice(first, min(count, first + batch_size))
            values, prediction = measure(*batch(indices))
            totals += values.numpy()
            predicted[indices] = prediction.numpy()
        metrics = summarize(totals)
        variance = float(np.var(arrays["returns"]))
        metrics["explained_variance"] = 1 - float(np.var(arrays["returns"] - predicted)) / variance if variance > 1e-12 else None
        metrics["gradient_norm"] = float(np.mean(norms))
        metrics["epoch"] = epoch
        metrics["early_stop"] = metrics["approx_kl"] > 1.5 * config["target_kl"]
        history.append(metrics)
        progress.emit(f"PPO update {update} {kind} epoch {epoch}/{config['epochs']} | KL {metrics['approx_kl']:.5f} | "
                      f"clip {metrics['clip_fraction']:.1%} | entropy {metrics['entropy']:.3f} | "
                      f"value MSE {metrics['value_mse']:.4f}" + (" | KL early stop" if metrics["early_stop"] else ""),
                      phase="ppo", model=kind, update=update, **metrics)
        if metrics["early_stop"]:
            break
    phase_metrics = {}
    for phase in np.unique(arrays["phase"]):
        indices = np.flatnonzero(arrays["phase"] == phase)
        totals = np.zeros(7, dtype=np.float64)
        for first in range(0, len(indices), batch_size):
            values, _ = measure(*batch(indices[first:first + batch_size]))
            totals += values.numpy()
        phase_metrics[str(phase)] = summarize(totals)
    return dict(update=update, model=kind, epochs=history, by_phase=phase_metrics, update_seconds=time.monotonic() - started)


def compare_evaluations(baseline, current):
    if (baseline["master_seed"], baseline["start_index"], baseline["seeds"]) != (current["master_seed"], current["start_index"], current["seeds"]):
        raise ValueError("Evaluation seed panels differ")
    differences = np.asarray([new["candidate"] - old["candidate"] for old, new in zip(baseline["blocks"], current["blocks"], strict=True)])
    rng = np.random.default_rng(712367)
    bootstrap = np.asarray([rng.choice(differences, len(differences)).mean() for _ in range(2000)])
    return dict(independent_seeds=len(differences), mean=float(differences.mean()),
                lower95=float(np.quantile(bootstrap, .025)), upper95=float(np.quantile(bootstrap, .975)))


def evaluation(run, update, cli, env, models, config, progress, tf):
    directory = run / "evaluations" / f"update-{update:04d}-{uuid.uuid4().hex[:8]}"
    directory.mkdir(parents=True)
    report = evaluate(directory, cli, env, models, config, progress, tf, np)
    report["ppo_update"] = update
    report["versions"] = version_pair(models)
    baseline_path = run / "baseline-evaluation.json"
    if update == 0 and not baseline_path.exists():
        atomic_json(baseline_path, report)
    if baseline_path.exists():
        improvement = compare_evaluations(json.loads(baseline_path.read_text()), report)
        report["versus_warm_start"] = improvement
        progress.emit(f"Compared with warm start: {improvement['mean']:+.3f} raw units/deal "
                      f"[{improvement['lower95']:.3f}, {improvement['upper95']:.3f}] (validation only)")
    atomic_json(directory / "evaluation.json", report)
    atomic_json(run / "evaluation.json", report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--warm-start", type=Path, help="Imitation run directory, model directory, or pair.json")
    parser.add_argument("--resume", type=Path, help="Existing PPO run; restores optimizer, sampling RNG and next deal index")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--train", choices=["playing", "bidding", "alternate", "staged"], default="playing")
    parser.add_argument("--minutes", type=float, help="Session time budget; inherits the last session on resume, otherwise 0 (disabled). Finish the current update and final evaluation before stopping")
    parser.add_argument("--warmup-updates", type=int, default=24, help="Staged mode: controlled playing updates before repeating four play / one bidding update")
    parser.add_argument("--bidding-decisions", type=int, default=0, help="0 uses --decisions for both models")
    parser.add_argument("--controlled-percent", type=int, default=0, help="Playing curriculum share after staged warmup; bidding always uses natural deals")
    parser.add_argument("--keep-checkpoints", type=int, default=0, help="Keep this many recent checkpoints plus initial/best; 0 keeps all")
    parser.add_argument("--keep-rollouts", type=int, default=0, help="Keep large trajectory arrays for this many recent updates; 0 keeps all. Traces/metrics always retained")
    parser.add_argument("--updates", type=int, default=10, help="Total target updates, including completed updates on resume")
    parser.add_argument("--decisions", type=int, default=4096, help="Selected learner decisions before draining all active games")
    parser.add_argument("--pool", type=int, default=64)
    parser.add_argument("--epochs", type=int, default=4)
    parser.add_argument("--batch-size", type=int, default=0, help="0 selects playing=2048, bidding=1024")
    parser.add_argument("--eval-seeds", type=int, default=32)
    parser.add_argument("--eval-every", type=int, default=5)
    parser.add_argument("--seed", type=int, default=20261120)
    parser.add_argument("--start-index", type=int, default=2000000)
    parser.add_argument("--gpu-memory-mb", type=int, default=2048)
    args = parser.parse_args()
    if args.minutes is None:
        session_path = args.resume / "session.json" if args.resume else None
        args.minutes = json.loads(session_path.read_text()).get("minutes", 0) if session_path and session_path.exists() else 0
    if platform.system() != "Linux" or "microsoft" not in platform.release().lower():
        parser.error("Run in WSL using bash training/run-sample.sh ppo")
    if args.resume and (args.output or args.warm_start):
        parser.error("--resume cannot be combined with --output or --warm-start")
    if not args.resume and not args.warm_start:
        parser.error("A new PPO run requires --warm-start pointing to the imitation checkpoint pair")
    if not (0 <= args.minutes <= 1440 and 0 <= args.warmup_updates <= 100000 and 0 <= args.bidding_decisions <= 131072 and
            0 <= args.controlled_percent <= 100 and 0 <= args.keep_checkpoints <= 100000 and 0 <= args.keep_rollouts <= 100000 and
            1 <= args.updates <= 100000 and 1 <= args.decisions <= 131072 and 1 <= args.pool <= 256 and
            1 <= args.epochs <= 4 and 0 <= args.batch_size <= 8192 and 2 <= args.eval_seeds <= 10000 and
            1 <= args.eval_every <= 100000 and 0 <= args.seed < 2**64 and 0 <= args.start_index < 2**62 - 100000000 and
            256 <= args.gpu_memory_mb <= 8192):
        parser.error("Invalid bounded PPO configuration")
    run = (args.resume or args.output or Path.home() / ".local/share/pax-schafkopf/runs" /
           (datetime.now().strftime("ppo-%Y%m%d-%H%M%S-") + uuid.uuid4().hex[:6])).resolve()
    if args.resume:
        config = json.loads((run / "run.json").read_text())
        if (config.get("algorithm"), config.get("script_version"), config.get("encoder_version"), config.get("rules_version")) != ("complete-game-ppo", 1, ENCODER, RULES):
            raise ValueError("Resume version mismatch")
    else:
        source_path, source_manifest, files = resolve_warm_start(args.warm_start)
        run.mkdir(parents=True, exist_ok=False)
        (run / "warm-start").mkdir()
        source_hashes = {}
        for kind, file in files.items():
            shutil.copyfile(file, run / "warm-start" / f"{kind}.keras")
            with file.open("rb") as stream:
                source_hashes[kind] = hashlib.file_digest(stream, "sha256").hexdigest()
        atomic_json(run / "warm-start/source-pair.json", source_manifest)
        config = dict(script_version=1, algorithm="complete-game-ppo", encoder_version=ENCODER, rules_version=RULES,
                      warm_start=str(source_path), warm_start_sha256=source_hashes, train=args.train, decisions=args.decisions,
                      warmup_updates=args.warmup_updates, bidding_decisions=args.bidding_decisions, controlled_percent=args.controlled_percent,
                      keep_checkpoints=args.keep_checkpoints, keep_rollouts=args.keep_rollouts,
                      pool=args.pool, max_games=max(args.pool, args.decisions * 4), epochs=args.epochs, batch_size=args.batch_size,
                      eval_seeds=args.eval_seeds, eval_every=args.eval_every, seed=args.seed, start_index=args.start_index,
                      gpu_memory_mb=args.gpu_memory_mb, clip=.2, value_coefficient=.5, entropy_coefficient=.01,
                      target_kl=.02, gamma=1., gae_lambda=1., reward_scale=100, gradient_norm_limit=.5,
                      learning_rates={k: v[2] for k, v in KINDS.items()}, adam_epsilon=1e-5,
                      created_at_utc=datetime.now(timezone.utc).isoformat())
        atomic_json(run / "run.json", config)
    import fcntl
    with (run / "run.lock").open("w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        progress = Progress(run)
        session_start = time.monotonic()
        deadline = session_start + args.minutes * 60 if args.minutes else float("inf")
        try:
            progress.emit(f"PPO RUN {run}", phase="preflight", run=str(run), target_updates=args.updates)
            progress.emit(f"Watch: bash training/watch-training.sh '{run}'")
            progress.emit(f"Resume: bash training/run-sample.sh ppo --resume '{run}' --updates {args.updates}" +
                          (f" --minutes {args.minutes:g}" if args.minutes else ""))
            if args.minutes:
                progress.emit(f"Time budget: {args.minutes:g} minutes; stop after a complete update, then evaluate the final pair")
            atomic_json(run / "session.json", dict(minutes=args.minutes, target_updates=args.updates,
                        started_at_utc=datetime.now(timezone.utc).isoformat()))
            env = dict(os.environ, DOTNET_ROOT=str(Path.home() / ".dotnet"),
                       PATH=str(Path.home() / ".dotnet") + os.pathsep + os.environ.get("PATH", ""), TF_CPP_MIN_LOG_LEVEL="2")
            environment_path = run / ("environment-" + datetime.now().strftime("%Y%m%d-%H%M%S") + ".json")
            progress.command([sys.executable, ROOT / "training/check_environment.py", "--output", environment_path], env)
            progress.command(["bash", ROOT / "training/dotnet.sh", "build", ROOT / "src/sk.training/sk.training.csproj",
                              "-c", "Release", "--artifacts-path", run / "build", "-v", "quiet"], env)
            cli = run / "build/bin/sk.training/release/sk.training.dll"
            os.environ["TF_CPP_MIN_LOG_LEVEL"] = "2"
            import tensorflow as tf
            gpus = tf.config.list_physical_devices("GPU")
            if not gpus:
                raise RuntimeError("No TensorFlow GPU available")
            tf.config.set_visible_devices(gpus[0], "GPU")
            tf.config.set_logical_device_configuration(gpus[0], [tf.config.LogicalDeviceConfiguration(memory_limit=config["gpu_memory_mb"])])
            tf.keras.utils.set_random_seed(config["seed"] % 2**31)
            rng = np.random.default_rng(config["seed"])
            if (run / "latest.json").exists():
                directory = run / json.loads((run / "latest.json").read_text())["checkpoint"]
                state = json.loads((directory / "state.json").read_text())
                if state["config"] != config:
                    raise ValueError("Run configuration changed after checkpoint")
                models = {kind: tf.keras.models.load_model(directory / f"{kind}.keras") for kind in KINDS}
                rng.bit_generator.state = state["rng_state"]
                if version_pair(models) != state["versions"]:
                    raise ValueError("Checkpoint policy checksum mismatch")
                if {k: int(m.optimizer.iterations.numpy()) for k, m in models.items()} != state["optimizer_steps"]:
                    raise ValueError("Optimizer iteration mismatch")
                progress.emit(f"Restored update {state['update']}, next deal index {state['next_index']}, optimizer steps {state['optimizer_steps']}")
            else:
                models = {kind: tf.keras.models.load_model(run / "warm-start" / f"{kind}.keras", compile=False) for kind in KINDS}
                for kind, model in models.items():
                    # Transfer learned weights; PPO starts fresh Adam moments. PPO resumes restore these moments.
                    model.compile(optimizer=tf.keras.optimizers.Adam(learning_rate=config["learning_rates"][kind],
                                  epsilon=config["adam_epsilon"], global_clipnorm=config["gradient_norm_limit"]))
                    model.optimizer.build(model.trainable_variables)
                state = dict(update=0, next_index=config["start_index"], total_decisions=0)
                directory = checkpoint(run, models, state, rng, config, tf)
            check_models(models)
            probe = models["playing"](np.zeros((1, FEATURES), np.float32))[0]
            if "GPU" not in probe.device:
                raise RuntimeError("Policy forward pass did not use GPU")
            progress.emit(f"Policy tensors on {probe.device}; TensorFlow allocator cap {config['gpu_memory_mb']} MiB")
            functions = {kind: step_functions(model, config, tf) for kind, model in models.items()}
            report = evaluation(run, state["update"], cli, env, models, config, progress, tf)
            select_best(run, directory, report)
            last_evaluated = state["update"]
            for update in range(state["update"] + 1, args.updates + 1):
                if time.monotonic() >= deadline:
                    progress.emit("Time budget reached; preserving the last completed update and finishing evaluation")
                    break
                kind, rollout_config = schedule(config, update)
                companion = "bidding" if kind == "playing" else "playing"
                if args.minutes:
                    progress.emit(f"TIME {max(0, (deadline - time.monotonic()) / 60):.1f} minutes remaining")
                progress.emit(f"UPDATE {update}/{args.updates}: collecting fresh {kind} decisions; {companion} frozen",
                              phase="rollout", update=update, updates=args.updates, model=kind)
                work = run / "rollouts" / f"update-{update:04d}-{uuid.uuid4().hex[:8]}"
                work.mkdir(parents=True)
                versions = version_pair(models)
                arrays, metadata = collect(work, cli, env, models, versions, rollout_config, kind, state["next_index"], rng, progress, tf)
                if version_pair(models) != versions:
                    raise RuntimeError("Weights changed during collection")
                metrics = update_policy(arrays, functions[kind], kind, config, rng, update, progress)
                if digest_model(models[companion]) != versions[companion]:
                    raise RuntimeError("Frozen companion weights changed")
                atomic_json(work / "ppo.json", metrics)
                state.update(update=update, next_index=metadata["next_index"], total_decisions=state["total_decisions"] + metadata["learner_rows"],
                             last_rollout=str(work.relative_to(run)))
                directory = checkpoint(run, models, state, rng, config, tf)
                with (run / "metrics.jsonl").open("a", encoding="utf-8") as stream:
                    stream.write(json.dumps(dict(metrics, rollout=metadata), allow_nan=False) + "\n")
                progress.emit(f"CHECKPOINT update {update}/{args.updates} committed; {state['total_decisions']:,} total learner decisions",
                              phase="checkpoint", update=update, updates=args.updates, total_decisions=state["total_decisions"])
                del arrays
                if update % config["eval_every"] == 0 or update == args.updates:
                    report = evaluation(run, update, cli, env, models, config, progress, tf)
                    last_evaluated = update
                    if select_best(run, directory, report):
                        progress.emit(f"BEST validated pair: update {update}, payout {report['paired_improvement']['mean']:+.3f}")
                prune(run, config.get("keep_checkpoints", 0), config.get("keep_rollouts", 0))
            if last_evaluated != state["update"]:
                report = evaluation(run, state["update"], cli, env, models, config, progress, tf)
                select_best(run, directory, report)
            progress.emit(f"COMPLETE: {state['update']} PPO updates; latest pair: {directory / 'pair.json'}; live bots unchanged",
                          phase="complete", completed_updates=state["update"], total_decisions=state["total_decisions"],
                          latest_checkpoint=str(directory), run=str(run), live_promoted=False)
            return 0
        except KeyboardInterrupt:
            progress.emit("INTERRUPTED. Resume restores the last committed update, RNG, optimizer and deal cursor.", phase="interrupted")
            return 130
        except Exception as error:
            progress.emit(f"FAILED: {type(error).__name__}: {error}", phase="failed", error=str(error))
            raise
        finally:
            progress.log.close()


if __name__ == "__main__":
    sys.exit(main())
