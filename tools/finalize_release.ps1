param(
    [Parameter(Mandatory = $true)]
    [string]$ReleasePath
)

$ErrorActionPreference = 'Stop'
$release = (Resolve-Path -LiteralPath $ReleasePath).Path
$documentDirectory = Join-Path $release 'documents'
$fileListPath = Join-Path $documentDirectory 'WriteMirror_ファイル一覧_ja.md'
$hashPath = Join-Path $release 'SHA256SUMS.txt'

$topLevel = Get-ChildItem -LiteralPath $release | Sort-Object Name
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# WriteMirror 0.5.2 ファイル一覧')
$lines.Add('')
$lines.Add('この一覧は最終提出用パッケージの構成を示します。旧ビルド、作業用画像、ダウンロード済み学習データ、仮想環境、bin/obj、審査途中の資料は含みません。')
$lines.Add('')
$lines.Add('| パス | 内容 |')
$lines.Add('|---|---|')
$lines.Add('| `WriteMirrorを起動.cmd` | 端末構成を確認し、ARM64版またはx64版を起動するランチャー |')
$lines.Add('| `最初にお読みください.txt` | 最短の起動案内と重要な制約 |')
$lines.Add('| `app/ARM64` | Snapdragon X系などのWindows 11 ARM64端末向け自己完結版 |')
$lines.Add('| `app/x64` | IntelまたはAMD搭載Windows 11端末向け自己完結版 |')
$lines.Add('| `documents` | 研究計画書、利用・技術説明書、検証結果、先行研究、README |')
$lines.Add('| `demo` | 日本語ナレーション付きデモ動画 |')
$lines.Add('| `source` | 実装、単体テスト、独立実験、生成スクリプト。生成物と学習データは除外 |')
$lines.Add('| `SHA256SUMS.txt` | ファイル改変確認用のSHA-256一覧 |')
$lines.Add('')
$lines.Add('## 集計')
$lines.Add('')
foreach ($item in $topLevel) {
    if ($item.PSIsContainer) {
        $files = Get-ChildItem -LiteralPath $item.FullName -Recurse -File
        $size = ($files | Measure-Object Length -Sum).Sum
        $lines.Add(('- `{0}`：{1}ファイル、{2:N1} MB' -f $item.Name, $files.Count, ($size / 1MB)))
    }
}
$lines | Set-Content -LiteralPath $fileListPath -Encoding UTF8

$hashLines = Get-ChildItem -LiteralPath $release -Recurse -File |
    Where-Object { $_.FullName -ne $hashPath } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($release.Length + 1).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        "$hash *$relative"
    }
$hashLines | Set-Content -LiteralPath $hashPath -Encoding UTF8

[pscustomobject]@{
    ReleasePath = $release
    FileCount = (Get-ChildItem -LiteralPath $release -Recurse -File).Count
    SizeMB = [math]::Round(((Get-ChildItem -LiteralPath $release -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
    HashEntries = $hashLines.Count
}
