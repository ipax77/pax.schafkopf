"""Scheduling and artifact retention for bounded training sessions."""
import json
import shutil
from train_sample import atomic_json


def schedule(config, update):
    mode = config["train"]
    warmup = config.get("warmup_updates", 0)
    if mode == "staged":
        kind = "playing" if update <= warmup or (update - warmup) % 5 else "bidding"
    elif mode == "alternate":
        kind = "playing" if update % 2 else "bidding"
    else:
        kind = mode
    rollout = dict(config)
    if kind == "bidding":
        rollout["decisions"] = config.get("bidding_decisions", 0) or config["decisions"]
        rollout["controlled_percent"] = 0
    elif mode == "staged" and update <= warmup:
        rollout["controlled_percent"] = 100
    rollout["max_games"] = max(config["pool"], rollout["decisions"] * 4)
    return kind, rollout


def select_best(run, directory, report):
    if not report.get("selection_eligible", True):
        return False
    score = report["paired_improvement"]["mean"]
    path = run / "best.json"
    previous = json.loads(path.read_text()) if path.exists() else None
    if previous is not None and score <= previous["validation_score"]:
        return False
    atomic_json(path, dict(checkpoint=str(directory.relative_to(run)), update=report["ppo_update"],
                          validation_score=score, evaluation=report,
                          selection=report["selection"] if "panels" in report else "highest mean payout on fixed validation panel; not confirmation", live_promoted=False))
    return True


def prune(run, keep_checkpoints=0, keep_rollouts=0):
    # Only generated directories inside this specific run; preserve initial/latest/best.
    protected = set()
    for name in ("latest.json", "best.json"):
        path = run / name
        if path.exists():
            protected.add((run / json.loads(path.read_text())["checkpoint"]).resolve())
    if keep_checkpoints:
        paths = sorted((p for p in (run / "checkpoints").glob("update-*") if not p.name.endswith(".tmp")),
                       key=lambda p: int(p.name.split("-")[1]))
        protected.update(p.resolve() for p in paths[:1] + paths[-keep_checkpoints:])
        for path in paths:
            if path.resolve() not in protected and path.resolve().parent == (run / "checkpoints").resolve() and not path.is_symlink():
                shutil.rmtree(path)
    if keep_rollouts:
        paths = sorted((run / "rollouts").glob("update-*"), key=lambda p: int(p.name.split("-")[1]))
        for path in paths[:-keep_rollouts]:
            # Keep traces, metrics, seed/RNG/version metadata; discard only large, spent PPO arrays.
            arrays = path / "trajectories.npz"
            if arrays.exists() and arrays.resolve().parent.parent == (run / "rollouts").resolve():
                arrays.unlink()
