# 日本語筆跡軌跡モデルの学習実験

標準筆順との参照比較に使う、小規模な自己教師あり軌跡エンコーダーの実験です。生の `(x, y, pen-state)` 系列は系列モデルで処理し、Phi Silica には検証済みの集計事実だけを渡します。

## データ

- 取得元: [KanjiVG](https://kanjivg.tagaini.net/) の `master` ブランチアーカイブ
- ライセンス: CC BY-SA 3.0。帰属表示と継承条件を守る必要があります。
- アーカイブ SHA-256: `031805B15E51DAEDC5E050782F86602519B7D713BC9A726B326748BCCA8FA525`
- ローカルコーパス: SVG 11,662 件

KanjiVG は整形済みの筆順参照データであり、多数の書き手から Surface または Wacom で収録したオンライン筆跡ではありません。実測の筆圧、傾き、ためらい、利き手、診断ラベルは含みません。

Kanji Alive も公開参照候補です。日本語母語話者が Wacom タブレットで書いた 1,235 件のアニメーションが CC BY 4.0 で公開されています。ただし公開 SVG は完成筆画画像であり、生のポインター列ではないため、今回の軌跡モデルには混在させていません。

## 再現手順

Python 3.11 以降を使用します。この ARM64 Surface では、Windows 11 の x64 エミュレーション上で x64 Python 3.12 と CPU 版 PyTorch 2.7.1 を使用して検証しました。

```powershell
python -m venv .venv
.venv\Scripts\python -m pip install -r experiments\japanese_trajectory\requirements.txt
.venv\Scripts\python experiments\japanese_trajectory\prepare_kanjivg.py
.venv\Scripts\python experiments\japanese_trajectory\train.py --epochs 30
```

既存チェックポイントを「あ」「木」「語」に追加学習する例です。

```powershell
.venv\Scripts\python experiments\japanese_trajectory\train.py `
  --init-checkpoint experiments\japanese_trajectory\artifacts\pretrained.pt `
  --characters あ木語 --epochs 10 --learning-rate 0.0001 `
  --output experiments\japanese_trajectory\artifacts\demo-finetuned.pt
```

モデルはノイズ除去型の双方向 GRU エンコーダーです。座標に小さなノイズを加え、元の軌跡を再構成するよう学習します。元データに存在しない困難度、障害、診断ラベルを推測するモデルではありません。

処理後の JSONL は、将来の Surface エクスポートと接続しやすい形式です。

```json
{"character":"木","source":"kanjivg","strokes":[[[0.1,0.2,0.0],[0.2,0.2,1.0]]]}
```

第3要素は筆画末尾だけ `1` です。同意を得た実測データが利用可能になれば、筆圧、傾き、時刻を追加特徴として拡張できます。

## 検証結果（2026-07-18）

- 前処理: 11,662 件を JSONL 化、スキップ 0 件
- 全コーパス事前学習: CPU 1 エポック、学習損失 `0.111577`、検証損失 `0.029064`
- デモ文字追加学習: 「あ」「木」「語」に一致する KanjiVG 4 件を、学習率 `0.0001` で 10 エポック
- 決定的再構成損失: 追加学習前 `0.029725`、追加学習後 `0.029579`（約 0.49% 低下）
- チェックポイント: `artifacts/demo-finetuned.pt`
- SHA-256: `87E3D6EC6DA6E235CE4D0245B728707260AED2C1EA794C3A210F7224E3C4EF76`

この結果は学習パイプラインが動作することを示すだけで、実利用時の精度を保証しません。評価には、同意を得た複数書き手の保留データを用い、個々の軌跡ではなく書き手単位で学習・検証・テストを分割する必要があります。
