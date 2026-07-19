# Third-Party Notices and License Scope

Last updated: 2026-07-19

This document identifies material that is not relicensed by the repository's
root `LICENSE` file. It is a practical attribution record, not legal advice.
When an upstream license or product term conflicts with this summary, the
upstream terms control.

## 1. WriteMirror-authored material

Unless an individual file says otherwise, original source code, tests, build
and release scripts, and documentation authored for WriteMirror are:

- Copyright 2026 Feng YE
- Licensed under the Apache License, Version 2.0

See [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE).

The Apache-2.0 grant does **not** relicense third-party components, operating
system APIs, hardware services, datasets, or the KanjiVG-derived artifacts
listed below.

## 2. KanjiVG and derived artifacts

WriteMirror uses KanjiVG as public stroke-order reference data.

- Upstream project: <https://github.com/KanjiVG/kanjivg>
- Project website: <https://kanjivg.tagaini.net/>
- Copyright: © 2009-2026 Ulrich Apel
- Upstream license: Creative Commons Attribution-ShareAlike 3.0 Unported
  (`CC BY-SA 3.0`)
- License text: <https://creativecommons.org/licenses/by-sa/3.0/legalcode>

WriteMirror transforms KanjiVG SVG paths by extracting stroke geometry,
normalizing coordinates, resampling trajectories, encoding
`(normalized_x, normalized_y, stroke_end)`, training self-supervised denoising
reconstruction models, and, for the bundled NPU model, exporting a static-shape
QDQ INT8 ONNX artifact.

The following KanjiVG-derived artifacts are made available under CC BY-SA 3.0:

- `src/WriteMirror.App/Models/trajectory-autoencoder-qdq-int8.onnx`
- `src/WriteMirror.App/Models/trajectory-autoencoder-model-card.json`
- any redistributed KanjiVG-derived datasets, checkpoints, or generated model
  artifacts under `experiments/japanese_trajectory/`

Redistributors must preserve attribution, identify their changes, provide a
link or copy of CC BY-SA 3.0, and apply the required ShareAlike terms to adapted
KanjiVG-derived material. Neither Ulrich Apel nor the KanjiVG project endorses
WriteMirror.

## 3. Direct software dependencies

The repository references or uses the following external projects. Versions
are recorded in the project files and may change. This table is a convenience
summary; consult each package's distributed license and third-party notices.

| Component | Use in WriteMirror | Upstream license or terms |
|---|---|---|
| .NET / WPF / Windows SDK | Application runtime, UI, Windows APIs | Microsoft product and repository terms; see <https://github.com/dotnet/runtime> |
| Microsoft Windows App SDK / `Microsoft.WindowsAppSDK.ML` | Windows App SDK, Windows ML execution-provider management | Upstream Windows App SDK is MIT; package-specific notices also apply: <https://github.com/microsoft/WindowsAppSDK> |
| ONNX Runtime | Local ONNX inference | MIT: <https://github.com/microsoft/onnxruntime/blob/main/LICENSE> |
| ONNX | Model export and validation during development | Apache-2.0: <https://github.com/onnx/onnx/blob/main/LICENSE> |
| PyTorch | Self-supervised model training | BSD-style license and bundled third-party notices: <https://github.com/pytorch/pytorch/blob/main/LICENSE> |
| NumPy | Numerical processing | BSD-3-Clause: <https://github.com/numpy/numpy/blob/main/LICENSE.txt> |
| svgpathtools | SVG path sampling | MIT: <https://github.com/mathandy/svgpathtools/blob/master/LICENSE> |
| MSTest | Unit testing | Upstream package terms: <https://github.com/microsoft/testfx> |

## 4. Platform and hardware services

The following are external platform or hardware services, not works licensed by
this repository:

- Microsoft Windows 11, Windows Ink, Japanese handwriting recognition, and
  Japanese text-to-speech
- Microsoft Surface hardware and Surface Pen
- Qualcomm QNN Execution Provider and Hexagon NPU support obtained through
  Windows ML

Use of those products and services remains subject to the applicable Microsoft,
Qualcomm, hardware, operating-system, and package terms.

## 5. Binary distributions

A source or binary release should include at least:

- `LICENSE`
- `NOTICE`
- `THIRD_PARTY_NOTICES.md`
- the model card accompanying the bundled ONNX artifact

A redistributor is responsible for retaining all notices and license materials
required by the exact dependency and artifact set it distributes.
