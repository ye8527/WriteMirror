"""Pretrain or fine-tune a denoising encoder on normalized stroke trajectories."""

from __future__ import annotations

import argparse
import json
import random
from pathlib import Path

import numpy as np
import torch
from torch import nn
from torch.nn.utils.rnn import pad_sequence
from torch.utils.data import DataLoader, Dataset, random_split


ROOT = Path(__file__).resolve().parent
DEFAULT_DATA = ROOT / "data" / "processed" / "kanjivg-trajectories.jsonl"
DEFAULT_OUTPUT = ROOT / "artifacts" / "pretrained.pt"


class TrajectoryDataset(Dataset):
    def __init__(self, path: Path, characters: set[str] | None = None) -> None:
        self.items: list[torch.Tensor] = []
        with path.open(encoding="utf-8") as source:
            for line in source:
                item = json.loads(line)
                if characters and item["character"] not in characters:
                    continue
                points = [point for stroke in item["strokes"] for point in stroke]
                if points:
                    self.items.append(torch.tensor(points, dtype=torch.float32))
        if not self.items:
            raise ValueError("No trajectories matched the requested characters")

    def __len__(self) -> int:
        return len(self.items)

    def __getitem__(self, index: int) -> torch.Tensor:
        return self.items[index]


def collate(batch: list[torch.Tensor]) -> tuple[torch.Tensor, torch.Tensor]:
    lengths = torch.tensor([len(item) for item in batch], dtype=torch.long)
    return pad_sequence(batch, batch_first=True), lengths


class TrajectoryEncoder(nn.Module):
    def __init__(self, hidden_size: int = 96, embedding_size: int = 64) -> None:
        super().__init__()
        self.encoder = nn.GRU(3, hidden_size, batch_first=True, bidirectional=True)
        self.embedding = nn.Linear(hidden_size * 2, embedding_size)
        self.reconstruction = nn.Sequential(
            nn.Linear(hidden_size * 2, hidden_size), nn.GELU(), nn.Linear(hidden_size, 3)
        )

    def forward(self, points: torch.Tensor, lengths: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor]:
        encoded, _ = self.encoder(points)
        steps = torch.arange(points.shape[1], device=points.device)[None, :]
        mask = steps < lengths[:, None]
        pooled = (encoded * mask.unsqueeze(-1)).sum(1) / lengths.clamp_min(1).unsqueeze(1)
        return self.reconstruction(encoded), nn.functional.normalize(self.embedding(pooled), dim=-1)


def masked_loss(prediction: torch.Tensor, target: torch.Tensor, lengths: torch.Tensor) -> torch.Tensor:
    steps = torch.arange(target.shape[1], device=target.device)[None, :]
    mask = (steps < lengths[:, None]).unsqueeze(-1)
    xy = ((prediction[..., :2] - target[..., :2]) ** 2 * mask).sum() / (mask.sum() * 2)
    pen = nn.functional.binary_cross_entropy_with_logits(prediction[..., 2], target[..., 2], reduction="none")
    pen = (pen * mask.squeeze(-1)).sum() / mask.sum()
    return xy + 0.2 * pen


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data", type=Path, default=DEFAULT_DATA)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--init-checkpoint", type=Path)
    parser.add_argument("--characters", help="Fine-tune on only these characters, e.g. あ木語")
    parser.add_argument("--epochs", type=int, default=30)
    parser.add_argument("--batch-size", type=int, default=64)
    parser.add_argument("--learning-rate", type=float, default=3e-4)
    parser.add_argument("--seed", type=int, default=20260718)
    args = parser.parse_args()

    random.seed(args.seed)
    np.random.seed(args.seed)
    torch.manual_seed(args.seed)
    dataset = TrajectoryDataset(args.data, set(args.characters) if args.characters else None)
    validation_size = max(1, int(len(dataset) * 0.1)) if len(dataset) > 1 else 0
    train_size = len(dataset) - validation_size
    generator = torch.Generator().manual_seed(args.seed)
    train_set, validation_set = random_split(dataset, [train_size, validation_size], generator=generator)
    train_loader = DataLoader(train_set, batch_size=args.batch_size, shuffle=True, collate_fn=collate)
    validation_loader = DataLoader(validation_set, batch_size=args.batch_size, collate_fn=collate)

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    model = TrajectoryEncoder().to(device)
    if args.init_checkpoint:
        checkpoint = torch.load(args.init_checkpoint, map_location=device, weights_only=True)
        model.load_state_dict(checkpoint["model"] if "model" in checkpoint else checkpoint)
    optimizer = torch.optim.AdamW(model.parameters(), lr=args.learning_rate, weight_decay=1e-4)

    for epoch in range(1, args.epochs + 1):
        model.train()
        train_total = 0.0
        for clean, lengths in train_loader:
            clean, lengths = clean.to(device), lengths.to(device)
            noisy = clean.clone()
            noisy[..., :2] = (noisy[..., :2] + torch.randn_like(noisy[..., :2]) * 0.012).clamp(0, 1)
            prediction, _ = model(noisy, lengths)
            loss = masked_loss(prediction, clean, lengths)
            optimizer.zero_grad()
            loss.backward()
            nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            optimizer.step()
            train_total += float(loss) * len(clean)

        model.eval()
        validation_total = 0.0
        with torch.no_grad():
            for clean, lengths in validation_loader:
                clean, lengths = clean.to(device), lengths.to(device)
                prediction, _ = model(clean, lengths)
                validation_total += float(masked_loss(prediction, clean, lengths)) * len(clean)
        train_loss = train_total / max(1, train_size)
        validation_loss = validation_total / max(1, validation_size)
        print(f"epoch={epoch:03d} train={train_loss:.6f} validation={validation_loss:.6f}")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    torch.save({
        "model": model.state_dict(),
        "features": ["normalized_x", "normalized_y", "stroke_end"],
        "characters": args.characters,
        "source": "KanjiVG CC BY-SA 3.0",
        "seed": args.seed,
    }, args.output)
    print(f"saved={args.output} device={device} samples={len(dataset)}")


if __name__ == "__main__":
    main()
