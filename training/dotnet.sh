#!/usr/bin/env bash
set -euo pipefail
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
if [[ ! -x "$DOTNET_ROOT/dotnet" ]]; then
  echo "Missing selected SDK executable: $DOTNET_ROOT/dotnet" >&2
  exit 1
fi
actual_sdk="$("$DOTNET_ROOT/dotnet" --version)"
if [[ "$actual_sdk" != "10.0.404" ]]; then
  echo "Expected SDK 10.0.404; resolved $actual_sdk from $DOTNET_ROOT/dotnet" >&2
  exit 1
fi
exec "$DOTNET_ROOT/dotnet" "$@"
