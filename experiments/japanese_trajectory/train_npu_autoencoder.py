"""Train and export a fixed-shape trajectory autoencoder for Qualcomm QNN."""

from __future__ import annotations

import argparse
import json
import random
from pathlib import Path

import numpy as np
import torch
from torch import nn
from torch.utils.data import DataLoader, TensorDataset


ROOT = Path(__file__).resolve().parent
DEFAULT_DATA = ROOT / "data" / "processed" / "kanjivg-trajectories.jsonl"
DEFAULT_ARTIFACTS = ROOT / "artifacts" / "npu"
POINT_COUNT = 128
FEATURE_COUNT = 3
INPUT_SIZE = POINT_COUNT * FEATURE_COUNT


def resample(strokes: list[list[list[float]]]) -> np.ndarray:
    points = np.asarray([point for stroke in strokes for point in stroke], dtype=np.float32)
    if len(points) == 1:
        points = np.repeat(points, POINT_COUNT, axis=0)
    source = np.linspace(0.0, 1.0, len(points), dtype=np.float32)
    target = np.linspace(0.0, 1.0, POINT_COUNT, dtype=np.float32)
    result = np.empty((POINT_COUNT, FEATURE_COUNT), dtype=np.float32)
    result[:, 0] = np.interp(target, source, points[:, 0])
    result[:, 1] = np.interp(target, source, points[:, 1])
    nearest = np.rint(target * (len(points) - 1)).astype(np.int64)
    result[:, 2] = points[nearest, 2]
    return result.reshape(INPUT_SIZE)


def load_data(path: Path) -> tuple[np.ndarray, list[str]]:
    vectors: list[np.ndarray] = []
    characters: list[str] = []
    with path.open(encoding="utf-8") as source:
        for line in source:
            item = json.loads(line)
            if not item["strokes"]:
                continue
            vectors.append(resample(item["strokes"]))
            characters.append(item["character"])
    if not vectors:
        raise ValueError("No trajectories were found")
    return np.stack(vectors), characters


class FixedTrajectoryAutoencoder(nn.Module):
    def __init__(self) -> None:
        super().__init__()
        self.encoder = nn.Sequential(
            nn.Linear(INPUT_SIZE, 128),
            nn.ReLU(),
            nn.Linear(128, 32),
            nn.ReLU(),
        )
        self.decoder = nn.Sequential(
            nn.Linear(32, 128),
            nn.ReLU(),
            nn.Linear(128, INPUT_SIZE),
            nn.Sigmoid(),
        )

    def forward(self, trajectory: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor]:
        embedding = self.encoder(trajectory)
        return self.decoder(embedding), embedding


def export_and_quantize(
    model: FixedTrajectoryAutoencoder,
    calibration: np.ndarray,
    artifacts: Path,
) -> None:
    import onnx
    from onnxruntime.quantization import (
        CalibrationDataReader,
        QuantFormat,
        QuantType,
        quantize_static,
    )

    artifacts.mkdir(parents=True, exist_ok=True)
    fp32_path = artifacts / "trajectory-autoencoder-fp32.onnx"
    qdq_path = artifacts / "trajectory-autoencoder-qdq-int8.onnx"
    sample = torch.from_numpy(calibration[:1])
    torch.onnx.export(
        model,
        sample,
        fp32_path,
        input_names=["trajectory"],
        output_names=["reconstruction", "embedding"],
        opset_version=17,
        dynamic_axes=None,
    )
    onnx.checker.check_model(onnx.load(fp32_path))

    class Reader(CalibrationDataReader):
        def __init__(self, values: np.ndarray) -> None:
            self._items = iter({"trajectory": value[None, :]} for value in values)

        def get_next(self) -> dict[str, np.ndarray] | None:
            return next(self._items, None)

    quantize_static(
        fp32_path,
        qdq_path,
        Reader(calibration[:256]),
        quant_format=QuantFormat.QDQ,
        activation_type=QuantType.QUInt8,
        weight_type=QuantType.QInt8,
        per_channel=False,
    )
    onnx.checker.check_model(onnx.load(qdq_path))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data", type=Path, default=DEFAULT_DATA)
    parser.add_argument("--artifacts", type=Path, default=DEFAULT_ARTIFACTS)
    parser.add_argument("--epochs", type=int, default=5)
    parser.add_argument("--batch-size", type=int, default=128)
    parser.add_argument("--learning-rate", type=float, default=5e-4)
    parser.add_argument("--seed", type=int, default=20260718)
    args = parser.parse_args()

    random.seed(args.seed)
    np.random.seed(args.seed)
    torch.manual_seed(args.seed)
    values, characters = load_data(args.data)
    generator = np.random.default_rng(args.seed)
    indices = generator.permutation(len(values))
    validation_size = max(1, len(values) // 10)
    validation = values[indices[:validation_size]]
    training = values[indices[validation_size:]]
    loader = DataLoader(
        TensorDataset(torch.from_numpy(training)),
        batch_size=args.batch_size,
        shuffle=True,
        generator=torch.Generator().manual_seed(args.seed),
    )

    model = FixedTrajectoryAutoencoder()
    optimizer = torch.optim.AdamW(model.parameters(), lr=args.learning_rate, weight_decay=1e-4)
    loss_function = nn.MSELoss()
    for epoch in range(1, args.epochs + 1):
        model.train()
        train_total = 0.0
        for (clean,) in loader:
            noisy = clean.clone()
            noisy[:, 0::FEATURE_COUNT] = (
                noisy[:, 0::FEATURE_COUNT] + torch.randn_like(noisy[:, 0::FEATURE_COUNT]) * 0.012
            ).clamp(0, 1)
            noisy[:, 1::FEATURE_COUNT] = (
                noisy[:, 1::FEATURE_COUNT] + torch.randn_like(noisy[:, 1::FEATURE_COUNT]) * 0.012
            ).clamp(0, 1)
            reconstruction, _ = model(noisy)
            loss = loss_function(reconstruction, clean)
            optimizer.zero_grad()
            loss.backward()
            optimizer.step()
            train_total += float(loss) * len(clean)

        model.eval()
        with torch.no_grad():
            validation_tensor = torch.from_numpy(validation)
            validation_output, _ = model(validation_tensor)
            validation_loss = float(loss_function(validation_output, validation_tensor))
        print(
            f"epoch={epoch:03d} train={train_total / len(training):.6f} "
            f"validation={validation_loss:.6f}"
        )

    args.artifacts.mkdir(parents=True, exist_ok=True)
    torch.save(
        {
            "model": model.state_dict(),
            "source": "KanjiVG CC BY-SA 3.0",
            "samples": len(values),
            "characters": len(set(characters)),
            "point_count": POINT_COUNT,
            "features": ["normalized_x", "normalized_y", "stroke_end"],
            "seed": args.seed,
        },
        args.artifacts / "trajectory-autoencoder.pt",
    )
    export_and_quantize(model.eval(), training, args.artifacts)
    metadata = {
        "model": "WriteMirror Fixed Trajectory Autoencoder",
        "version": 1,
        "source": "KanjiVG",
        "license": "CC BY-SA 3.0",
        "samples": len(values),
        "unique_characters": len(set(characters)),
        "input_shape": [1, INPUT_SIZE],
        "point_count": POINT_COUNT,
        "features": ["normalized_x", "normalized_y", "stroke_end"],
        "purpose": "self-supervised trajectory reconstruction",
        "not_for": ["diagnosis", "ability scoring", "difficulty inference"],
        "seed": args.seed,
    }
    (args.artifacts / "model-card.json").write_text(
        json.dumps(metadata, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"saved={args.artifacts} samples={len(values)} unique_characters={len(set(characters))}")


if __name__ == "__main__":
    main()
