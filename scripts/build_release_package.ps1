param(
    [Parameter(Mandatory = $true)]
    [string]$MsixPath,
    [string]$DemoVideoPath = ""
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$releaseRoot = Join-Path $root "release"
$target = Join-Path $releaseRoot "WriteMirror_0.6.0_AI_NPU_ARM64"
$zipPath = Join-Path $releaseRoot "WriteMirror_0.6.0_AI_NPU_ARM64.zip"

if (-not $target.StartsWith($releaseRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "配布先がreleaseフォルダー外です。"
}
if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
New-Item -ItemType Directory -Force -Path $target | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $target "documents") | Out-Null

Copy-Item -LiteralPath (Resolve-Path $MsixPath) -Destination (Join-Path $target "WriteMirror_0.6.0_ARM64.msix")
Copy-Item -LiteralPath (Join-Path $root "packaging\0.6.0\Install-WriteMirror.cmd") -Destination $target
Copy-Item -LiteralPath (Join-Path $root "packaging\0.6.0\Install-WriteMirror.ps1") -Destination $target
Copy-Item -LiteralPath (Join-Path $root "packaging\0.6.0\Uninstall-WriteMirror.cmd") -Destination $target
Copy-Item -LiteralPath (Join-Path $root "packaging\0.6.0\最初にお読みください_ja.md") -Destination $target

$certificate = Get-Item "Cert:\CurrentUser\My\ECEEABF981A97A2E0CDB7AD7DA18B535D73B9976"
Export-Certificate -Cert $certificate -FilePath (Join-Path $target "WriteMirror_公開証明書.cer") -Type CERT | Out-Null

$documents = @(
    "WriteMirror_アプリ開発計画書_Vibe_Coding版.docx",
    "WriteMirror_利用・技術説明書_ja.docx",
    "WriteMirror_AI実機検証_ja.md",
    "WriteMirror_検証結果_ja.md",
    "WriteMirror_実装・性能指標_ja.md",
    "WriteMirror_プラットフォーム・データ・ライセンス_ja.md",
    "WriteMirror_先行研究・参考文献_ja.md"
)
foreach ($name in $documents) {
    Copy-Item -LiteralPath (Join-Path $root "docs\$name") -Destination (Join-Path $target "documents")
}
Copy-Item -LiteralPath (Join-Path $root "docs\assets\WriteMirror_システムアーキテクチャ_ja.png") -Destination (Join-Path $target "documents")

if ($DemoVideoPath) {
    Copy-Item -LiteralPath (Resolve-Path $DemoVideoPath) -Destination (Join-Path $target "WriteMirror_デモ動画_ja_0.6.0.mp4")
}

$hashLines = Get-ChildItem -LiteralPath $target -Recurse -File |
    Where-Object Name -ne "SHA256SUMS_ja.txt" |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($target.Length + 1).Replace("\", "/")
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
        "$hash  $relative"
    }
$hashLines | Set-Content -LiteralPath (Join-Path $target "SHA256SUMS_ja.txt") -Encoding UTF8

Compress-Archive -Path (Join-Path $target "*") -DestinationPath $zipPath -CompressionLevel Optimal
Get-Item -LiteralPath $target, $zipPath
