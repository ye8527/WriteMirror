# WriteMirror AI・NPU実機検証

検証日時：2026年7月19日 01:56（日本標準時）

## 結論

Windows MLが管理する認定済みQualcomm QNN実行プロバイダーをSurface実機で取得・登録し、WriteMirror独自の量子化ONNX軌跡モデルをQualcomm Hexagon NPUで推論できた。最終ARM64 MSIXでは、OSの日本語手書き認識、本人意思優先フロー、任意AI表示まで連続回帰に成功した。これは実装経路の成立を示すが、児童への有効性、教育効果または診断性能を示すものではない。

## 実機と実行経路

- 端末：Surface Pro 11
- SoC：Snapdragon X Elite X1E80100
- NPU：Qualcomm Hexagon NPU
- NPUドライバー：30.0.220.3000
- OS：Windows 11 Pro、ビルド26200
- アプリ：WriteMirror 0.6.0.4、WPF、ARM64、MSIX
- Windows App SDK ML：2.1.70
- 実行プロバイダー：Microsoft認定 `QNNExecutionProvider`

## Readinessの実測遷移

```text
QNNExecutionProvider  NotPresent または NotReady
EnsureReadyAsync      Success
Registered            Ready
```

Windows ML登録後にONNX Runtimeが列挙したデバイスは次のとおりである。

```text
CPUExecutionProvider / CPU
DmlExecutionProvider / GPU
QNNExecutionProvider / NPU
QNNExecutionProvider / GPU
QNNExecutionProvider / CPU
```

## モデルと推論結果

- モデル：WriteMirror Fixed Trajectory Autoencoder
- 学習元：KanjiVG 11,662件、CC BY-SA 3.0
- 入力：128点×正規化X・正規化Y・筆画終端、形状 `[1,384]`
- 構成：`384→128→32→128→384`
- 形式：静的形状QDQ INT8 ONNX
- SHA-256：`8F24114713A208753C5E088D50AC2A3D3980E71905BDF7C90F6BB76B68C2E9C3`
- NPU確認：QNN NPUデバイスを明示選択し、`session.disable_cpu_ep_fallback=1`
- 0.6.0.4インストール回帰の起動時確認推論：11.607 ms、再構成差0.182712
- デモ筆跡20回：中央値1.524 ms、P95 5.400 ms、最小0.898 ms、最大6.677 ms
- デモ筆跡の再構成差：全20回0.024576
- 20回連続時の最大ワーキングセット：1,087.2 MiB、最大プライベートメモリー：1,023.4 MiB

CPUフォールバックを無効にしたセッションでモデル生成と全20回の推論が成功したため、これらはQNNのNPU経路で実行された。メモリーは10回前後で約1.07～1.09 GiBの範囲へ達した後、20回目まで線形増加しなかった。ただし自包含Windows ML/QNN実行時の占有量は大きく、端末間比較や製品性能保証には使用しない。

本人意思優先の回帰確認では、既存ログ件数が中立回答の確定前後で5件のまま変わらず、本人が「観測を見てみる（任意）」を押した後だけ6件になった。Windows日本語手書き認識は同じデモ軌跡から「木」を取得し、AIプレビューも表示された。独立練習の前後で保存済みJSONは2件のまま、パス、長さ、SHA-256がすべて不変だった。

## 安全上の意味づけ

モデルは公開された整形済み筆順軌跡を再構成する自己教師ありモデルである。実測の児童データ、困難度、診断、能力、感情、学年、改善ラベルを学習していない。再構成差は書字の正しさ、良否、読みやすさ、困難、能力または教育効果を示さず、本人の回答を上書きしない。

## 残作業

- 別のSnapdragon X搭載SurfaceでARM64配布パッケージを確認する
- 実際のSurface Penによる0.6.0全操作を別途最終回帰確認する
- Intel／AMD端末ではCPUモードを実機確認する
