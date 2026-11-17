#!/bin/sh
# Unit tests of the core in Docker (dotnet SDK 8), then a compile of the core against
# .NET Framework 4.8, the API the Rust server's Mono provides. No local .NET needed.
#   sh bin/test.sh
set -e
cd "$(dirname "$0")/.."
ROOT="$(pwd -W 2>/dev/null || pwd)"
MSYS_NO_PATHCONV=1 docker run --rm -v "$ROOT:/src" -v coinspay-nuget:/root/.nuget/packages -w /src \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 mcr.microsoft.com/dotnet/sdk:8.0 \
  sh -c 'dotnet test test/CoinsPay.Tests.csproj --nologo -v q && dotnet build test/net48/Net48Check.csproj --nologo -v q'
