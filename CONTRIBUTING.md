# Contributing to WriteMirror

Thank you for considering a contribution. WriteMirror is a research prototype
for privacy-preserving, on-device reflection on Japanese handwriting. The
project welcomes focused bug fixes, tests, accessibility improvements,
documentation, packaging improvements, and carefully scoped research tooling.

## Project boundaries

Contributions must preserve these boundaries:

- WriteMirror is not a diagnostic, treatment, ability-scoring, grading, or
  medical system.
- Do not present reconstruction error or other observations as evidence of a
  child's difficulty, disability, correctness, improvement, or educational
  outcome.
- Do not upload children's handwriting, personal information, confidential
  research records, school records, or other sensitive data to issues, pull
  requests, test fixtures, logs, or external AI services.
- Use synthetic data or clearly licensed public data for development and tests.
- Preserve the consent-first flow, the default no-save independent-practice
  mode, the ability to stop, and the separation between Windows handwriting
  recognition and the trajectory reconstruction model.
- Avoid adding cloud inference, analytics, accounts, or telemetry without an
  explicit design discussion and a documented privacy review.

## Before starting

For a substantial feature or behavior change, open an issue first and describe:

1. the user or maintainer problem;
2. the proposed behavior;
3. privacy, accessibility, research-validity, and licensing implications;
4. how the change will be tested.

Small documentation fixes and narrowly scoped bug fixes may go directly to a
pull request.

## Development setup

The main application targets Windows 11 and .NET 9. The verified ARM64 path is
`WriteMirror.Wpf`; the WinUI project is retained as a technical prototype.

Typical validation commands are:

```powershell
dotnet build src\WriteMirror.Wpf\WriteMirror.Wpf.csproj -c Release -r win-arm64
dotnet test tests\WriteMirror.Core.Tests\WriteMirror.Core.Tests.csproj -c Release
```

When relevant, also build the x64 target and state clearly whether you performed
hardware testing. Do not claim QNN or Hexagon NPU execution unless the runtime
provider and no-CPU-fallback behavior were actually verified.

## Pull request checklist

A pull request should:

- explain what changed and why;
- keep unrelated changes out of the same PR;
- add or update tests for behavior changes;
- pass the applicable build and test commands;
- document hardware, operating-system, and test limitations;
- contain no credentials, private certificates, personal data, or sensitive
  handwriting samples;
- update README, model cards, or license notices when behavior, dependencies,
  data provenance, or redistribution terms change.

## Data and model contributions

Do not add a dataset, checkpoint, or model artifact unless the pull request
records:

- its source and version;
- copyright holder and license;
- redistribution permission;
- processing and training steps;
- cryptographic hash for distributed artifacts;
- intended use and explicit non-uses;
- evaluation limitations.

KanjiVG source data and WriteMirror's KanjiVG-derived artifacts are governed by
CC BY-SA 3.0 as described in `THIRD_PARTY_NOTICES.md`. Do not mix those
artifacts with material whose terms are incompatible or unclear.

## License of contributions

Unless you explicitly state otherwise, a contribution intentionally submitted
for inclusion in project-authored code or documentation is provided under the
Apache License, Version 2.0, in accordance with Section 5 of that license.
Contributions to separately licensed data or model areas must be clearly marked
and must preserve the applicable upstream terms.

## 日本語要約

WriteMirror への貢献では、診断・採点・能力評価を行わないこと、本人意思を
優先すること、独立練習で保存しないこと、児童の筆跡や個人情報を Issue・PR・
テスト・外部AIサービスへ含めないことを守ってください。大きな変更は先に
Issueで目的、プライバシー、アクセシビリティ、研究上の制約、ライセンス、
検証方法を相談してください。

作者が作成したコード・文書への通常の貢献は Apache License 2.0 で提供されます。
KanjiVG由来のデータ・モデルは `THIRD_PARTY_NOTICES.md` に記載した
CC BY-SA 3.0 の条件を維持してください。
