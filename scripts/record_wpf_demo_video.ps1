param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,
    [string]$OutputPath = "artifacts\video\WriteMirror_デモ動画_ja_0.6.0.mp4",
    [string]$SubtitlePath = "docs\WriteMirror_デモ字幕_ja.srt"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class DemoWindow
{
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hwnd, int command);
}
"@

function Find-ElementByName {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [int]$TimeoutSeconds = 10
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $element = $Root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)
        if ($null -ne $element) {
            return $element
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "要素が見つかりません: $Name"
}

function Find-ElementById {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$AutomationId
    )
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Invoke-Element {
    param([System.Windows.Automation.AutomationElement]$Element)
    $pattern = $Element.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 300
}

function Get-AiReadyCount {
    param([string]$LogPath)
    if (-not (Test-Path -LiteralPath $LogPath)) {
        return 0
    }
    return @(Select-String -LiteralPath $LogPath -SimpleMatch "`tAiReady`t").Count
}

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$resolvedOutput = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
$resolvedSubtitle = (Resolve-Path -LiteralPath $SubtitlePath).Path
$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$rawPath = Join-Path $outputDirectory "WriteMirror_demo_raw.mp4"
$captureLog = Join-Path $outputDirectory "ffmpeg-capture.log"
$ffmpeg = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Gyan.FFmpeg*" -Recurse -Filter ffmpeg.exe | Select-Object -First 1
if ($null -eq $ffmpeg) {
    throw "FFmpegが見つかりません"
}

$logPath = Join-Path $env:LOCALAPPDATA "WriteMirror\windows-ml-readiness.log"
$readyBaseline = Get-AiReadyCount $logPath
$app = Start-Process -FilePath $resolvedExecutable -PassThru
$capture = $null

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 200
        $app.Refresh()
    } while ($app.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($app.MainWindowHandle -eq 0) {
        throw "WriteMirrorのウィンドウを取得できません"
    }
    [DemoWindow]::ShowWindowAsync($app.MainWindowHandle, 3) | Out-Null
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)

    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 200
        $readyCount = Get-AiReadyCount $logPath
    } while ($readyCount -le $readyBaseline -and [DateTime]::UtcNow -lt $deadline)
    if ($readyCount -le $readyBaseline) {
        throw "AIモデルの準備が60秒以内に完了しませんでした"
    }

    $captureArguments = @(
        "-y", "-f", "gdigrab", "-framerate", "20", "-i", "title=WriteMirror",
        "-t", "55", "-vf", "crop=iw:ih-mod(ih\,2),scale=1920:-2",
        "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18",
        "-pix_fmt", "yuv420p", $rawPath
    )
    $capture = Start-Process -FilePath $ffmpeg.FullName -ArgumentList $captureArguments -RedirectStandardError $captureLog -WindowStyle Hidden -PassThru

    Start-Sleep -Seconds 6
    Invoke-Element (Find-ElementByName $window "つかう")
    Start-Sleep -Seconds 3

    $demoSettings = Find-ElementByName $window "確認・デモ設定"
    $expandPattern = $null
    if ($demoSettings.TryGetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
        [ref]$expandPattern)) {
        $expandPattern.Expand()
    }
    Start-Sleep -Seconds 2
    Invoke-Element (Find-ElementByName $window "デモデータを使う")
    Start-Sleep -Seconds 5

    Invoke-Element (Find-ElementByName $window "Windowsで文字候補を見る（任意）")
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 200
        $status = Find-ElementById $window "StatusText"
        $statusText = if ($null -eq $status) { "" } else { $status.Current.Name }
    } while ($statusText -notlike "Windows 文字候補：*" -and [DateTime]::UtcNow -lt $deadline)
    if ($statusText -notlike "Windows 文字候補：*") {
        throw "Windows日本語手書き認識に失敗しました: $statusText"
    }
    Start-Sleep -Seconds 5

    Invoke-Element (Find-ElementByName $window "結果を聞く")
    Start-Sleep -Seconds 4
    Invoke-Element (Find-ElementByName $window "書けた・ふり返る")
    Start-Sleep -Seconds 6

    Invoke-Element (Find-ElementByName $window "観測を見てみる（任意）")
    $preview = Find-ElementByName $window "AIによる軌跡の圧縮・再構成" 15
    $scrollItem = $null
    if ($preview.TryGetCurrentPattern(
        [System.Windows.Automation.ScrollItemPattern]::Pattern,
        [ref]$scrollItem)) {
        $scrollItem.ScrollIntoView()
    }
    $ancestor = $preview
    while ($null -ne $ancestor) {
        $scrollPattern = $null
        if ($ancestor.TryGetCurrentPattern(
            [System.Windows.Automation.ScrollPattern]::Pattern,
            [ref]$scrollPattern)) {
            if ($scrollPattern.Current.VerticallyScrollable) {
                $scrollPattern.SetScrollPercent(
                    [System.Windows.Automation.ScrollPattern]::NoScroll,
                    100)
                break
            }
        }
        $ancestor = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($ancestor)
    }
    Start-Sleep -Seconds 14

    Invoke-Element (Find-ElementByName $window "やめる・いまのデータを消す")
    if (-not $capture.WaitForExit(30000)) {
        throw "画面録画が55秒以内に完了しませんでした"
    }
    if (-not (Test-Path -LiteralPath $rawPath) -or (Get-Item -LiteralPath $rawPath).Length -lt 1MB) {
        throw "画面録画に失敗しました。ログ: $captureLog"
    }

    $subtitleForFilter = $resolvedSubtitle.Replace("\", "/").Replace(":", "\:")
    $subtitleFilter = "subtitles='$subtitleForFilter':force_style='FontName=Yu Gothic UI,FontSize=19,BorderStyle=3,BackColour=&H80000000,Outline=1,Shadow=0,MarginV=28'"
    & $ffmpeg.FullName -y -i $rawPath -vf $subtitleFilter -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart -an $resolvedOutput
    if ($LASTEXITCODE -ne 0) {
        throw "字幕の埋め込みに失敗しました"
    }
    Get-Item -LiteralPath $resolvedOutput | Select-Object FullName, Length, LastWriteTime
}
finally {
    if ($null -ne $capture -and -not $capture.HasExited) {
        Stop-Process -Id $capture.Id -Force
    }
    if (-not $app.HasExited) {
        Stop-Process -Id $app.Id -Force
    }
}
