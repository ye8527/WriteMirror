param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,
    [string]$ScreenshotPath = "",
    [switch]$TestRecognition
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeWindowCapture
{
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
"@

function Find-ElementByName {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [int]$TimeoutSeconds = 10
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name)
        $element = $Root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)
        if ($null -ne $element) {
            return $element
        }
        Start-Sleep -Milliseconds 150
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

function Find-OptionalElementByName {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name
    )
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
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

function Get-InferenceCount {
    param([string]$LogPath)
    if (-not (Test-Path -LiteralPath $LogPath)) {
        return 0
    }
    return @(Select-String -LiteralPath $LogPath -SimpleMatch "`tInference`t").Count
}

function Get-AiReadyCount {
    param([string]$LogPath)
    if (-not (Test-Path -LiteralPath $LogPath)) {
        return 0
    }
    return @(Select-String -LiteralPath $LogPath -SimpleMatch "`tAiReady`t").Count
}

function Save-WindowScreenshot {
    param(
        [System.Windows.Automation.AutomationElement]$Window,
        [string]$Path
    )
    $rect = $Window.Current.BoundingRectangle
    $width = [Math]::Max(1, [int]$rect.Width)
    $height = [Math]::Max(1, [int]$rect.Height)
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $hdc = $graphics.GetHdc()
        try {
            $captured = [NativeWindowCapture]::PrintWindow(
                [IntPtr]$Window.Current.NativeWindowHandle,
                $hdc,
                2)
        }
        finally {
            $graphics.ReleaseHdc($hdc)
        }
        if (-not $captured) {
            $graphics.CopyFromScreen(
                [int]$rect.Left,
                [int]$rect.Top,
                0,
                0,
                $bitmap.Size)
        }
        $directory = Split-Path -Parent $Path
        if ($directory) {
            New-Item -ItemType Directory -Force -Path $directory | Out-Null
        }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$logPath = Join-Path $env:LOCALAPPDATA "WriteMirror\windows-ml-readiness.log"
$readyBaseline = Get-AiReadyCount $logPath
$process = Start-Process -FilePath $resolvedExecutable -PassThru

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)

    if ($process.MainWindowHandle -eq 0) {
        throw "WriteMirrorのウィンドウを取得できません"
    }

    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $deadline = [DateTime]::UtcNow.AddSeconds(50)
    do {
        Start-Sleep -Milliseconds 250
        $readyCount = Get-AiReadyCount $logPath
    } while ($readyCount -le $readyBaseline -and [DateTime]::UtcNow -lt $deadline)
    if ($readyCount -le $readyBaseline) {
        throw "AIモデルの準備が50秒以内に完了しませんでした"
    }

    Invoke-Element (Find-ElementByName $window "つかう")

    $demoSettings = Find-ElementByName $window "確認・デモ設定"
    $expandPattern = $null
    if ($demoSettings.TryGetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
        [ref]$expandPattern)) {
        $expandPattern.Expand()
        Start-Sleep -Milliseconds 250
    }

    Invoke-Element (Find-ElementByName $window "デモデータを使う")
    $recognitionStatus = "not-tested"
    if ($TestRecognition) {
        Invoke-Element (Find-ElementByName $window "Windowsで文字候補を見る（任意）")
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 200
            $statusElement = Find-ElementById $window "StatusText"
            $recognitionStatus = if ($null -eq $statusElement) { "" } else { $statusElement.Current.Name }
        } while (
            $recognitionStatus -notlike "Windows 文字候補：*" -and
            $recognitionStatus -notlike "文字候補を取得できません*" -and
            [DateTime]::UtcNow -lt $deadline)
        if ($recognitionStatus -notlike "Windows 文字候補：*") {
            throw "Windows日本語手書き認識に失敗しました: $recognitionStatus"
        }
    }
    $baseline = Get-InferenceCount $logPath
    Invoke-Element (Find-ElementByName $window "書けた・ふり返る")
    Start-Sleep -Milliseconds 500

    $afterNeutral = Get-InferenceCount $logPath
    $previewHeading = "AIによる軌跡の圧縮・再構成"
    $hiddenPanel = Find-OptionalElementByName $window $previewHeading
    if ($afterNeutral -ne $baseline) {
        throw "中立回答の確定直後にAI推論が実行されました"
    }
    if ($null -ne $hiddenPanel) {
        throw "中立回答の確定直後にAIプレビューが表示されました"
    }

    Invoke-Element (Find-ElementByName $window "観測を見てみる（任意）")
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 200
        $visiblePanel = Find-OptionalElementByName $window $previewHeading
        $afterObserve = Get-InferenceCount $logPath
    } while (($null -eq $visiblePanel -or $afterObserve -le $baseline) -and [DateTime]::UtcNow -lt $deadline)

    if ($null -eq $visiblePanel) {
        throw "明示選択後にAIプレビューが表示されませんでした"
    }
    if ($afterObserve -ne ($baseline + 1)) {
        throw "明示選択後のAI推論件数が想定と異なります: $baseline -> $afterObserve"
    }

    $scrollItem = $null
    if ($visiblePanel.TryGetCurrentPattern(
        [System.Windows.Automation.ScrollItemPattern]::Pattern,
        [ref]$scrollItem)) {
        $scrollItem.ScrollIntoView()
    }
    $ancestor = $visiblePanel
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
    Start-Sleep -Seconds 3

    if ($ScreenshotPath) {
        Save-WindowScreenshot $window (Join-Path (Get-Location) $ScreenshotPath)
    }

    [pscustomobject]@{
        ProcessId = $process.Id
        InferenceBefore = $baseline
        InferenceAfterNeutral = $afterNeutral
        InferenceAfterExplicitObserve = $afterObserve
        AiPreviewVisible = $true
        RecognitionStatus = $recognitionStatus
        Screenshot = $ScreenshotPath
    } | Format-List
}
finally {
    if (-not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(3000)) {
            $process.Kill()
        }
    }
}
