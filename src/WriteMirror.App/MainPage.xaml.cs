using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using WriteMirror.Core.Analysis;
using WriteMirror.Core.Comparison;
using WriteMirror.Core.Feedback;
using WriteMirror.Core.Input;
using WriteMirror.Core.Matching;
using WriteMirror.Core.Models;
using WriteMirror.Core.Replay;
using WriteMirror.Core.Storage;
using WriteMirror.Infrastructure.Storage;

namespace WriteMirror.App;

/// <summary>
/// Captures pen pointer history and renders live and replayed strokes.
/// </summary>
public sealed partial class MainPage : Page
{
    private const double StrokeThickness = 3;
    private const int MaximumReplayDelayMs = 1_500;

    private readonly IPenInputRecorder _recorder = new PenInputRecorder();
    private readonly IWritingAnalyzer _analyzer = new WritingAnalyzer();
    private readonly ISubjectiveMatcher _subjectiveMatcher = new SubjectiveMatcher();
    private readonly IAttemptComparer _attemptComparer;
    private readonly IFeedbackGenerator _feedbackGenerator = new PhiSilicaFeedbackService(
        new UnavailableLocalLanguageModel(),
        new TemplateFeedbackService());
    private readonly ISessionRepository _sessionRepository;
    private readonly SolidColorBrush _liveStrokeBrush = new(Colors.Black);
    private readonly SolidColorBrush _replayStrokeBrush = new(Colors.DodgerBlue);
    private uint? _activePointerId;
    private PenPointSample? _lastRenderedPoint;
    private CancellationTokenSource? _replayCancellation;
    private InteractionMode _interactionMode = InteractionMode.Writing;
    private Point? _markCenter;
    private Ellipse? _markVisual;
    private SubjectiveMark? _subjectiveMark;
    private SubjectiveMatchResult? _subjectiveMatch;
    private Ellipse? _objectiveVisual;
    private WritingAttempt? _currentAttempt;
    private WritingAttempt? _firstAttempt;
    private int _attemptNo = 1;
    private Guid _sessionId = Guid.NewGuid();
    private DateTimeOffset _sessionStartedAt = DateTimeOffset.Now;
    private CancellationTokenSource _sessionPersistenceCancellation = new();
    private string _activeInputName = "入力";

    public MainPage()
    {
        _attemptComparer = new AttemptComparer(_analyzer);
        _sessionRepository = new JsonSessionRepository(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WriteMirror",
            "Sessions"));
        InitializeComponent();
    }

    private void DrawingCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_recorder.IsRecording || _replayCancellation is not null)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(DrawingCanvas);
        if (!point.IsInContact || point.PointerDeviceType == PointerDeviceType.Touch)
        {
            if (point.PointerDeviceType == PointerDeviceType.Touch)
            {
                StatusText.Text = "タッチ入力を検出しました。白い書字エリアに Surface Pen のペン先を当ててください";
                e.Handled = true;
            }

            return;
        }

        if (_interactionMode == InteractionMode.Marking)
        {
            if (!TryCapturePointer(e))
            {
                StatusText.Text = "ペン入力を開始できませんでした。もう一度ペン先を当ててください";
                return;
            }

            _activePointerId = point.PointerId;
            _markCenter = point.Position;
            UpdateMarkVisual(point.Position);
            e.Handled = true;
            return;
        }

        if (_interactionMode != InteractionMode.Writing)
        {
            return;
        }

        if (!TryCapturePointer(e))
        {
            StatusText.Text = "ペン入力を開始できませんでした。もう一度ペン先を当ててください";
            return;
        }

        _activeInputName = point.PointerDeviceType == PointerDeviceType.Pen
            ? "Surface Pen"
            : "マウス";
        _recorder.StartStroke();
        TaskSelector.IsEnabled = false;
        HandednessSelector.IsEnabled = false;
        DemoButton.IsEnabled = false;
        _activePointerId = point.PointerId;
        _lastRenderedPoint = null;
        AddPointerPoint(point);
        e.Handled = true;
    }

    private void DrawingCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _activePointerId)
        {
            return;
        }

        if (_interactionMode == InteractionMode.Marking && _markCenter is not null)
        {
            UpdateMarkVisual(e.GetCurrentPoint(DrawingCanvas).Position);
            e.Handled = true;
            return;
        }

        if (!_recorder.IsRecording)
        {
            return;
        }

        foreach (PointerPoint point in e.GetIntermediatePoints(DrawingCanvas))
        {
            AddPointerPoint(point);
        }

        e.Handled = true;
    }

    private void DrawingCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _activePointerId)
        {
            return;
        }

        if (_interactionMode == InteractionMode.Marking && _markCenter is not null)
        {
            Point edge = e.GetCurrentPoint(DrawingCanvas).Position;
            UpdateMarkVisual(edge);
            FinalizeSubjectiveMark(edge);
            TryReleasePointer(e);
            e.Handled = true;
            return;
        }

        if (!_recorder.IsRecording)
        {
            return;
        }

        foreach (PointerPoint point in e.GetIntermediatePoints(DrawingCanvas))
        {
            AddPointerPoint(point);
        }

        CompleteActiveStroke();
        TryReleasePointer(e);
        e.Handled = true;
    }

    private void DrawingCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_interactionMode == InteractionMode.Marking)
        {
            _activePointerId = null;
            _markCenter = null;
            e.Handled = true;
            return;
        }

        CompleteActiveStroke();
        e.Handled = true;
    }

    private void DrawingCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_interactionMode == InteractionMode.Marking)
        {
            _activePointerId = null;
            _markCenter = null;
            return;
        }

        CompleteActiveStroke();
    }

    private void AddPointerPoint(PointerPoint pointerPoint)
    {
        PenPointSample sample = ToSample(pointerPoint);
        _recorder.AddPoint(sample);
        RenderPoint(sample, _lastRenderedPoint, _liveStrokeBrush);
        _lastRenderedPoint = sample;
    }

    private void CompleteActiveStroke()
    {
        if (!_recorder.IsRecording)
        {
            return;
        }

        _recorder.EndStroke();
        _activePointerId = null;
        _lastRenderedPoint = null;
        if (_attemptNo == 1)
        {
            MarkButton.IsEnabled = _recorder.Strokes.Count > 0;
            StatusText.Text = $"{_activeInputName} を検出しました。1回目：{_recorder.Strokes.Count}画を記録しました";
        }
        else
        {
            ReplayButton.IsEnabled = true;
            CompareButton.IsEnabled = true;
            StatusText.Text = $"{_activeInputName} を検出しました。2回目：{_recorder.Strokes.Count}画を記録しました。比較できます";
        }
    }

    private static PenPointSample ToSample(PointerPoint point)
    {
        bool isPen = point.PointerDeviceType == PointerDeviceType.Pen;
        return new PenPointSample(
            point.Position.X,
            point.Position.Y,
            checked((long)point.Timestamp),
            isPen ? TryReadPenProperty(() => point.Properties.Pressure) : null,
            isPen ? TryReadPenProperty(() => point.Properties.XTilt) : null,
            isPen ? TryReadPenProperty(() => point.Properties.YTilt) : null,
            point.IsInContact);
    }

    private bool TryCapturePointer(PointerRoutedEventArgs args)
    {
        try
        {
            return DrawingCanvas.CapturePointer(args.Pointer);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void TryReleasePointer(PointerRoutedEventArgs args)
    {
        try
        {
            DrawingCanvas.ReleasePointerCapture(args.Pointer);
        }
        catch (Exception)
        {
            // ドライバーが既にキャプチャを解放している場合は、そのまま終了します。
        }
    }

    private static float? TryReadPenProperty(Func<float> readProperty)
    {
        try
        {
            return readProperty();
        }
        catch (Exception)
        {
            // 端末が対応していない筆圧・傾きは欠損値として扱います。
            return null;
        }
    }

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        Guid sessionToDelete = _sessionId;
        ResetSessionState();
        try
        {
            await _sessionRepository.DeleteAsync(sessionToDelete);
            StatusText.Text = "セッションを端末から削除しました。新しい書字を開始できます";
        }
        catch (Exception)
        {
            StatusText.Text = "画面は消去しましたが、端末内ファイルの削除に失敗しました";
        }
    }

    private async void ReplayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording ||
            _recorder.Strokes.Count == 0 ||
            _replayCancellation is not null)
        {
            return;
        }

        _replayCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _replayCancellation.Token;
        ReplayButton.IsEnabled = false;
        DrawingCanvas.Children.Clear();
        StatusText.Text = "再生中…";

        try
        {
            PenPointSample? previousPoint = null;
            foreach (ReplayFrame frame in StrokeReplayTimeline.Create(_recorder.Strokes))
            {
                int delayMs = (int)Math.Min(
                    frame.DelayUs / 1_000,
                    MaximumReplayDelayMs);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, cancellationToken);
                }

                if (frame.StartsStroke)
                {
                    previousPoint = null;
                }

                RenderPoint(frame.Point, previousPoint, _replayStrokeBrush);
                previousPoint = frame.Point;
            }

            StatusText.Text = $"再生完了 · {_recorder.Strokes.Count}画";
            RenderSubjectiveMark();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "再生を中止しました";
        }
        finally
        {
            _replayCancellation?.Dispose();
            _replayCancellation = null;
            ReplayButton.IsEnabled = _recorder.Strokes.Count > 0;
        }
    }

    private void RenderPoint(
        PenPointSample point,
        PenPointSample? previousPoint,
        Brush brush)
    {
        if (previousPoint is null)
        {
            var dot = new Ellipse
            {
                Width = StrokeThickness,
                Height = StrokeThickness,
                Fill = brush
            };
            Canvas.SetLeft(dot, point.X - StrokeThickness / 2);
            Canvas.SetTop(dot, point.Y - StrokeThickness / 2);
            DrawingCanvas.Children.Add(dot);
            return;
        }

        DrawingCanvas.Children.Add(new Line
        {
            X1 = previousPoint.X,
            Y1 = previousPoint.Y,
            X2 = point.X,
            Y2 = point.Y,
            Stroke = brush,
            StrokeThickness = StrokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
    }

    private void CancelReplay()
    {
        _replayCancellation?.Cancel();
    }

    private void MarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.Strokes.Count == 0 || _replayCancellation is not null)
        {
            return;
        }

        if (_markVisual is not null)
        {
            DrawingCanvas.Children.Remove(_markVisual);
            _markVisual = null;
        }

        if (_objectiveVisual is not null)
        {
            DrawingCanvas.Children.Remove(_objectiveVisual);
            _objectiveVisual = null;
        }

        _subjectiveMark = null;
        _subjectiveMatch = null;
        _interactionMode = InteractionMode.Marking;
        ReplayButton.IsEnabled = false;
        MarkButton.IsEnabled = false;
        SubjectiveLabelBox.IsEnabled = true;
        SecondAttemptButton.IsEnabled = false;
        StatusText.Text = "データを見る前に、書きにくかった場所を囲んでください";
    }

    private void UpdateMarkVisual(Point edge)
    {
        if (_markCenter is null)
        {
            return;
        }

        double radius = Distance(_markCenter.Value, edge);
        if (_markVisual is null)
        {
            _markVisual = new Ellipse
            {
                Stroke = new SolidColorBrush(Colors.DarkOrange),
                StrokeThickness = 3
            };
            DrawingCanvas.Children.Add(_markVisual);
        }

        _markVisual.Width = radius * 2;
        _markVisual.Height = radius * 2;
        Canvas.SetLeft(_markVisual, _markCenter.Value.X - radius);
        Canvas.SetTop(_markVisual, _markCenter.Value.Y - radius);
    }

    private void FinalizeSubjectiveMark(Point edge)
    {
        if (_markCenter is null)
        {
            return;
        }

        double radius = Distance(_markCenter.Value, edge);
        _activePointerId = null;
        if (radius < 8)
        {
            if (_markVisual is not null)
            {
                DrawingCanvas.Children.Remove(_markVisual);
                _markVisual = null;
            }

            _markCenter = null;
            MarkButton.IsEnabled = true;
            StatusText.Text = "選択範囲が小さすぎます。もう一度囲んでください";
            return;
        }

        _subjectiveMark = new SubjectiveMark(
            _markCenter.Value.X,
            _markCenter.Value.Y,
            radius,
            new[] { SelectedSubjectiveLabel() });
        _markCenter = null;
        _interactionMode = InteractionMode.Marked;
        MarkButton.Content = "位置を選び直す";
        MarkButton.IsEnabled = true;
        ReplayButton.IsEnabled = true;
        SubjectiveLabelBox.IsEnabled = false;
        SecondAttemptButton.IsEnabled = true;

        WritingMetrics metrics = _analyzer.Analyze(
            new WritingAttempt(1, _recorder.Strokes));
        _currentAttempt = new WritingAttempt(
            1,
            _recorder.Strokes,
            _subjectiveMark,
            metrics);
        SaveCurrentSessionAsync();
        SubjectiveMatchResult match = _subjectiveMatcher.Match(_subjectiveMark, metrics);
        _subjectiveMatch = match;
        RenderObjectiveEvent(match.Closest);
        InitialMetricsText.Text = FormatInitialMetrics(metrics);
        StatusText.Text = DescribeMatch(match);
        ShowFeedbackAsync(_currentAttempt!, match, null);
    }

    private void SecondAttemptButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentAttempt is null || _replayCancellation is not null)
        {
            return;
        }

        _firstAttempt = _currentAttempt;
        _currentAttempt = null;
        _attemptNo = 2;
        _recorder.Reset();
        _subjectiveMark = null;
        _subjectiveMatch = null;
        _markVisual = null;
        _objectiveVisual = null;
        _interactionMode = InteractionMode.Writing;
        DrawingCanvas.Children.Clear();
        ReplayButton.IsEnabled = false;
        MarkButton.Visibility = Visibility.Collapsed;
        SubjectiveLabelBox.Visibility = Visibility.Collapsed;
        SecondAttemptButton.Visibility = Visibility.Collapsed;
        CompareButton.Visibility = Visibility.Visible;
        CompareButton.IsEnabled = false;
        ComparisonPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = "2回目を書いてください。1回目のデータは分けて保存されています";
    }

    private void CompareButton_Click(object sender, RoutedEventArgs e)
    {
        if (_firstAttempt is null || _recorder.Strokes.Count == 0)
        {
            return;
        }

        WritingMetrics secondMetrics = _analyzer.Analyze(
            new WritingAttempt(2, _recorder.Strokes));
        _currentAttempt = new WritingAttempt(
            2,
            _recorder.Strokes,
            metrics: secondMetrics);
        SaveCurrentSessionAsync();
        AttemptComparison comparison = _attemptComparer.Compare(
            _firstAttempt,
            _currentAttempt);

        DurationComparisonText.Text = FormatMetric(
            "総書字時間",
            comparison.TotalDurationUs,
            value => $"{value / 1_000:0} ms");
        PauseComparisonText.Text = comparison.LongestPauseUs is null
            ? "最長停止：データが不足しているため比較できません"
            : FormatMetric(
                "最長停止",
                comparison.LongestPauseUs,
                value => $"{value / 1_000:0} ms");
        PressureComparisonText.Text = comparison.PressureVariability is null
            ? "筆圧の変動：このデバイスではデータが不足しています"
            : FormatMetric(
                "筆圧の変動",
                comparison.PressureVariability,
                value => value.ToString("0.000"));
        StructureComparisonText.Text = comparison.HasComparableStrokeStructure
            ? $"画数：{comparison.FirstStrokeCount} → {comparison.SecondStrokeCount}。画ごとの比較が可能です"
            : $"画数：{comparison.FirstStrokeCount} → {comparison.SecondStrokeCount}。構成が異なるため総量のみ表示します";

        ResultTitleText.Text = "2回の書字比較と端末内フィードバック";
        DurationComparisonText.Visibility = Visibility.Visible;
        PauseComparisonText.Visibility = Visibility.Visible;
        PressureComparisonText.Visibility = Visibility.Visible;
        StructureComparisonText.Visibility = Visibility.Visible;

        ComparisonPanel.Visibility = Visibility.Visible;
        CompareButton.IsEnabled = false;
        _interactionMode = InteractionMode.Marked;
        StatusText.Text = "2回の書字比較が完了しました";
        ShowFeedbackAsync(_firstAttempt!, null, comparison);
    }

    private async void ShowFeedbackAsync(
        WritingAttempt firstAttempt,
        SubjectiveMatchResult? match,
        AttemptComparison? comparison)
    {
        try
        {
            WritingMetrics metrics = firstAttempt.Metrics ?? _analyzer.Analyze(firstAttempt);
            PauseMetrics? longestPause = metrics.Pauses
                .OrderByDescending(pause => pause.DurationUs)
                .FirstOrDefault();
            SubjectiveEventMatch? closest = match?.Closest;
            if (closest is null && firstAttempt.SubjectiveMark is not null)
            {
                closest = _subjectiveMatcher
                    .Match(firstAttempt.SubjectiveMark, metrics)
                    .Closest;
            }

            var request = new FeedbackRequest(
                "ja-JP",
                SelectedTaskDisplayName(),
                longestPause is null ? null : longestPause.BeforeStrokeIndex + 1,
                longestPause is null ? null : longestPause.AfterStrokeIndex + 1,
                longestPause is null ? null : longestPause.DurationUs / 1_000,
                closest?.Relation,
                closest?.EventKind,
                comparison?.TotalDurationUs.PercentChange,
                comparison?.HasComparableStrokeStructure,
                GetPressureTrend(comparison?.PressureVariability),
                firstAttempt.SubjectiveResponse ?? SubjectiveResponseKind.Skipped);
            FeedbackMessage feedback = await _feedbackGenerator.GenerateAsync(request);

            FeedbackObservationText.Text = feedback.Observation;
            FeedbackReflectionText.Text = feedback.Reflection;
            FeedbackQuestionText.Text = feedback.NextQuestion;
            FeedbackGeneratorText.Text = feedback.Generator == "phi-silica"
                ? "生成方法：Phi Silica（端末内）"
                : "生成方法：オフラインテンプレート（Phi Silica 未設定またはフォールバック）";
            if (comparison is null)
            {
                ResultTitleText.Text = "端末内フィードバック";
                DurationComparisonText.Visibility = Visibility.Collapsed;
                PauseComparisonText.Visibility = Visibility.Collapsed;
                PressureComparisonText.Visibility = Visibility.Collapsed;
                StructureComparisonText.Visibility = Visibility.Collapsed;
            }

            ComparisonPanel.Visibility = Visibility.Visible;
        }
        catch (Exception error)
        {
            FeedbackObservationText.Text = "フィードバック生成は現在利用できません。";
            FeedbackReflectionText.Text = error is OperationCanceledException
                ? "処理を中止しました。"
                : "筆跡と比較データはこのセッションに保持されています。";
            FeedbackQuestionText.Text = "決定的アルゴリズムの分析結果は引き続き確認できます。";
            FeedbackGeneratorText.Text = "生成方法：フォールバックメッセージ";
            ComparisonPanel.Visibility = Visibility.Visible;
        }
    }

    private static PressureTrend GetPressureTrend(MetricDelta? delta)
    {
        if (delta is null)
        {
            return PressureTrend.Unavailable;
        }

        if (Math.Abs(delta.AbsoluteChange) < 0.001)
        {
            return PressureTrend.NoClearChange;
        }

        return delta.AbsoluteChange < 0
            ? PressureTrend.Decreased
            : PressureTrend.Increased;
    }

    private async void SaveCurrentSessionAsync()
    {
        WritingAttempt[] attempts = _firstAttempt is not null && _currentAttempt is not null
            ? [_firstAttempt, _currentAttempt]
            : _currentAttempt is not null
                ? [_currentAttempt]
                : [];
        if (attempts.Length == 0)
        {
            return;
        }

        var session = new WritingSession(
            _sessionId,
            SelectedTaskId(),
            _sessionStartedAt,
            attempts,
            SelectedHandedness());
        try
        {
            await _sessionRepository.SaveAsync(
                session,
                _sessionPersistenceCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // A reset or deletion intentionally cancels an in-flight save.
        }
        catch (Exception)
        {
            StatusText.Text = "分析は完了しましたが、端末内JSONの保存に失敗しました";
        }
    }

    private async void DemoButton_Click(object sender, RoutedEventArgs e)
    {
        Guid sessionToDelete = _sessionId;
        ResetSessionState();
        try
        {
            await _sessionRepository.DeleteAsync(sessionToDelete);
        }
        catch (Exception)
        {
            // Demo data remains usable in memory even if an earlier file cannot be removed.
        }

        TaskSelector.SelectedIndex = 1;
        TaskSelector.IsEnabled = false;
        HandednessSelector.IsEnabled = false;
        DemoButton.IsEnabled = false;
        LoadDemoStrokes();
        MarkButton.IsEnabled = true;
        StatusText.Text = "デモ「木」を読み込みました。先に書きにくかった場所を囲んでください";
    }

    private void LoadDemoStrokes()
    {
        PenPointSample[][] strokes =
        [
            [DemoPoint(190, 150, 1_000_000), DemoPoint(480, 150, 1_420_000)],
            [DemoPoint(335, 80, 1_680_000), DemoPoint(335, 360, 2_160_000)],
            [DemoPoint(330, 205, 2_430_000), DemoPoint(215, 345, 2_820_000)],
            [DemoPoint(340, 205, 3_520_000), DemoPoint(475, 350, 3_900_000)]
        ];

        foreach (PenPointSample[] points in strokes)
        {
            _recorder.StartStroke();
            PenPointSample? previous = null;
            foreach (PenPointSample point in points)
            {
                _recorder.AddPoint(point);
                RenderPoint(point, previous, _liveStrokeBrush);
                previous = point;
            }

            _recorder.EndStroke();
        }
    }

    private static PenPointSample DemoPoint(double x, double y, long timestampUs) =>
        new(x, y, timestampUs, 0.5f, 0, 0, true);

    private void ResetSessionState()
    {
        CancelReplay();
        _sessionPersistenceCancellation.Cancel();
        _sessionPersistenceCancellation.Dispose();
        _sessionPersistenceCancellation = new CancellationTokenSource();
        _sessionId = Guid.NewGuid();
        _sessionStartedAt = DateTimeOffset.Now;
        _activeInputName = "入力";
        _recorder.Reset();
        _activePointerId = null;
        _lastRenderedPoint = null;
        _markCenter = null;
        _subjectiveMark = null;
        _subjectiveMatch = null;
        _currentAttempt = null;
        _firstAttempt = null;
        _attemptNo = 1;
        _markVisual = null;
        _objectiveVisual = null;
        _interactionMode = InteractionMode.Writing;
        DrawingCanvas.Children.Clear();
        ReplayButton.IsEnabled = false;
        MarkButton.IsEnabled = false;
        MarkButton.Content = "書字完了・位置選択";
        MarkButton.Visibility = Visibility.Visible;
        SubjectiveLabelBox.IsEnabled = false;
        SubjectiveLabelBox.Visibility = Visibility.Visible;
        SecondAttemptButton.IsEnabled = false;
        SecondAttemptButton.Visibility = Visibility.Visible;
        CompareButton.IsEnabled = false;
        CompareButton.Visibility = Visibility.Collapsed;
        ComparisonPanel.Visibility = Visibility.Collapsed;
        InitialMetricsText.Text = string.Empty;
        TaskSelector.IsEnabled = true;
        HandednessSelector.IsEnabled = true;
        DemoButton.IsEnabled = true;
        StatusText.Text = "ペンで書いてください（デモではマウスも使用できます）";
    }

    private string SelectedTaskId() => TaskSelector.SelectedIndex switch
    {
        0 => "hiragana_a",
        2 => "kanji_go",
        _ => "kanji_ki"
    };

    private string SelectedTaskDisplayName() => TaskSelector.SelectedIndex switch
    {
        0 => "あ",
        2 => "語",
        _ => "木"
    };

    private Handedness SelectedHandedness() =>
        HandednessSelector.SelectedIndex == 1
            ? Handedness.Left
            : Handedness.Right;

    private void RenderSubjectiveMark()
    {
        if (_subjectiveMark is null)
        {
            return;
        }

        _markVisual = new Ellipse
        {
            Width = _subjectiveMark.RadiusPx * 2,
            Height = _subjectiveMark.RadiusPx * 2,
            Stroke = new SolidColorBrush(Colors.DarkOrange),
            StrokeThickness = 3
        };
        Canvas.SetLeft(_markVisual, _subjectiveMark.CenterX - _subjectiveMark.RadiusPx);
        Canvas.SetTop(_markVisual, _subjectiveMark.CenterY - _subjectiveMark.RadiusPx);
        DrawingCanvas.Children.Add(_markVisual);
        RenderObjectiveEvent(_subjectiveMatch?.Closest);
    }

    private void RenderObjectiveEvent(SubjectiveEventMatch? match)
    {
        if (match is null)
        {
            return;
        }

        _objectiveVisual = new Ellipse
        {
            Width = 16,
            Height = 16,
            Stroke = new SolidColorBrush(Colors.DodgerBlue),
            StrokeThickness = 3
        };
        Canvas.SetLeft(_objectiveVisual, match.EventX - 8);
        Canvas.SetTop(_objectiveVisual, match.EventY - 8);
        DrawingCanvas.Children.Add(_objectiveVisual);
    }

    private SubjectiveLabel SelectedSubjectiveLabel() =>
        SubjectiveLabelBox.SelectedIndex switch
        {
            1 => SubjectiveLabel.Difficult,
            2 => SubjectiveLabel.Dissatisfied,
            _ => SubjectiveLabel.Hesitation
        };

    private static string DescribeMatch(SubjectiveMatchResult result)
    {
        if (result.Closest is null)
        {
            return "主観位置を保存しました。今回は照合できるイベントがありません";
        }

        string eventName = result.Closest.EventKind == ObjectiveEventKind.LongestPause
            ? "最長停止"
            : "最も遅い移動区間";
        return result.Closest.Relation switch
        {
            SpatialRelation.Inside => $"選んだ範囲と{eventName}の位置が重なっています",
            SpatialRelation.Near => $"選んだ範囲と{eventName}の位置は近くにあります",
            _ => $"選んだ範囲と{eventName}は別の位置にあります"
        };
    }

    private static double Distance(Point first, Point second) =>
        Math.Sqrt(
            Math.Pow(second.X - first.X, 2) +
            Math.Pow(second.Y - first.Y, 2));

    private static string FormatMetric(
        string label,
        MetricDelta delta,
        Func<double, string> formatValue)
    {
        string percent = delta.PercentChange is null
            ? "割合は算出できません"
            : $"{delta.PercentChange.Value:+0.0;-0.0;0.0}%";
        return $"{label}：{formatValue(delta.FirstValue)} → {formatValue(delta.SecondValue)}（{percent}）";
    }

    private static string FormatInitialMetrics(WritingMetrics metrics)
    {
        string pause = metrics.LongestPauseUs is null
            ? "最長停止：利用不可"
            : $"最長停止：{metrics.LongestPauseUs.Value / 1_000:0} ms";
        string pressure = metrics.Pressure is null
            ? "筆圧：このデバイスでは利用不可"
            : $"平均筆圧：{metrics.Pressure.Mean:0.00} · 変動：{metrics.Pressure.PopulationStandardDeviation:0.000}";
        return $"総時間：{metrics.TotalDurationUs / 1_000:0} ms · {metrics.Strokes.Count}画 · {pause} · {pressure}";
    }

    private enum InteractionMode
    {
        Writing,
        Marking,
        Marked
    }
}
