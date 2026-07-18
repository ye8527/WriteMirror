using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WriteMirror.Core.Analysis;
using WriteMirror.Core.Comparison;
using WriteMirror.Core.Feedback;
using WriteMirror.Core.Input;
using WriteMirror.Core.Matching;
using WriteMirror.Core.Models;
using WriteMirror.Core.Policy;
using WriteMirror.Core.Replay;
using WriteMirror.Core.Storage;
using WriteMirror.Infrastructure.Storage;
using InkStroke = System.Windows.Ink.Stroke;

namespace WriteMirror.Wpf;

public partial class MainWindow : Window
{
    private const int MaximumReplayDelayMs = 1_000;

    private readonly IPenInputRecorder _recorder = new PenInputRecorder();
    private readonly IWritingAnalyzer _analyzer = new WritingAnalyzer();
    private readonly ISubjectiveMatcher _subjectiveMatcher = new SubjectiveMatcher();
    private readonly IAttemptComparer _attemptComparer;
    private readonly IFeedbackGenerator _feedbackGenerator = new TemplateFeedbackService();
    private readonly ISessionRepository _sessionRepository;
    private readonly JapaneseHandwritingRecognizer? _handwritingRecognizer;

    private CancellationTokenSource? _replayCancellation;
    private CancellationTokenSource _saveCancellation = new();
    private InteractionMode _mode = InteractionMode.Writing;
    private long? _strokeStartedUs;
    private string _activeInputName = "Windows Ink";
    private SubjectiveMark? _subjectiveMark;
    private SubjectiveMatchResult? _subjectiveMatch;
    private WritingAttempt? _currentAttempt;
    private WritingAttempt? _firstAttempt;
    private int _attemptNo = 1;
    private Guid _sessionId = Guid.NewGuid();
    private DateTimeOffset _sessionStartedAt = DateTimeOffset.Now;

    public MainWindow()
    {
        InitializeComponent();
        _attemptComparer = new AttemptComparer(_analyzer);
        _sessionRepository = new JsonSessionRepository(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WriteMirror",
            "Sessions"));
        try
        {
            _handwritingRecognizer = new JapaneseHandwritingRecognizer();
        }
        catch (Exception error)
        {
            RecognizeButton.Content = "手書き認識は利用不可";
            RecognizeButton.ToolTip = error.Message;
        }

        var attributes = new DrawingAttributes
        {
            Color = Colors.Black,
            Width = 3,
            Height = 3,
            FitToCurve = false,
            IgnorePressure = false
        };
        WritingCanvas.DefaultDrawingAttributes = attributes;
        ApplyUsageMode();
        StartPracticeButton.Focus();
    }

    private UsageMode CurrentUsageMode =>
        UsageModeSelector.SelectedIndex == 1
            ? UsageMode.GuidedReview
            : UsageMode.IndependentPractice;

    private void StartPracticeButton_Click(object sender, RoutedEventArgs e)
    {
        AssentPanel.Visibility = Visibility.Collapsed;
        PracticeContent.IsEnabled = true;
        StepText.Text = "1 / 5　文字を書こう";
        StatusText.Text = "下の白いところに Surface Pen で書いてください";
        WritingCanvas.Focus();
    }

    private void DeclinePracticeButton_Click(object sender, RoutedEventArgs e)
    {
        ResetSessionState();
        Close();
    }

    private async void UsageModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        bool changedToIndependent = CurrentUsageMode == UsageMode.IndependentPractice;
        if (changedToIndependent)
        {
            SaveConsentCheckBox.IsChecked = false;
            try
            {
                await _sessionRepository.DeleteAsync(_sessionId);
            }
            catch (Exception)
            {
                StatusText.Text = "保存していた現在のデータを消せませんでした。いっしょに確認する人へ知らせてください";
            }
        }

        ApplyUsageMode();
    }

    private void ApplyUsageMode()
    {
        bool guided = CurrentUsageMode == UsageMode.GuidedReview;
        GuidedSavePanel.Visibility = guided ? Visibility.Visible : Visibility.Collapsed;
        ModeNoticeText.Text = guided
            ? "いっしょに確認するモードです。保存は毎回えらべます。点数や診断には使いません。"
            : "このモードは保存しません。点数をつけません。いつでもやめられます。";
    }

    private async void InstructionSpeakButton_Click(object sender, RoutedEventArgs e)
    {
        string text = $"{StepText.Text}。{StatusText.Text}。{ModeNoticeText.Text}";
        InstructionSpeakButton.IsEnabled = false;
        try
        {
            await Task.Run(() => SpeakJapanese(text));
        }
        catch (Exception error)
        {
            StatusText.Text = $"音声を使えませんでした：{error.Message}";
        }
        finally
        {
            InstructionSpeakButton.IsEnabled = true;
        }
    }

    private void WritingCanvas_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        _strokeStartedUs = NowUs();
        _activeInputName = "Surface Pen";
        StatusText.Text = "Surface Pen を検出しました。書字中です";
    }

    private void WritingCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Stylus.CurrentStylusDevice is not null)
        {
            return;
        }

        _strokeStartedUs = NowUs();
        _activeInputName = "マウス";
        StatusText.Text = "マウス入力を検出しました。書字中です";
    }

    private void WritingCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        try
        {
            if (_replayCancellation is not null)
            {
                return;
            }

            if (_mode == InteractionMode.Marking)
            {
                CompleteSubjectiveMark(e.Stroke);
                return;
            }

            if (_mode != InteractionMode.Writing)
            {
                WritingCanvas.Strokes.Remove(e.Stroke);
                return;
            }

            RecordStroke(e.Stroke);
            HandednessSelector.IsEnabled = false;
            DemoButton.IsEnabled = false;

            if (_attemptNo == 1)
            {
                CandidateSelector.ItemsSource = null;
                CandidateSelector.IsEnabled = false;
                RecognizeButton.IsEnabled = _handwritingRecognizer is not null;
                SpeakButton.IsEnabled = !string.IsNullOrWhiteSpace(RecognizedTextBox.Text);
                MarkButton.IsEnabled = _recorder.Strokes.Count > 0;
                SubjectiveLabelBox.IsEnabled = true;
                StepText.Text = "2 / 5　書いたあとの気もちをえらぼう";
                StatusText.Text = $"{_activeInputName} で {_recorder.Strokes.Count} 画を記録しました";
            }
            else
            {
                ReplayButton.IsEnabled = true;
                MarkButton.IsEnabled = true;
                SubjectiveLabelBox.IsEnabled = true;
                StepText.Text = "5 / 5　もう一度書いて、気もちをえらぼう";
                StatusText.Text = $"2回目を {_recorder.Strokes.Count} 画記録しました。振り返りを選んでください";
            }
        }
        catch (Exception error)
        {
            StatusText.Text = $"筆画を安全に保存できませんでした：{error.Message}";
        }
    }

    private void RecordStroke(InkStroke stroke)
    {
        StylusPointCollection points = stroke.StylusPoints;
        if (points.Count == 0)
        {
            return;
        }

        long endUs = NowUs();
        long startUs = _strokeStartedUs ?? Math.Max(0, endUs - (points.Count - 1L) * 8_000L);
        if (endUs <= startUs)
        {
            endUs = startUs + Math.Max(1, points.Count - 1L) * 1_000L;
        }

        _recorder.StartStroke();
        for (int index = 0; index < points.Count; index++)
        {
            StylusPoint point = points[index];
            long timestampUs = points.Count == 1
                ? startUs
                : startUs + (endUs - startUs) * index / (points.Count - 1L);
            _recorder.AddPoint(new PenPointSample(
                point.X,
                point.Y,
                timestampUs,
                point.PressureFactor,
                null,
                null,
                true));
        }

        _recorder.EndStroke();
        _strokeStartedUs = null;
    }

    private async void MarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.Strokes.Count == 0 || _replayCancellation is not null)
        {
            return;
        }

        if (_attemptNo == 1 && string.IsNullOrWhiteSpace(RecognizedTextBox.Text))
        {
            await RecognizeWritingAsync();
        }

        MarkerCanvas.Children.Clear();
        _subjectiveMark = null;
        _subjectiveMatch = null;
        SubjectiveResponseKind response = SelectedSubjectiveResponseKind();
        if (!SubjectiveResponsePolicy.RequiresLocation(response))
        {
            FinalizeCurrentAttempt(null, response);
            return;
        }

        _mode = InteractionMode.Marking;
        StepText.Text = "3 / 5　気になったところをペンでかこもう";
        MarkButton.IsEnabled = false;
        ReplayButton.IsEnabled = false;
        SubjectiveLabelBox.IsEnabled = true;
        SecondAttemptButton.IsEnabled = false;
        StatusText.Text = "結果を見る前に、気になった位置をペンで円のように囲んでください";
    }

    private async void RecognizeButton_Click(object sender, RoutedEventArgs e)
    {
        await RecognizeWritingAsync();
    }

    private async Task RecognizeWritingAsync()
    {
        if (_handwritingRecognizer is null || WritingCanvas.Strokes.Count == 0)
        {
            StatusText.Text = "認識できる筆跡がありません";
            return;
        }

        RecognizeButton.IsEnabled = false;
        StatusText.Text = "Windows の平仮名・片仮名・漢字候補を取得しています…";
        try
        {
            HandwritingRecognition result = await _handwritingRecognizer.RecognizeAsync(WritingCanvas.Strokes);
            if (string.IsNullOrWhiteSpace(result.Text))
            {
                StatusText.Text = "文字を認識できませんでした。認識文字欄へ手動で入力できます";
                return;
            }

            CandidateSelector.ItemsSource = result.Candidates;
            CandidateSelector.SelectedIndex = -1;
            CandidateSelector.IsEnabled = result.Candidates.Count > 0;
            string candidates = result.Candidates.Count == 0
                ? string.Empty
                : $" 候補：{string.Join("、", result.Candidates)}";
            StatusText.Text = $"Windows 文字候補：{result.Text}（{result.RecognizerName}）{candidates} 候補を選ぶか、文字欄へ入力してください";
            SpeakButton.IsEnabled = !string.IsNullOrWhiteSpace(RecognizedTextBox.Text);
        }
        catch (Exception error)
        {
            StatusText.Text = $"文字候補を取得できませんでした。対象文字欄へ手動入力できます：{error.Message}";
        }
        finally
        {
            RecognizeButton.IsEnabled = _handwritingRecognizer is not null && WritingCanvas.Strokes.Count > 0;
        }
    }

    private void CandidateSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CandidateSelector.SelectedItem is string candidate &&
            !string.IsNullOrWhiteSpace(candidate))
        {
            RecognizedTextBox.Text = candidate;
            SpeakButton.IsEnabled = true;
            StatusText.Text = $"候補「{candidate}」を選びました。必要なら対象文字欄で修正できます";
        }
    }

    private async void SpeakButton_Click(object sender, RoutedEventArgs e)
    {
        string text = string.Join("。", new[]
        {
            string.IsNullOrWhiteSpace(RecognizedTextBox.Text)
                ? string.Empty
                : $"認識した文字は、{RecognizedTextBox.Text}です",
            FeedbackObservationText.Text,
            FeedbackReflectionText.Text,
            FeedbackQuestionText.Text
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText.Text = "読み上げる内容がありません";
            return;
        }

        SpeakButton.IsEnabled = false;
        StatusText.Text = "日本語音声で読み上げています…";
        try
        {
            await Task.Run(() => SpeakJapanese(text));
            StatusText.Text = "音声読み上げが完了しました";
        }
        catch (Exception error)
        {
            StatusText.Text = $"音声読み上げに失敗しました：{error.Message}";
        }
        finally
        {
            SpeakButton.IsEnabled = true;
        }
    }

    private static void SpeakJapanese(string text)
    {
        Type voiceType = Type.GetTypeFromProgID("SAPI.SpVoice")
            ?? throw new InvalidOperationException("Windows 音声合成を利用できません");
        object voiceObject = Activator.CreateInstance(voiceType)
            ?? throw new InvalidOperationException("Windows 音声合成を開始できません");
        try
        {
            dynamic voice = voiceObject;
            dynamic voices = voice.GetVoices();
            for (int index = 0; index < voices.Count; index++)
            {
                dynamic token = voices.Item(index);
                string description = token.GetDescription();
                if (description.Contains("Haruka", StringComparison.OrdinalIgnoreCase) ||
                    description.Contains("Japanese", StringComparison.OrdinalIgnoreCase) ||
                    description.Contains("日本", StringComparison.OrdinalIgnoreCase))
                {
                    voice.Voice = token;
                    break;
                }
            }

            voice.Rate = 0;
            voice.Volume = 100;
            voice.Speak(text);
        }
        finally
        {
            Marshal.FinalReleaseComObject(voiceObject);
        }
    }

    private void CompleteSubjectiveMark(InkStroke stroke)
    {
        Rect bounds = stroke.GetBounds();
        WritingCanvas.Strokes.Remove(stroke);
        double radius = Math.Max(bounds.Width, bounds.Height) / 2;
        if (radius < 8)
        {
            _mode = InteractionMode.Writing;
            MarkButton.IsEnabled = true;
            StatusText.Text = "選択範囲が小さすぎます。もう一度、位置を囲んでください";
            return;
        }

        double centerX = bounds.Left + bounds.Width / 2;
        double centerY = bounds.Top + bounds.Height / 2;
        _subjectiveMark = new SubjectiveMark(
            centerX,
            centerY,
            radius,
            new[] { SelectedSubjectiveLabel() });
        FinalizeCurrentAttempt(_subjectiveMark, SelectedSubjectiveResponseKind());
    }

    private void FinalizeCurrentAttempt(
        SubjectiveMark? mark,
        SubjectiveResponseKind subjectiveResponse)
    {
        WritingMetrics metrics = _analyzer.Analyze(
            new WritingAttempt(_attemptNo, _recorder.Strokes));
        _currentAttempt = new WritingAttempt(
            _attemptNo,
            _recorder.Strokes,
            mark,
            metrics,
            subjectiveResponse);
        _subjectiveMark = mark;
        _subjectiveMatch = mark is null ? null : _subjectiveMatcher.Match(mark, metrics);
        MarkerCanvas.Children.Clear();
        if (mark is not null)
        {
            DrawSubjectiveMark(mark);
        }
        bool showCandidatesAutomatically =
            SubjectiveResponsePolicy.ShowsObservationCandidatesAutomatically(subjectiveResponse);
        if (showCandidatesAutomatically)
        {
            DrawObjectiveCandidates(metrics);
        }

        _mode = InteractionMode.Marked;
        WritingCanvas.IsHitTestVisible = false;
        SubjectiveLabelBox.IsEnabled = false;
        MarkButton.IsEnabled = false;
        ReplayButton.IsEnabled = true;

        if (_attemptNo == 1)
        {
            MarkButton.Content = "位置を選び直す";
            MarkButton.IsEnabled = mark is not null;
            ObservationChoicePanel.Visibility = showCandidatesAutomatically
                ? Visibility.Collapsed
                : Visibility.Visible;
            ObserveCandidatesButton.IsEnabled = true;
            SecondAttemptButton.IsEnabled = showCandidatesAutomatically;
            InitialMetricsText.Text = showCandidatesAutomatically
                ? FormatInitialMetrics(metrics)
                : "本人が「観測を見てみる」を選ぶまで、観測値は表示しません。";
            string relationText = showCandidatesAutomatically
                ? DescribeMatch(_subjectiveMatch)
                : DescribeAnswerPriority(subjectiveResponse);
            ComparisonText.Text = $"1回目の回答：{DescribeSubjectiveResponse(subjectiveResponse)}。{relationText}";
            ResultPanel.Visibility = Visibility.Visible;
            ResultTitleText.Text = showCandidatesAutomatically
                ? "書き方を見てみよう"
                : "あなたの答えをそのまま受け取りました";
            StepText.Text = showCandidatesAutomatically
                ? "4 / 5　書き方を見て、もう一度ためそう"
                : "できあがり　このまま終わることもできます";
            SaveCurrentSessionAsync();
            ShowFeedbackAsync(_currentAttempt, _subjectiveMatch, null);
            StatusText.Text = relationText;
            return;
        }

        if (_firstAttempt is null)
        {
            StatusText.Text = "1回目のデータがないため比較できません";
            return;
        }

        AttemptComparison comparison = _attemptComparer.Compare(_firstAttempt, _currentAttempt);
        SaveCurrentSessionAsync();
        ResultTitleText.Text = "2回の書き方を見てみよう";
        InitialMetricsText.Text = $"2回目の回答：{DescribeSubjectiveResponse(subjectiveResponse)}";
        ComparisonText.Text = FormatComparison(comparison);
        ResultPanel.Visibility = Visibility.Visible;
        StepText.Text = "できあがり　点数ではなく、書き方のちがいです";
        StatusText.Text = "2回の値の差を表示しました。正確さ、読みやすさ、能力は評価していません";
        ShowFeedbackAsync(_currentAttempt, null, comparison);
    }

    private void ObserveCandidatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentAttempt?.Metrics is null)
        {
            StatusText.Text = "表示できる観測値がありません";
            return;
        }

        bool candidateShown = DrawObjectiveCandidates(_currentAttempt.Metrics);
        InitialMetricsText.Text = FormatInitialMetrics(_currentAttempt.Metrics);
        ObserveCandidatesButton.IsEnabled = false;
        SecondAttemptButton.IsEnabled = true;
        StepText.Text = "任意の観測を表示しました。このまま終わることもできます";
        StatusText.Text = candidateShown
            ? "本人が選んだため、線をかえていた時間の候補を表示しました"
            : "この試行には表示条件を満たす観測候補がありませんでした";
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        ObservationChoicePanel.Visibility = Visibility.Collapsed;
        SecondAttemptButton.IsEnabled = false;
        ResultTitleText.Text = "これで終わりです";
        StepText.Text = "できあがり";
        StatusText.Text = "回答をそのまま受け取り、通常終了しました。いつでも新しく始められます";
    }

    private void DrawSubjectiveMark(SubjectiveMark mark)
    {
        var ellipse = new Ellipse
        {
            Width = mark.RadiusPx * 2,
            Height = mark.RadiusPx * 2,
            Stroke = Brushes.DarkOrange,
            StrokeThickness = 3
        };
        Canvas.SetLeft(ellipse, mark.CenterX - mark.RadiusPx);
        Canvas.SetTop(ellipse, mark.CenterY - mark.RadiusPx);
        MarkerCanvas.Children.Add(ellipse);
    }

    private bool DrawObjectiveCandidates(WritingMetrics metrics)
    {
        PauseMetrics? pause = metrics.Pauses
            .Where(item => item.IsLongPause)
            .OrderByDescending(item => item.DurationUs)
            .FirstOrDefault();
        if (pause is not null)
        {
            DrawPauseEndpoints(pause);
            return true;
        }

        return false;
    }

    private void DrawPauseEndpoints(PauseMetrics pause)
    {
        Brush brush = Brushes.RoyalBlue;
        DrawEndpoint(pause.BeforePoint, brush);
        DrawEndpoint(pause.AfterPoint, brush);

        var text = new TextBlock
        {
            Text = $"線をかえていた時間：{pause.DurationUs / 1_000:0} ms（この試行内の候補）",
            Foreground = brush,
            Background = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(3, 1, 3, 1)
        };
        Canvas.SetLeft(text, Math.Min(pause.BeforePoint.X, pause.AfterPoint.X) + 6);
        Canvas.SetTop(text, Math.Min(pause.BeforePoint.Y, pause.AfterPoint.Y) + 6);
        MarkerCanvas.Children.Add(text);
    }

    private void DrawEndpoint(PenPointSample point, Brush brush)
    {
        var endpoint = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = Brushes.White,
            Stroke = brush,
            StrokeThickness = 3
        };
        Canvas.SetLeft(endpoint, point.X - 6);
        Canvas.SetTop(endpoint, point.Y - 6);
        MarkerCanvas.Children.Add(endpoint);
    }

    private async void ReplayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.Strokes.Count == 0 || _replayCancellation is not null)
        {
            return;
        }

        _replayCancellation = new CancellationTokenSource();
        CancellationToken token = _replayCancellation.Token;
        ReplayButton.IsEnabled = false;
        WritingCanvas.IsHitTestVisible = false;
        WritingCanvas.Strokes.Clear();
        MarkerCanvas.Children.Clear();
        StatusText.Text = "再生中…";

        try
        {
            InkStroke? currentStroke = null;
            foreach (ReplayFrame frame in StrokeReplayTimeline.Create(_recorder.Strokes))
            {
                int delayMs = (int)Math.Min(frame.DelayUs / 1_000, MaximumReplayDelayMs);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, token);
                }

                var point = new StylusPoint(
                    frame.Point.X,
                    frame.Point.Y,
                    frame.Point.Pressure ?? 0.5f);
                if (frame.StartsStroke || currentStroke is null)
                {
                    var points = new StylusPointCollection { point };
                    currentStroke = new InkStroke(points, CopyDrawingAttributes());
                    WritingCanvas.Strokes.Add(currentStroke);
                }
                else
                {
                    currentStroke.StylusPoints.Add(point);
                }
            }

            if (_subjectiveMark is not null)
            {
                DrawSubjectiveMark(_subjectiveMark);
            }
            if (_currentAttempt?.Metrics is not null)
            {
                DrawObjectiveCandidates(_currentAttempt.Metrics);
            }

            StatusText.Text = $"再生完了：{_recorder.Strokes.Count} 画";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "再生を中止しました";
        }
        finally
        {
            _replayCancellation.Dispose();
            _replayCancellation = null;
            ReplayButton.IsEnabled = _recorder.Strokes.Count > 0;
            WritingCanvas.IsHitTestVisible = _mode == InteractionMode.Writing;
        }
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
        _mode = InteractionMode.Writing;
        WritingCanvas.Strokes.Clear();
        WritingCanvas.IsHitTestVisible = true;
        MarkerCanvas.Children.Clear();
        ReplayButton.IsEnabled = false;
        MarkButton.Content = "書けた・ふり返る";
        MarkButton.IsEnabled = false;
        MarkButton.Visibility = Visibility.Visible;
        SubjectiveLabelBox.SelectedIndex = 3;
        SubjectiveLabelBox.IsEnabled = true;
        SubjectiveLabelBox.Visibility = Visibility.Visible;
        SecondAttemptButton.Visibility = Visibility.Collapsed;
        CompareButton.Visibility = Visibility.Collapsed;
        CompareButton.IsEnabled = false;
        ResultPanel.Visibility = Visibility.Collapsed;
        StepText.Text = "5 / 5　もう一度書いてみよう";
        StatusText.Text = "2回目を書いてください。書き終えたら同じ振り返り質問へ回答します";
    }

    private void CompareButton_Click(object sender, RoutedEventArgs e)
    {
        MarkButton_Click(sender, e);
    }

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        Guid sessionToDelete = _sessionId;
        ResetSessionState();
        try
        {
            await _sessionRepository.DeleteAsync(sessionToDelete);
            StatusText.Text = "セッションを削除しました。白い書字エリアに Surface Pen で書いてください";
        }
        catch (Exception)
        {
            StatusText.Text = "画面は消去しましたが、端末内ファイルの削除に失敗しました";
        }
    }

    private async void DeleteAllButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult answer = MessageBox.Show(
            "WriteMirror が保存したすべてのセッション JSON を削除します。元に戻せません。続けますか？",
            "保存済み全セッション削除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            StatusText.Text = "全セッション削除をキャンセルしました";
            return;
        }

        try
        {
            await _sessionRepository.DeleteAllAsync();
            ResetSessionState();
            StatusText.Text = "保存済みセッションをすべて削除しました。Sessions フォルダーに JSON は残っていません";
        }
        catch (Exception error)
        {
            StatusText.Text = $"保存済みセッションをすべて削除できませんでした：{error.Message}";
        }
    }

    private void SaveConsentCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_currentAttempt is not null)
        {
            SaveCurrentSessionAsync();
        }
    }

    private async void SaveConsentCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        try
        {
            await _sessionRepository.DeleteAsync(_sessionId);
            if (CurrentUsageMode == UsageMode.GuidedReview)
            {
                StatusText.Text = "このセッションの保存をやめ、端末内の現在データを消しました";
            }
        }
        catch (Exception)
        {
            StatusText.Text = "端末内の現在データを消せませんでした。いっしょに確認する人へ知らせてください";
        }
    }

    private async void DemoButton_Click(object sender, RoutedEventArgs e)
    {
        Guid oldSession = _sessionId;
        ResetSessionState();
        try
        {
            await _sessionRepository.DeleteAsync(oldSession);
        }
        catch (Exception)
        {
            // 固定デモはファイル削除に失敗しても利用できます。
        }

        RecognizedTextBox.Text = "木";
        CandidateSelector.ItemsSource = new[] { "木" };
        CandidateSelector.SelectedIndex = 0;
        CandidateSelector.IsEnabled = true;
        RecognizeButton.IsEnabled = true;
        SpeakButton.IsEnabled = true;
        HandednessSelector.IsEnabled = false;
        DemoButton.IsEnabled = false;
        LoadDemoStrokes();
        MarkButton.IsEnabled = true;
        SubjectiveLabelBox.IsEnabled = true;
        StepText.Text = "2 / 5　書いたあとの気もちをえらぼう";
        StatusText.Text = "デモ「木」を読み込みました。先に振り返りの回答を選んでください";
    }

    private void LoadDemoStrokes()
    {
        PenPointSample[][] strokes =
        [
            [DemoPoint(190, 120, 1_000_000), DemoPoint(480, 120, 1_420_000)],
            [DemoPoint(335, 55, 1_680_000), DemoPoint(335, 340, 2_160_000)],
            [DemoPoint(330, 180, 2_430_000), DemoPoint(215, 325, 2_820_000)],
            [DemoPoint(340, 180, 3_520_000), DemoPoint(475, 330, 3_900_000)]
        ];

        foreach (PenPointSample[] samples in strokes)
        {
            _recorder.StartStroke();
            var points = new StylusPointCollection();
            foreach (PenPointSample sample in samples)
            {
                _recorder.AddPoint(sample);
                points.Add(new StylusPoint(sample.X, sample.Y, sample.Pressure ?? 0.5f));
            }

            _recorder.EndStroke();
            WritingCanvas.Strokes.Add(new InkStroke(points, CopyDrawingAttributes()));
        }
    }

    private DrawingAttributes CopyDrawingAttributes() => WritingCanvas.DefaultDrawingAttributes.Clone();

    private static PenPointSample DemoPoint(double x, double y, long timestampUs) =>
        new(x, y, timestampUs, 0.5f, null, null, true);

    private void ResetSessionState()
    {
        _replayCancellation?.Cancel();
        _saveCancellation.Cancel();
        _saveCancellation.Dispose();
        _saveCancellation = new CancellationTokenSource();
        _sessionId = Guid.NewGuid();
        _sessionStartedAt = DateTimeOffset.Now;
        _recorder.Reset();
        _mode = InteractionMode.Writing;
        _attemptNo = 1;
        _strokeStartedUs = null;
        _activeInputName = "Windows Ink";
        _subjectiveMark = null;
        _subjectiveMatch = null;
        _currentAttempt = null;
        _firstAttempt = null;
        WritingCanvas.Strokes.Clear();
        WritingCanvas.IsHitTestVisible = true;
        MarkerCanvas.Children.Clear();
        ReplayButton.IsEnabled = false;
        MarkButton.Content = "書けた・ふり返る";
        MarkButton.IsEnabled = false;
        MarkButton.Visibility = Visibility.Visible;
        SubjectiveLabelBox.IsEnabled = false;
        SubjectiveLabelBox.Visibility = Visibility.Visible;
        SecondAttemptButton.IsEnabled = false;
        SecondAttemptButton.Visibility = Visibility.Visible;
        CompareButton.IsEnabled = false;
        CompareButton.Visibility = Visibility.Collapsed;
        HandednessSelector.IsEnabled = true;
        DemoButton.IsEnabled = true;
        RecognizedTextBox.Text = string.Empty;
        SaveConsentCheckBox.IsChecked = false;
        SubjectiveLabelBox.SelectedIndex = 3;
        CandidateSelector.ItemsSource = null;
        CandidateSelector.IsEnabled = false;
        RecognizeButton.IsEnabled = false;
        SpeakButton.IsEnabled = false;
        ResultPanel.Visibility = Visibility.Collapsed;
        ObservationChoicePanel.Visibility = Visibility.Collapsed;
        ObserveCandidatesButton.IsEnabled = true;
        InitialMetricsText.Text = string.Empty;
        ComparisonText.Text = string.Empty;
        ResultTitleText.Text = "書き方を見てみよう";
        StepText.Text = "1 / 5　文字を書こう";
    }

    private async void SaveCurrentSessionAsync()
    {
        if (!SessionDataPolicy.CanPersist(
                CurrentUsageMode,
                SaveConsentCheckBox.IsChecked == true))
        {
            return;
        }

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
            await _sessionRepository.SaveAsync(session, _saveCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // セッションのリセット時は保存を中止します。
        }
        catch (Exception)
        {
            StatusText.Text = "分析は完了しましたが、端末内 JSON の保存に失敗しました";
        }
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
                .Where(pause => pause.IsLongPause)
                .OrderByDescending(pause => pause.DurationUs)
                .FirstOrDefault();
            SubjectiveEventMatch? pauseMatch = match?.Events.FirstOrDefault(
                item => item.EventKind == ObjectiveEventKind.LongestPause);

            var request = new FeedbackRequest(
                "ja-JP",
                SelectedTaskDisplayName(),
                longestPause is null ? null : longestPause.BeforeStrokeIndex + 1,
                longestPause is null ? null : longestPause.AfterStrokeIndex + 1,
                longestPause is null ? null : longestPause.DurationUs / 1_000,
                pauseMatch?.Relation,
                pauseMatch?.EventKind,
                comparison?.TotalDurationUs.PercentChange,
                comparison?.HasComparableStrokeStructure,
                GetPressureTrend(comparison?.PressureVariability),
                firstAttempt.SubjectiveResponse ?? SubjectiveResponseKind.Skipped);
            FeedbackMessage feedback = await _feedbackGenerator.GenerateAsync(request);
            FeedbackObservationText.Text = feedback.Observation;
            FeedbackReflectionText.Text = feedback.Reflection;
            FeedbackQuestionText.Text = feedback.NextQuestion;
            FeedbackGeneratorText.Text = "生成方法：端末内の日本語テンプレート";
            SpeakButton.IsEnabled = true;
            ResultPanel.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            FeedbackObservationText.Text = "フィードバックは現在利用できません。";
            FeedbackReflectionText.Text = "筆跡の観測値は画面上で引き続き確認できます。";
            FeedbackQuestionText.Text = "軌跡と基本指標を引き続き確認できます。";
            FeedbackGeneratorText.Text = "生成方法：安全な回復メッセージ";
            SpeakButton.IsEnabled = true;
            ResultPanel.Visibility = Visibility.Visible;
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

        return delta.AbsoluteChange < 0 ? PressureTrend.Decreased : PressureTrend.Increased;
    }

    private string SelectedTaskId()
    {
        string text = RecognizedTextBox.Text.Trim().ReplaceLineEndings(string.Empty);
        return string.IsNullOrWhiteSpace(text) ? "japanese_unspecified" : $"japanese:{text}";
    }

    private string SelectedTaskDisplayName()
    {
        string text = RecognizedTextBox.Text.Trim();
        return string.IsNullOrWhiteSpace(text) ? "未認識文字" : text;
    }

    private Handedness SelectedHandedness() =>
        HandednessSelector.SelectedIndex == 1 ? Handedness.Left : Handedness.Right;

    private SubjectiveLabel SelectedSubjectiveLabel() => SubjectiveLabelBox.SelectedIndex switch
    {
        1 => SubjectiveLabel.Difficult,
        2 => SubjectiveLabel.Dissatisfied,
        _ => SubjectiveLabel.Hesitation
    };

    private SubjectiveResponseKind SelectedSubjectiveResponseKind() =>
        SubjectiveLabelBox.SelectedIndex switch
    {
        1 => SubjectiveResponseKind.Difficult,
        2 => SubjectiveResponseKind.Dissatisfied,
        3 => SubjectiveResponseKind.None,
        4 => SubjectiveResponseKind.WentWell,
        5 => SubjectiveResponseKind.Skipped,
        _ => SubjectiveResponseKind.Hesitation
    };

    private static string DescribeSubjectiveResponse(SubjectiveResponseKind response) =>
        response switch
        {
            SubjectiveResponseKind.Difficult => "書きにくい",
            SubjectiveResponseKind.Dissatisfied => "気になった",
            SubjectiveResponseKind.None => "特になし",
            SubjectiveResponseKind.WentWell => "うまくいった",
            SubjectiveResponseKind.Skipped => "答えない",
            _ => "ためらった"
        };

    private static string DescribeAnswerPriority(SubjectiveResponseKind response) =>
        response switch
        {
            SubjectiveResponseKind.None =>
                "「特になし」を優先し、観測候補は自動表示していません。",
            SubjectiveResponseKind.WentWell =>
                "「うまくいった」を優先し、観測候補は自動表示していません。",
            SubjectiveResponseKind.Skipped =>
                "回答しない選択を優先し、位置や理由を推測していません。",
            _ => "本人の回答をそのまま受け取りました。"
        };

    private static string DescribeMatch(SubjectiveMatchResult? result)
    {
        if (result is null || result.Events.Count == 0)
        {
            return "今回は信頼条件を満たす観測候補がありません";
        }

        return string.Join(" ", result.Events
            .OrderBy(item => item.EventKind)
            .Select(item =>
            {
                const string eventName = "線をかえていた時間の端点候補";
                string relation = item.Relation switch
                {
                    SpatialRelation.Inside => "選んだ範囲と重なります",
                    SpatialRelation.Near => "選んだ範囲の近くです",
                    _ => "選んだ範囲とは別の位置です"
                };
                return $"{eventName}は{relation}。";
            })) + "これは困難や能力との一致を意味しません。";
    }

    private static string FormatInitialMetrics(WritingMetrics metrics)
    {
        PauseMetrics? candidate = metrics.Pauses
            .Where(pause => pause.IsLongPause)
            .OrderByDescending(pause => pause.DurationUs)
            .FirstOrDefault();
        string pause = candidate is null
            ? "この試行内で相対的に長い画間空白候補：なし"
            : $"第{candidate.BeforeStrokeIndex + 1}画終了〜第{candidate.AfterStrokeIndex + 1}画開始：{candidate.DurationUs / 1_000:0} ms（観測候補）";
        string pressure = metrics.Pressure is null
            ? "端末のペン先圧：利用不可"
            : $"端末が取得した平均ペン先圧：{metrics.Pressure.Mean:0.00}、変動：{metrics.Pressure.PopulationStandardDeviation:0.000}";
        return $"総時間：{metrics.TotalDurationUs / 1_000:0} ms · {metrics.Strokes.Count} 画 · {pause} · {pressure}";
    }

    private static string FormatComparison(AttemptComparison comparison)
    {
        string duration = FormatDelta(
            comparison.TotalDurationUs,
            value => $"{value / 1_000:0} ms",
            minimumAbsoluteDifference: 100_000);
        string pressure = comparison.PressureVariability is null
            ? "ペン先圧の変動：比較データ不足"
            : $"ペン先圧の変動：{FormatDelta(comparison.PressureVariability, value => value.ToString("0.000"), 0.01)}";
        string structure = comparison.HasComparableStrokeStructure
            ? $"筆画数：{comparison.FirstStrokeCount} → {comparison.SecondStrokeCount}（逐筆比較可能）"
            : $"筆画数：{comparison.FirstStrokeCount} → {comparison.SecondStrokeCount}（構造が異なるため総量のみ比較）";
        return $"総書字時間：{duration} · {pressure} · {structure}";
    }

    private static string FormatDelta(
        MetricDelta delta,
        Func<double, string> format,
        double minimumAbsoluteDifference)
    {
        string percent = delta.PercentChange is null
            ? "割合は計算できません"
            : $"{delta.PercentChange.Value:+0.0;-0.0;0.0}%";
        bool clearDifference = delta.PercentChange is not null &&
            Math.Abs(delta.PercentChange.Value) >= 5 &&
            Math.Abs(delta.AbsoluteChange) >= minimumAbsoluteDifference;
        string interpretation = clearDifference
            ? "差が観測されました（良否は判定しません）"
            : "明確な差なし";
        return $"{format(delta.FirstValue)} → {format(delta.SecondValue)}（{percent}、{interpretation}）";
    }

    private static long NowUs() =>
        checked(Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency);

    private enum InteractionMode
    {
        Writing,
        Marking,
        Marked
    }
}
