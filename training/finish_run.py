"""Freeze a completed run's best pair, verify native parity, and run the predeclared held-out diagnostics."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import sys
from train_sample import ROOT, atomic_json


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run", type=Path)
    parser.add_argument("--output", type=Path, required=True, help="New directory for exported models and reports")
    parser.add_argument("--quick", action="store_true", help="32-seed validation-only pipeline test, not the held-out protocol")
    args = parser.parse_args()
    run, output = args.run.resolve(), args.output.resolve()
    # Do not inspect a moving target: selection is frozen before any held-out result.
    if json.loads((run / "status.json").read_text())["phase"] != "complete":
        raise ValueError("Wait for training to complete before held-out evaluation")
    import fcntl
    with (run / "run.lock").open("r") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        output.mkdir(parents=True, exist_ok=False)
        protocol = json.loads((ROOT / "training/results/serious-20261120-protocol.json").read_text())
        split = "Validation" if args.quick else protocol["held_out_split"]
        start = 9000000 if args.quick else protocol["held_out_start_index"]
        config = json.loads((run / "run.json").read_text())
        best = json.loads((run / "best.json").read_text())
        initial = next(p for p in (run / "checkpoints").glob("update-0000-*")
                       if not p.name.endswith(".tmp") and (p / "state.json").is_file())
        atomic_json(output / "selection.json", dict(best, selected_before_holdout_at_utc=datetime.now(timezone.utc).isoformat()))
        atomic_json(output / "protocol.json", dict(protocol, quick=args.quick, actual_training_run=str(run), actual_master_seed=config["seed"],
                                                  actual_evaluation_split=split, actual_start_index=start))
        env = dict(os.environ, CUDA_VISIBLE_DEVICES="-1", TF_CPP_MIN_LOG_LEVEL="3")
        def command(arguments):
            subprocess.run([str(a) for a in arguments], cwd=ROOT, env=env, check=True)
        command(["bash", ROOT / "training/dotnet.sh", "build", ROOT / "src/sk.training/sk.training.csproj", "-c", "Release",
                 "--artifacts-path", output / "build", "-v", "quiet"])
        cli = output / "build/bin/sk.training/release/sk.training.dll"
        for name, source in (("best", run / best["checkpoint"]), ("initial", initial)):
            command([sys.executable, ROOT / "training/export_policy.py", source, "--output", output / name])
        command([sys.executable, ROOT / "training/verify_export.py", run, output / "best", "--cli", cli, "--output", output / "parity.json"])
        command(["bash", ROOT / "training/dotnet.sh", cli, "audit-policy", output / "best/pair.json", output / "latency.json"])
        reports = {}
        suites = [(name, None) for name in ("conservative", "balanced")]
        suites += [("conservative", action) for action in protocol["controlled_contract_actions"]]
        for opponents, contract in suites:
            name = f"natural-{opponents}" if contract is None else f"controlled-{contract}"
            path = output / f"{name}.json"
            seeds = 32 if args.quick else protocol["held_out_natural_seeds_per_lineup" if contract is None else "controlled_seeds_per_contract"]
            command(["bash", ROOT / "training/dotnet.sh", cli, "evaluate", "--output", path, "--seeds", seeds,
                     "--master", config["seed"], "--start", start, "--split", split,
                     "--candidate", f"neural:{output / 'best/pair.json'}", "--reference", f"neural:{output / 'initial/pair.json'}",
                     "--opponents", ",".join([opponents] * 3)] + ([] if contract is None else ["--contract", contract]))
            report = json.loads(path.read_text())
            reports[name] = dict(candidate=report["CandidatePayout"], improvement_over_warm_start=report["PairedImprovement"],
                                 games=report["CompletedGames"], eligible_seeds=len(report["Blocks"]),
                                 candidate_totals=report["CandidateTotals"])
        summary = dict(run=str(run), selected_update=best["update"], selection_validation_score=best["validation_score"],
                       quick=args.quick, reports=reports, exported_pair=str(output / "best/pair.json"), experimental=True,
                       live_promoted=False, completed_at_utc=datetime.now(timezone.utc).isoformat())
        atomic_json(output / "summary.json", summary)
        # A convenient pointer, without altering any running API's immutable weights.
        atomic_json(run / "playable.json", dict(directory=str(output / "best"), report=str(output / "summary.json"), update=best["update"]))
        print(f"Ready for local play: bash training/play-trained.sh '{output / 'best'}'")
        print(f"Held-out results: {output / 'summary.json'}")


if __name__ == "__main__":
    main()
