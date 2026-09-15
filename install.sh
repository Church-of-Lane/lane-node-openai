#!/usr/bin/env bash
# Sets up Lane Node (OpenAI-compatible) on macOS / Linux:
#   1. installs the .NET 10 SDK into ~/.dotnet if it isn't already available
#   2. downloads lane-bot and packs the Lane NuGet packages into ./lane-packages
#   3. restores and builds this project
#
# Environment overrides:
#   LANE_BOT_REPO  git URL of lane-bot   (default https://github.com/ImNotJahan/lane-bot)
#   LANE_BOT_REF   branch or tag to use  (default main)
#   LANE_BOT_DIR   use an existing lane-bot checkout instead of downloading one
set -euo pipefail

LANE_BOT_REPO="${LANE_BOT_REPO:-https://github.com/ImNotJahan/lane-bot}"
LANE_BOT_REF="${LANE_BOT_REF:-main}"
LANE_VERSION="0.1.0"
LANE_PROJECTS=(Lane.Core Lane.Nodes.Protocol Lane.Node.Sdk Lane.Providers)

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PACKAGES_DIR="$ROOT/lane-packages"

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

# --- lane-bot source ---------------------------------------------------------

WORK_DIR=""
cleanup() { [ -n "$WORK_DIR" ] && rm -rf "$WORK_DIR"; }
trap cleanup EXIT

# The repository the packages should be compared against later, filled in below.
STAMP_REPO="$LANE_BOT_REPO"
STAMP_REF="$LANE_BOT_REF"
STAMP_COMMIT=""

if [ -n "${LANE_BOT_DIR:-}" ]; then
  [ -d "$LANE_BOT_DIR/Lane.Core" ] || fail "LANE_BOT_DIR ($LANE_BOT_DIR) doesn't look like a lane-bot checkout"
  SRC="$LANE_BOT_DIR"
  info "Using lane-bot from $SRC"
else
  WORK_DIR="$(mktemp -d)"
  SRC="$WORK_DIR/lane-bot"
  if have git; then
    info "Cloning $LANE_BOT_REPO ($LANE_BOT_REF)"
    git clone --quiet --depth 1 --branch "$LANE_BOT_REF" "$LANE_BOT_REPO" "$SRC"
  else
    info "Downloading $LANE_BOT_REPO ($LANE_BOT_REF)"
    download "${LANE_BOT_REPO%.git}/archive/$LANE_BOT_REF.tar.gz" "$WORK_DIR/lane-bot.tar.gz"
    mkdir -p "$SRC"
    tar -xzf "$WORK_DIR/lane-bot.tar.gz" -C "$SRC" --strip-components 1
  fi
fi

# Remember where the packages came from, so the node can say when that source has moved on.
if have git; then
  STAMP_COMMIT="$(git -C "$SRC" rev-parse HEAD 2>/dev/null || true)"

  if [ -n "${LANE_BOT_DIR:-}" ]; then
    # Follow the checkout's own branch and origin rather than the defaults.
    STAMP_REPO="$(git -C "$SRC" remote get-url origin 2>/dev/null || true)"
    STAMP_REF="$(git -C "$SRC" rev-parse --abbrev-ref HEAD 2>/dev/null || true)"
    if [ "$STAMP_REF" = "HEAD" ]; then STAMP_REF=""; fi
  elif [ -z "$STAMP_COMMIT" ]; then
    # Downloaded as a tarball: ask the remote what the ref points at.
    STAMP_COMMIT="$(git ls-remote "$LANE_BOT_REPO" "$LANE_BOT_REF" 2>/dev/null | head -n 1 | cut -f 1 || true)"
  fi
fi

# --- Lane packages -----------------------------------------------------------

info "Packing Lane packages into lane-packages/"
rm -rf "$PACKAGES_DIR"
mkdir -p "$PACKAGES_DIR"
for project in "${LANE_PROJECTS[@]}"; do
  echo "    $project"
  "$DOTNET" pack "$SRC/$project/$project.csproj" -c Release -p:Version="$LANE_VERSION" \
    -o "$PACKAGES_DIR" --nologo -v quiet
done

# Drop cached copies so restore picks up the freshly packed versions.
NUGET_CACHE="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
for project in "${LANE_PROJECTS[@]}"; do
  rm -rf "$NUGET_CACHE/$(printf '%s' "$project" | tr '[:upper:]' '[:lower:]')/$LANE_VERSION"
done

if [ -n "$STAMP_REPO" ] && [ -n "$STAMP_REF" ] && [ -n "$STAMP_COMMIT" ]; then
  cat > "$PACKAGES_DIR/source.txt" <<STAMP
repo=$STAMP_REPO
ref=$STAMP_REF
commit=$STAMP_COMMIT
STAMP
fi

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
