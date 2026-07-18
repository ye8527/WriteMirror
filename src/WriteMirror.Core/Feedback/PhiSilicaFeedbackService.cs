using System.Text.Json;
using System.Text.Json.Serialization;
using WriteMirror.Core.Models;

namespace WriteMirror.Core.Feedback;

/// <summary>
/// Optional Phi Silica adapter over an injected platform bridge. Any capability,
/// timeout, JSON, schema, or safety failure returns the deterministic template.
/// </summary>
public sealed class PhiSilicaFeedbackService : IFeedbackGenerator
{
    private readonly ILocalLanguageModel _localModel;
    private readonly IFeedbackGenerator _fallback;
    private readonly FeedbackSafetyValidator _validator;
    private readonly TimeSpan _timeout;

    public PhiSilicaFeedbackService(
        ILocalLanguageModel localModel,
        IFeedbackGenerator fallback,
        FeedbackSafetyValidator? validator = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(localModel);
        ArgumentNullException.ThrowIfNull(fallback);
        _localModel = localModel;
        _fallback = fallback;
        _validator = validator ?? new FeedbackSafetyValidator();
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async Task<FeedbackMessage> GenerateAsync(
        FeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            bool available = await _localModel
                .IsAvailableAsync(timeout.Token)
                .WaitAsync(timeout.Token);
            if (!available)
            {
                return await _fallback.GenerateAsync(request, cancellationToken);
            }

            string json = await _localModel
                .GenerateJsonAsync(CreatePrompt(request), timeout.Token)
                .WaitAsync(timeout.Token);
            ModelFeedbackDto? parsed = JsonSerializer.Deserialize<ModelFeedbackDto>(json);
            if (parsed is null || parsed.Extra is { Count: > 0 })
            {
                return await _fallback.GenerateAsync(request, cancellationToken);
            }

            var message = new FeedbackMessage(
                "phi-silica",
                parsed.Observation ?? string.Empty,
                parsed.Reflection ?? string.Empty,
                parsed.NextQuestion ?? string.Empty,
                parsed.SafetyFlags);
            return _validator.IsValid(request, message)
                ? message
                : await _fallback.GenerateAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await _fallback.GenerateAsync(request, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await _fallback.GenerateAsync(request, cancellationToken);
        }
    }

    private static string CreatePrompt(FeedbackRequest request)
    {
        string facts = JsonSerializer.Serialize(request);
        return $$"""
            次の検証済み事実だけを使い、中立的な日本語で書字の振り返りを作成してください。
            診断、原因推測、能力評価、入力にない数値は禁止です。各項目は1文、JSONだけを返してください。
            SubjectiveResponseがNone、WentWell、Skippedの場合は選択位置を仮定しないでください。Skippedでは次の課題を提案しないでください。
            schema: {"observation":"string","reflection":"string","nextQuestion":"string","safetyFlags":["string"]}
            facts: {{facts}}
            """;
    }

    private sealed class ModelFeedbackDto
    {
        [JsonPropertyName("observation")]
        public string? Observation { get; set; }

        [JsonPropertyName("reflection")]
        public string? Reflection { get; set; }

        [JsonPropertyName("nextQuestion")]
        public string? NextQuestion { get; set; }

        [JsonPropertyName("safetyFlags")]
        public string[]? SafetyFlags { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }
}
