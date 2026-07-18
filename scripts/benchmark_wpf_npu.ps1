param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,
    [ValidateRange(3, 100)]
    [int]$Iterations = 10,
    [string]$OutputPath = "artifacts\metrics\wpf-npu-benchmark.json"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

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

function Invoke-Element {
    param([System.Windows.Automation.AutomationElement]$Element)
    $pattern = $Element.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 250
}

function Get-MatchingLineCount {
    param([string]$Path, [string]$Token)
    if (-not (Test-Path -LiteralPath $Path)) {
        return 0
    }
    return @(Select-String -LiteralPath $Path -SimpleMatch $Token).Count
}

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$resolvedOutput = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$logPath = Join-Path $env:LOCALAPPDATA "WriteMirror\windows-ml-readiness.log"
$readyBaseline = Get-MatchingLineCount $logPath "`tAiReady`t"
$process = Start-Process -FilePath $resolvedExecutable -PassThru

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq 0) {
        throw "WriteMirrorのウィンドウを取得できません"
    }

    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 200
        $readyCount = Get-MatchingLineCount $logPath "`tAiReady`t"
    } while ($readyCount -le $readyBaseline -and [DateTime]::UtcNow -lt $deadline)
    if ($readyCount -le $readyBaseline) {
        throw "AIモデルの準備が60秒以内に完了しませんでした"
    }

    Invoke-Element (Find-ElementByName $window "つかう")
    $demoSettings = Find-ElementByName $window "確認・デモ設定"
    $expandPattern = $null
    if ($demoSettings.TryGetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
        [ref]$expandPattern)) {
        $expandPattern.Expand()
        Start-Sleep -Milliseconds 200
    }

    $results = @()
    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
        Invoke-Element (Find-ElementByName $window "デモデータを使う")
        $baseline = Get-MatchingLineCount $logPath "`tInference`t"
        Invoke-Element (Find-ElementByName $window "書けた・ふり返る")
        Invoke-Element (Find-ElementByName $window "観測を見てみる（任意）")

        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 100
            $current = Get-MatchingLineCount $logPath "`tInference`t"
        } while ($current -le $baseline -and [DateTime]::UtcNow -lt $deadline)
        if ($current -ne ($baseline + 1)) {
            throw "推論件数が想定と異なります: $baseline -> $current"
        }

        $line = (Select-String -LiteralPath $logPath -SimpleMatch "`tInference`t" |
            Select-Object -Last 1).Line
        $fields = $line -split "`t"
        $provider = $fields[3]
        if ($provider -notmatch "QNNExecutionProvider" -or $provider -notmatch "NPU") {
            throw "NPU実行を確認できません: $provider"
        }
        $milliseconds = [double]($fields[4] -replace "ms$", "")
        $difference = [double](($fields[5] -split "=")[1])
        $process.Refresh()
        $results += [pscustomobject]@{
            iteration = $iteration
            inferenceMilliseconds = $milliseconds
            reconstructionDifference = $difference
            executionProvider = $provider
            workingSetMiB = [Math]::Round($process.WorkingSet64 / 1MB, 1)
            privateMemoryMiB = [Math]::Round($process.PrivateMemorySize64 / 1MB, 1)
        }

        Invoke-Element (Find-ElementByName $window "やめる・いまのデータを消す")
    }

    $sorted = @($results.inferenceMilliseconds | Sort-Object)
    $median = if ($sorted.Count % 2 -eq 0) {
        ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2
    }
    else {
        $sorted[[Math]::Floor($sorted.Count / 2)]
    }
    $p95Index = [Math]::Max(0, [Math]::Ceiling(0.95 * $sorted.Count) - 1)
    $summary = [pscustomobject]@{
        measuredAt = [DateTimeOffset]::Now.ToString("o")
        iterations = $Iterations
        medianMilliseconds = [Math]::Round($median, 3)
        p95Milliseconds = [Math]::Round($sorted[$p95Index], 3)
        minimumMilliseconds = [Math]::Round($sorted[0], 3)
        maximumMilliseconds = [Math]::Round($sorted[-1], 3)
        maximumWorkingSetMiB = [Math]::Round(($results.workingSetMiB | Measure-Object -Maximum).Maximum, 1)
        maximumPrivateMemoryMiB = [Math]::Round(($results.privateMemoryMiB | Measure-Object -Maximum).Maximum, 1)
        results = $results
    }
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resolvedOutput -Encoding UTF8
    $summary | Format-List measuredAt,iterations,medianMilliseconds,p95Milliseconds,minimumMilliseconds,maximumMilliseconds,maximumWorkingSetMiB,maximumPrivateMemoryMiB
    $results | Format-Table -AutoSize
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
}
