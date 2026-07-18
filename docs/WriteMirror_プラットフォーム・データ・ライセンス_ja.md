# WriteMirror 0.6.0 プラットフォーム・データ・ライセンス一覧

更新日：2026年7月19日

## チームが実装した範囲

| 項目 | 内容 |
|---|---|
| アプリケーション | WPFのペン入力画面、本人意思を優先する操作フロー、筆順再生、観測表示、2回比較、音声操作、保存制御 |
| 記録・分析 | 接触点、筆画終端、時刻、利用可能な筆圧・傾きの記録と、説明可能な固定規則による中立的な観測 |
| AI | 128点×3特徴量への固定長化、軌跡オートエンコーダーの設計・学習・量子化、Windows ML/QNN選択、CPUフォールバック禁止確認、任意表示の制御 |
| データ保護 | 独立練習時の保存禁止、共同確認時の明示選択、端末内JSON保存、同意解除時の削除 |
| 検証 | Core単体テスト、ARM64/x64ビルド、MSIX導入、Surface Pro 11でのQNN NPU推論、本人回答優先フローの自動操作確認 |

公開されていること自体は、第三者へチーム実装コードの再利用許諾を与えることを意味しません。リポジトリ全体の再利用ライセンスは現時点では設定しておらず、外部資源にはそれぞれのライセンスが適用されます。

## 外部プラットフォーム・API・ライブラリ

| 資源 | 本アプリでの利用 | ライセンス・利用条件 |
|---|---|---|
| Microsoft Surface Pro 11 / Surface Pen | ペン入力、筆圧・傾きの取得、ARM64実機デモ | ハードウェア。アプリへ第三者素材を再配布しない |
| Windows 11 / WPF / Windows Ink | 画面、ペン入力、筆画データ | Microsoft製品・.NETの各ライセンスに従う |
| Windows App SDK / Windows ML | `ExecutionProviderCatalog`、`EnsureReadyAsync`、ONNXセッション生成 | Microsoftの製品・NuGetパッケージ条件に従う |
| ONNX Runtime | ONNXモデルの端末内推論 | MIT License |
| Qualcomm QNN Execution Provider | Snapdragon上のHexagon NPU実行 | Windows MLが管理する認定実行プロバイダー。Qualcomm/Microsoftの条件に従う |
| Windows日本語手書き認識 | 平仮名・片仮名・漢字の文字候補 | OS機能。独自AIモデルとは別機能 |
| Windows日本語音声合成 | 説明と結果の読み上げ | OS機能。クラウド音声APIは使用しない |
| PyTorch 2.7.1 | 開発時の自己教師あり学習 | BSD-style License |
| NumPy 2.3.1 | 学習データの数値処理 | BSD 3-Clause License |
| svgpathtools 1.7.1 | KanjiVG SVG筆画のサンプリング | MIT License |

## データセットとモデル

| 項目 | 内容 |
|---|---|
| 学習データ | KanjiVG `master` のSVG 11,662件、重複を除く文字6,703種 |
| 出典 | [KanjiVG](https://kanjivg.tagaini.net/) |
| データライセンス | Creative Commons Attribution-ShareAlike 3.0（CC BY-SA 3.0） |
| アーカイブSHA-256 | `031805B15E51DAEDC5E050782F86602519B7D713BC9A726B326748BCCA8FA525` |
| 学習方式 | 正解ラベルを使わない自己教師ありノイズ除去再構成 |
| 入力 | 正規化した `(x, y, stroke_end)`、128点×3特徴量 |
| モデル | 全結合オートエンコーダー `384→128→32→128→384`、ReLU/Sigmoid |
| 配布形式 | 静的形状QDQ INT8 ONNX、115,925 bytes |
| モデルSHA-256 | `8F24114713A208753C5E088D50AC2A3D3980E71905BDF7C90F6BB76B68C2E9C3` |

KanjiVGは整形済みの漢字筆順参照データです。児童の実測筆跡、書字困難ラベル、診断情報、筆圧、傾き、平仮名・片仮名を含みません。このモデルについて、児童、平仮名、片仮名に対する精度または妥当性を主張しません。

## 実行時のデータ経路

- モデルはアプリへ同梱し、推論は端末内で行います。
- 筆跡、本人回答、AI入力、AI出力をクラウドへ送信しません。
- Windows MLの認定QNN実行プロバイダーが未導入の場合、初回準備時にWindows側がインターネットから取得する場合があります。これはモデル学習やクラウド推論ではありません。
- 既定の独立練習では筆跡をファイルへ保存しません。共同確認で明示的に選択した場合だけ端末内JSONへ保存します。
- アプリに広告、アナリティクス、利用者アカウント、外部AI API、APIキーはありません。

## 引用と公開時の注意

- KanjiVG由来データまたはその派生物を再配布する場合は、CC BY-SA 3.0の帰属表示と継承条件を維持します。
- 発表では、チーム実装、OS機能、学習データ、第三者ライブラリを区別して説明します。
- AI再構成差は診断、採点、能力、困難、改善または教育効果を示す値として使用しません。

## 一次資料

- [Microsoft Learn: Install Windows ML execution providers](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/initialize-execution-providers)
- [Microsoft Learn: ExecutionProvider.EnsureReadyAsync](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.machinelearning.executionprovider.ensurereadyasync)
- [ONNX Runtime LICENSE](https://github.com/microsoft/onnxruntime/blob/main/LICENSE)
- [KanjiVG repository and licence](https://github.com/KanjiVG/kanjivg)
- [PyTorch LICENSE](https://github.com/pytorch/pytorch/blob/main/LICENSE)
- [NumPy LICENSE](https://github.com/numpy/numpy/blob/main/LICENSE.txt)
- [svgpathtools LICENSE](https://github.com/mathandy/svgpathtools/blob/master/LICENSE)
