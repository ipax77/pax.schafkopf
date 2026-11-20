"""Read-only WSL preflight; saves evidence even when a prerequisite fails."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import platform
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]


def command(args, env=None, timeout=120):
    try:
        result = subprocess.run(args, cwd=ROOT, env=env, text=True,
                                capture_output=True, timeout=timeout, check=False)
        return {"command": [str(a) for a in args], "exit_code": result.returncode,
                "stdout": result.stdout.strip(), "stderr": result.stderr.strip()}
    except (OSError, subprocess.TimeoutExpired) as error:
        return {"command": [str(a) for a in args], "exit_code": -1, "error": str(error)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--python", default=sys.executable, help="Training venv Python executable")
    parser.add_argument("--core-only", action="store_true", help="Skip TensorFlow/GPU readiness checks")
    args = parser.parse_args()
    dotnet = Path.home() / ".dotnet" / "dotnet"
    env = dict(os.environ, DOTNET_ROOT=str(dotnet.parent),
               PATH=str(dotnet.parent) + os.pathsep + os.environ.get("PATH", ""))
    checks = {
        "sdk": command([dotnet, "--version"], env),
        "dotnet_info": command([dotnet, "--info"], env),
        "runtimes": command([dotnet, "--list-runtimes"], env),
        "git_commit": command(["git", "rev-parse", "HEAD"]),
        "git_status": command(["git", "status", "--porcelain"]),
        "python": command([args.python, "-c",
            "import json,sys; print(json.dumps(dict(version=sys.version, executable=sys.executable, "
            "venv=sys.prefix != sys.base_prefix)))"]),
    }
    errors = []
    if platform.system() != "Linux" or "microsoft" not in platform.release().lower():
        errors.append("Run this preflight inside WSL.")
    if checks["sdk"].get("stdout") != "10.0.404":
        errors.append("Selected ~/.dotnet/dotnet must resolve SDK 10.0.404; no PATH fallback is used.")
    if "Microsoft.NETCore.App 10." not in checks["runtimes"].get("stdout", ""):
        errors.append("The selected installation needs the .NET 10 runtime.")
    if checks["python"]["exit_code"] != 0:
        errors.append("Python probe failed.")
    if not args.core_only:
        checks["gpu"] = command(["nvidia-smi", "--query-gpu=name,memory.total,driver_version",
                                 "--format=csv,noheader"])
        checks["tensorflow"] = command([args.python, "-c", "\n".join([
            "import json, sys, tensorflow as tf",
            "gpus = tf.config.list_physical_devices('GPU')",
            "for gpu in gpus: tf.config.experimental.set_memory_growth(gpu, True)",
            "if not gpus: raise RuntimeError('No TensorFlow GPU discovered')",
            "tf.config.set_soft_device_placement(False)",
            "with tf.device('/GPU:0'): value = tf.reduce_sum(tf.matmul(tf.ones((32,32)), tf.ones((32,32))))",
            "print(json.dumps(dict(version=tf.__version__, gpus=[g.name for g in gpus], device=value.device, value=float(value.numpy()))))",
            "assert tf.__version__ == '2.21.0', 'Expected pinned TensorFlow 2.21.0'",
            "assert sys.prefix != sys.base_prefix, 'Use a dedicated Python virtual environment'",
            "assert sys.version_info[:2] == (3, 12), 'Expected Python 3.12'",
            "assert float(value.numpy()) == 32768.0",
        ])])
        checks["packages"] = command([args.python, "-m", "pip", "freeze", "--all"])
        checks["pip_check"] = command([args.python, "-m", "pip", "check"])
        for name in ("gpu", "tensorflow", "packages", "pip_check"):
            if checks[name]["exit_code"] != 0:
                errors.append(f"{name} check failed; see its captured diagnostics.")
    report = {"schema_version": 1, "checked_at_utc": datetime.now(timezone.utc).isoformat(),
              "platform": platform.platform(), "scope": "core" if args.core_only else "full",
              "ready": not errors, "errors": errors, "checks": checks}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"{'PASS' if not errors else 'FAIL'} ({report['scope']}): {args.output}")
    for error in errors:
        print(error, file=sys.stderr)
    return int(bool(errors))


if __name__ == "__main__":
    sys.exit(main())
