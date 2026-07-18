param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$dist = Join-Path $ProjectRoot 'dist'
$docs = Join-Path $ProjectRoot 'docs'
$assetDir = Join-Path $dist 'video-assets-0.5.2'
New-Item -ItemType Directory -Path $assetDir -Force | Out-Null

$slides = @(
    [pscustomobject]@{
        Title = 'WriteMirror 0.5.2'
        Subtitle = 'Surface Penで「書いた過程」をふり返る研究プロトタイプ'
        Body = '小グループ課題向けデモ｜日本語UI｜ARM64 Surface'
        Image = $null
        Narration = 'WriteMirrorは、Surface Penで書いた過程を本人がゆっくり振り返るための、日本語研究プロトタイプです。'
    },
    [pscustomobject]@{
        Title = '1. 本人の意思を最初に確認'
        Subtitle = $null
        Body = "・点数をつけない`n・独立モードでは保存しない`n・いつでもやめられる"
        Image = (Join-Path $dist 'demo-01-start.png')
        Narration = '起動時には、点数をつけないこと、独立モードでは保存しないこと、いつでもやめられることを説明し、本人が使うかどうかを選びます。'
    },
    [pscustomobject]@{
        Title = '2. Surface Penで書く'
        Subtitle = $null
        Body = "・右手・左手を誰でも選択`n・平仮名、片仮名、漢字に対応`n・マウス入力でも確認可能"
        Image = (Join-Path $dist 'demo-02-write.png')
        Narration = '右手と左手は、誰でも画面から選べます。書く文字は平仮名、片仮名、漢字を自由に入力し、Surface Penまたはマウスで書きます。'
    },
    [pscustomobject]@{
        Title = '3. 筆跡を観測する'
        Subtitle = $null
        Body = "・筆画と画間の時間を観測`n・補間時刻から局所速度を表示しない`n・値から困難や能力を推論しない"
        Image = (Join-Path $dist 'demo-03-data.png')
        Narration = 'アプリは筆画と画間の時間を観測します。WPFの点時刻は補間されるため、局所速度や低速度候補は表示しません。数値から困難や能力も推論しません。'
    },
    [pscustomobject]@{
        Title = '4. 気持ちと観測候補を並べる'
        Subtitle = $null
        Body = "・本人の回答を先に選ぶ`n・肯定・なし・回答拒否は候補非表示`n・本人が見るか終わるかを選ぶ"
        Image = (Join-Path $dist 'demo-04-result.png')
        Narration = '書いた後は本人の気持ちを先に選びます。特になし、うまくいった、答えないでは候補を自動表示せず、観測を見るか、このまま終わるかを本人が選びます。'
    },
    [pscustomobject]@{
        Title = '5. 文字候補とフィードバックの範囲'
        Subtitle = $null
        Body = "Windows文字候補`n　候補を表示し、明示選択時だけ置換`n`n端末内の固定日本語テンプレート`n　診断・原因・能力・感情を生成しない`n`nPhi Silica / Aion Instruct / NPU`n　未接続。8月1日版へ追加しない"
        Image = $null
        Narration = '文字候補はWindowsの日本語手書き認識で、独自AIではありません。結果を自動確定せず、選んだ候補だけを使います。フィードバックは端末内の固定日本語テンプレートです。'
    },
    [pscustomobject]@{
        Title = '研究計画としての結論'
        Subtitle = '技術デモは可能。教育効果は未実証。'
        Body = "本課題で行うこと`n成人による操作デモ、コード、単体テスト、研究計画の提示`n`n本課題で行わないこと`n児童実験、診断、治療、学校・家庭導入、AI接続済みという主張"
        Image = $null
        Narration = 'これは小グループ課題の技術デモです。児童実験、診断、治療、学校や家庭への導入は行いません。教育的な効果は、実証結果ではなく研究仮説として提示します。'
    }
)

foreach ($slide in $slides) {
    if ($slide.Image -and -not (Test-Path -LiteralPath $slide.Image)) {
        throw "画像がありません: $($slide.Image)"
    }
}

$voice = New-Object -ComObject SAPI.SpVoice
$japaneseVoice = $voice.GetVoices() | Where-Object { $_.GetDescription() -like '*Haruka*' } | Select-Object -First 1
if ($null -ne $japaneseVoice) {
    $voice.Voice = $japaneseVoice
}
$voice.Rate = 0
$voice.Volume = 100

$audioPaths = @()
for ($index = 0; $index -lt $slides.Count; $index++) {
    $audioPath = Join-Path $assetDir ('narration-{0:D2}.wav' -f ($index + 1))
    $stream = New-Object -ComObject SAPI.SpFileStream
    $stream.Open($audioPath, 3, $false)
    $voice.AudioOutputStream = $stream
    $null = $voice.Speak($slides[$index].Narration)
    $stream.Close()
    $voice.AudioOutputStream = $null
    [Runtime.InteropServices.Marshal]::ReleaseComObject($stream) | Out-Null
    $audioPaths += $audioPath
}
[Runtime.InteropServices.Marshal]::ReleaseComObject($voice) | Out-Null

function Convert-Rgb([int]$r, [int]$g, [int]$b) {
    return $r + (256 * $g) + (65536 * $b)
}

function Add-TextBox($slide, [string]$text, [double]$left, [double]$top, [double]$width, [double]$height, [double]$size, [int]$color, [bool]$bold = $false) {
    $shape = $slide.Shapes.AddTextbox(1, $left, $top, $width, $height)
    $shape.TextFrame.TextRange.Text = $text
    $shape.TextFrame.MarginLeft = 4
    $shape.TextFrame.MarginRight = 4
    $shape.TextFrame.MarginTop = 2
    $shape.TextFrame.MarginBottom = 2
    $shape.TextFrame.TextRange.Font.Name = 'Yu Gothic UI'
    $shape.TextFrame.TextRange.Font.NameFarEast = 'Yu Gothic UI'
    $shape.TextFrame.TextRange.Font.Size = $size
    $shape.TextFrame.TextRange.Font.Color.RGB = $color
    $shape.TextFrame.TextRange.Font.Bold = if ($bold) { -1 } else { 0 }
    return $shape
}

$navy = Convert-Rgb 13 35 58
$blue = Convert-Rgb 22 119 164
$teal = Convert-Rgb 0 133 139
$white = Convert-Rgb 255 255 255
$ink = Convert-Rgb 25 38 52
$pale = Convert-Rgb 239 247 249
$muted = Convert-Rgb 105 124 140

$powerPoint = New-Object -ComObject PowerPoint.Application
$powerPoint.Visible = -1
$presentation = $powerPoint.Presentations.Add()
$presentation.PageSetup.SlideWidth = 960
$presentation.PageSetup.SlideHeight = 540

for ($index = 0; $index -lt $slides.Count; $index++) {
    $data = $slides[$index]
    $slide = $presentation.Slides.Add($index + 1, 12)
    $background = $slide.Shapes.AddShape(1, 0, 0, 960, 540)
    $background.Fill.ForeColor.RGB = $navy
    $background.Line.Visible = 0

    if ($index -eq 0) {
        $accent = $slide.Shapes.AddShape(1, 0, 0, 22, 540)
        $accent.Fill.ForeColor.RGB = $teal
        $accent.Line.Visible = 0
        Add-TextBox $slide $data.Title 72 122 800 90 44 $white $true | Out-Null
        Add-TextBox $slide $data.Subtitle 76 224 800 72 24 $white $false | Out-Null
        Add-TextBox $slide $data.Body 78 345 760 60 17 (Convert-Rgb 174 220 226) $false | Out-Null
    }
    elseif ($null -ne $data.Image) {
        Add-TextBox $slide $data.Title 38 18 860 52 25 $white $true | Out-Null
        $frame = $slide.Shapes.AddShape(5, 30, 84, 638, 428)
        $frame.Fill.ForeColor.RGB = $white
        $frame.Line.ForeColor.RGB = $teal
        $frame.Line.Weight = 2
        $slide.Shapes.AddPicture($data.Image, 0, -1, 39, 92, 620, 413) | Out-Null
        $panel = $slide.Shapes.AddShape(5, 686, 84, 244, 428)
        $panel.Fill.ForeColor.RGB = $pale
        $panel.Line.Visible = 0
        Add-TextBox $slide $data.Body 706 118 204 330 18 $ink $false | Out-Null
        Add-TextBox $slide ('0.5.2  |  ' + ($index + 1) + '/7') 710 470 190 25 10 $muted $false | Out-Null
    }
    else {
        Add-TextBox $slide $data.Title 62 45 840 62 30 $white $true | Out-Null
        if ($data.Subtitle) {
            Add-TextBox $slide $data.Subtitle 66 118 820 52 22 (Convert-Rgb 174 220 226) $true | Out-Null
        }
        $panelTop = if ($data.Subtitle) { 190 } else { 132 }
        $panel = $slide.Shapes.AddShape(5, 62, $panelTop, 836, (480 - $panelTop))
        $panel.Fill.ForeColor.RGB = $pale
        $panel.Line.Visible = 0
        Add-TextBox $slide $data.Body 92 ($panelTop + 28) 776 (420 - $panelTop) 20 $ink $false | Out-Null
    }

    $audio = $slide.Shapes.AddMediaObject2($audioPaths[$index], 0, -1, 944, 524, 1, 1)
    $audio.AnimationSettings.PlaySettings.PlayOnEntry = -1
    $audio.AnimationSettings.PlaySettings.HideWhileNotPlaying = -1
    $audio.AnimationSettings.PlaySettings.StopAfterSlides = 1
    $slide.SlideShowTransition.AdvanceOnClick = 0
    $slide.SlideShowTransition.AdvanceOnTime = -1
    $slide.SlideShowTransition.AdvanceTime = 11
}

$pptxPath = Join-Path $docs 'WriteMirror_デモ動画_ja_0.5.2.pptx'
$videoPath = Join-Path $dist 'WriteMirror_デモ動画_ja_0.5.2.mp4'
$presentation.SaveAs($pptxPath)
$presentation.CreateVideo($videoPath, $true, 11, 1080, 30, 90)

$status = $presentation.CreateVideoStatus
while ($status -eq 1 -or $status -eq 2) {
    Write-Output ("video-status={0} time={1}" -f $status, (Get-Date -Format 'HH:mm:ss'))
    Start-Sleep -Seconds 5
    $status = $presentation.CreateVideoStatus
}

if ($status -ne 3) {
    throw "PowerPoint video export failed. status=$status"
}

$presentation.Close()
$powerPoint.Quit()
[Runtime.InteropServices.Marshal]::ReleaseComObject($presentation) | Out-Null
[Runtime.InteropServices.Marshal]::ReleaseComObject($powerPoint) | Out-Null

Get-Item $pptxPath, $videoPath | Select-Object FullName, Length, LastWriteTime
