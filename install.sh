#!/usr/bin/env bash
# Sets up Lane Node (OpenAI-compatible) on macOS / Linux:
#   1. installs the .NET 10 SDK into ~/.dotnet if it isn't already available
#   2. restores (the Lane packages come from nuget.org) and builds this project
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31merror:\033[0m %s\n' "$*" >&2; exit 1; }
have() { command -v "$1" >/dev/null 2>&1; }

download() { # download <url> <output file>
  if have curl; then curl -fsSL "$1" -o "$2"
  elif have wget; then wget -qO "$2" "$1"
  else fail "curl or wget is required"
  fi
}

# --- .NET 10 SDK -------------------------------------------------------------

has_dotnet10() { "$1" --list-sdks 2>/dev/null | grep -q '^10\.'; }

DOTNET=""
if have dotnet && has_dotnet10 dotnet; then
  DOTNET="dotnet"
elif [ -x "$HOME/.dotnet/dotnet" ] && has_dotnet10 "$HOME/.dotnet/dotnet"; then
  DOTNET="$HOME/.dotnet/dotnet"
else
  info "Installing the .NET 10 SDK into ~/.dotnet"
  installer="$(mktemp)"
  download https://dot.net/v1/dotnet-install.sh "$installer"
  bash "$installer" --channel 10.0 --install-dir "$HOME/.dotnet"
  rm -f "$installer"
  DOTNET="$HOME/.dotnet/dotnet"
fi
if [ "$DOTNET" != "dotnet" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
fi
info "Using .NET SDK $("$DOTNET" --version)"

# --- Build -------------------------------------------------------------------

info "Building Lane Node"
"$DOTNET" build "$ROOT/Lane.Node.OpenAi.csproj" --nologo -v quiet

info "Done. Start the node with:"
if [ "$DOTNET" = "dotnet" ]; then
  echo "    dotnet run"
else
  echo "    export DOTNET_ROOT=\"\$HOME/.dotnet\" PATH=\"\$HOME/.dotnet:\$PATH\""
  echo "    dotnet run"
fi
