#!/bin/bash
# 交叉编译发布 Windows exe（需 ~/.dotnet 的 .NET SDK；在 mac 上构建 win-x64）
set -euo pipefail
cd "$(dirname "$0")/.."

DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

VERSION=$("$DOTNET" msbuild CCRSwitch/CCRSwitch.csproj -getProperty:Version)
echo "==> 版本: $VERSION"

echo "==> 自包含单文件 exe（免 .NET 运行时，~70MB）"
"$DOTNET" publish CCRSwitch -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish
cp "publish/CCR-Switch.exe" "publish/CCR-Switch-${VERSION}-win64.exe"

echo "==> 框架依赖版（需目标机装 .NET Desktop Runtime，~2MB）"
"$DOTNET" publish CCRSwitch -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -o publish-small
cp "publish-small/CCR-Switch.exe" "publish/CCR-Switch-${VERSION}-win64-small.exe"

echo "==> 完成:"
ls -lh publish/*.exe | awk '{print "    " $5 "  " $9}'
echo "    首次在 Windows 上运行未签名 exe 可能需右键→属性→解除锁定"
