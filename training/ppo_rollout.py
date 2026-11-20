"""Batched C# rollout transport and per-seat terminal bookkeeping for one frozen policy pair."""
import json
from pathlib import Path
import subprocess
import time
import numpy as np
from ppo_math import masked_log_probs, trajectory_targets, normalize_advantages
from train_sample import ENCODER, RULES, FEATURES, ROOT, KINDS, atomic_json


class Trajectories:
    def __init__(self, kind):
        self.kind = kind
        self.pending = {}
        self.rows = []
        self.completed = set()

    def add(self, request, features, mask, action, log_probability, value):
        if not request["learner"]:
            return
        if request["model"] != self.kind or request["episode_id"] in self.completed:
            raise ValueError("Wrong learner model or completed episode")
        row = dict(episode=int(request["episode_id"]), seat=request["seat"], decision=request["decision"],
                   phase=request["phase"], features=features.copy(), mask=mask.copy(), action=action,
                   old_log_probability=log_probability, old_value=value)
        rows = self.pending.setdefault(request["episode_id"], [])
        if rows and row["decision"] <= rows[-1]["decision"]:
            raise ValueError("Duplicate or out-of-order learner decision")
        rows.append(row)

    def finish(self, episode_id, rewards):
        if episode_id in self.completed or len(rewards) != 4 or not np.isfinite(rewards).all() or sum(rewards) != 0:
            raise ValueError("Invalid or duplicate terminal rewards")
        self.completed.add(episode_id)
        rows = self.pending.pop(episode_id, [])
        for seat in range(4):
            path = [row for row in rows if row["seat"] == seat]
            if not path:
                continue
            reward, terminal, advantages, returns = trajectory_targets([row["old_value"] for row in path], rewards[seat])
            for i, row in enumerate(path):
                row.update(reward=reward[i], terminal=terminal[i], advantage=advantages[i], returns=returns[i])
                self.rows.append(row)

    def arrays(self):
        if self.pending or not self.rows:
            raise ValueError("Rollout has unfinished or no learner trajectories")
        result = {name: np.asarray([row[name] for row in self.rows], dtype=dtype) for name, dtype in
                  (("episode", np.uint64), ("seat", np.int32), ("decision", np.int32), ("phase", np.int32),
                   ("features", np.float32), ("mask", bool), ("action", np.int32), ("old_log_probability", np.float32),
                   ("old_value", np.float32), ("reward", np.float32), ("terminal", bool), ("advantage", np.float32), ("returns", np.float32))}
        result["normalized_advantage"] = normalize_advantages(result["advantage"], result["mask"].sum(axis=1) > 1)
        return result


def collect(directory, cli, env, models, versions, config, kind, start, rng, progress, tf):
    initial_rng = rng.bit_generator.state
    command = [str(Path(env["DOTNET_ROOT"]) / "dotnet"), str(cli), "rollout", "--traces", str(directory / "games.jsonl"),
               "--model", kind, "--target", str(config["decisions"]), "--pool", str(config["pool"]),
               "--master", str(config["seed"]), "--start", str(start), "--max-games", str(config["max_games"]),
               "--bidding-version", versions["bidding"], "--playing-version", versions["playing"],
               "--controlled-percent", str(config.get("controlled_percent", 0) if kind == "playing" else 0)]
    trajectories = Trajectories(kind)
    started = time.monotonic()
    last_progress = 0.0
    contracts = {}
    hello = False
    last_batch = 0
    final = None
    error_path = directory / "collector.stderr.log"
    with error_path.open("w", encoding="utf-8") as errors, subprocess.Popen(command, cwd=ROOT, env=env,
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=errors, text=True, bufsize=1) as process:
        try:
            for line in process.stdout:
                message = json.loads(line)
                if message["kind"] == "hello":
                    options = message["options"]
                    if hello or (message["protocol"], message["encoder_version"], message["rules_version"], message["feature_count"]) != (2, ENCODER, RULES, FEATURES):
                        raise ValueError("Rollout protocol mismatch")
                    if options["model"] != kind or any(options[f"{k}_version"] != versions[k] for k in KINDS):
                        raise ValueError("Rollout policy version mismatch")
                    hello = True
                    continue
                if not hello or message["kind"] != "batch":
                    raise ValueError("Unexpected rollout message")
                batch = message["batch"]
                if batch["batch_id"] != last_batch + 1:
                    raise ValueError("Duplicate/stale rollout batch")
                last_batch = batch["batch_id"]
                for completion in batch["completed"]:
                    trace = completion["trace"]
                    trajectories.finish(completion["episode_id"], trace["rewards"])
                    contract = trace["contract"]
                    key = "all-pass" if contract is None else f"{contract['game_type']}/{contract['suit']}/{contract['tout']}"
                    contracts[key] = contracts.get(key, 0) + 1
                now = time.monotonic()
                if now - last_progress >= 1 or batch["done"]:
                    elapsed = now - started
                    count = batch["learner_decisions"]
                    rate = count / max(elapsed, .001)
                    progress.emit(f"ROLLOUT {kind} {count}/{config['decisions']} decisions "
                                  f"({min(100, 100 * count / config['decisions']):.0f}%) | {batch['completed_games']} games | "
                                  f"{rate:.0f} decisions/s | {'draining complete games' if batch['draining'] else 'collecting'}",
                                  phase="rollout", model=kind, decisions=count, target=config["decisions"],
                                  games=batch["completed_games"], draining=batch["draining"])
                    last_progress = now
                if batch["done"]:
                    if batch["requests"] or batch["completed_games"] != batch["launched_games"]:
                        raise ValueError("Rollout failed to drain all games")
                    final = batch
                    break
                requests = batch["requests"]
                if not requests:
                    continue
                responses = [None] * len(requests)
                for model_kind, (action_count, _, _) in KINDS.items():
                    indices = [i for i, request in enumerate(requests) if request["model"] == model_kind]
                    if not indices:
                        continue
                    x = np.asarray([requests[i]["features"] for i in indices], dtype=np.float32)
                    if x.shape != (len(indices), FEATURES) or not np.isfinite(x).all():
                        raise ValueError("Invalid encoded rollout features")
                    mask = np.zeros((len(indices), action_count), dtype=bool)
                    for j, i in enumerate(indices):
                        request = requests[i]
                        legal = request["legal"]
                        if request["policy_version"] != versions[model_kind] or not legal or min(legal) < 0 or max(legal) >= action_count:
                            raise ValueError("Invalid version or legal mask")
                        mask[j, legal] = True
                    logits, values = models[model_kind](x, training=False)
                    if logits.shape != (len(indices), action_count) or values.shape != (len(indices), 1):
                        raise ValueError("Invalid policy/value shape")
                    log_probs = masked_log_probs(logits, mask, tf).numpy()
                    values = values.numpy()[:, 0]
                    if not np.isfinite(values).all():
                        raise ValueError("Nonfinite policy value")
                    for j, i in enumerate(indices):
                        request = requests[i]
                        probabilities = np.exp(log_probs[j].astype(np.float64))
                        probabilities /= probabilities.sum()
                        action = int(rng.choice(action_count, p=probabilities))
                        log_probability, value = float(log_probs[j, action]), float(values[j])
                        trajectories.add(request, x[j], mask[j], action, log_probability, value)
                        responses[i] = dict(episode_id=request["episode_id"], decision=request["decision"], action=action,
                                            log_probability=log_probability, value=value, policy_version=versions[model_kind])
                if any(response is None for response in responses):
                    raise ValueError("Unknown model in rollout")
                process.stdin.write(json.dumps(dict(batch_id=last_batch, actions=responses), allow_nan=False) + "\n")
                process.stdin.flush()
            if process.wait(timeout=30) != 0 or final is None:
                errors.flush()
                detail = error_path.read_text()[-4000:].strip()
                raise RuntimeError(f"Rollout process failed: {detail or 'no terminal batch'}")
        except BaseException:
            # EOF lets C# save accepted action prefixes before exiting.
            process.stdin.close()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
            raise
    arrays = trajectories.arrays()
    count = len(arrays["action"])
    if count != final["learner_decisions"] or count < config["decisions"]:
        raise ValueError("Missing learner decisions or incomplete target")
    with (directory / "trajectories.npz.tmp").open("wb") as stream:
        np.savez_compressed(stream, **arrays)
    (directory / "trajectories.npz.tmp").replace(directory / "trajectories.npz")
    metadata = dict(schema_version=1, model=kind, versions=versions, rules_version=RULES, encoder_version=ENCODER,
                    reward_scale=100, gamma=1, gae_lambda=1, seed_schedule_version="splitmix64-partition-v1",
                    master_seed=config["seed"], start_index=start, next_index=final["next_index"], split="training",
                    sampling="masked-categorical-temperature-1", numpy_rng_before=initial_rng, numpy_rng_after=rng.bit_generator.state,
                    learner_rows=count, completed_games=final["completed_games"], contracts=contracts,
                    collection_seconds=time.monotonic() - started, feature_bytes=int(arrays["features"].nbytes),
                    table_mix="half self-play; half one learner and three heuristics; frozen neural play in all seats for bidding updates",
                    curriculum="mixed-controlled-v1" if config.get("controlled_percent", 0) and kind == "playing" else "natural-deals",
                    controlled_games=final.get("controlled_games", 0), rejected_deals=final.get("rejected_deals", 0), illegal_actions=0)
    atomic_json(directory / "rollout.json", metadata)
    return arrays, metadata
