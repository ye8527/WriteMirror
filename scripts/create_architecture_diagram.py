"""Create the Japanese system architecture diagram used by the project documents."""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "docs" / "assets" / "WriteMirror_システムアーキテクチャ_ja.png"
FONT = Path(r"C:\Windows\Fonts\YuGothM.ttc")

WIDTH = 1800
HEIGHT = 1160


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    index = 1 if bold else 0
    return ImageFont.truetype(str(FONT), size=size, index=index)


def box(
    draw: ImageDraw.ImageDraw,
    rect: tuple[int, int, int, int],
    title: str,
    lines: list[str],
    fill: str,
    outline: str,
    title_color: str = "#17324D",
) -> None:
    draw.rounded_rectangle(rect, radius=22, fill=fill, outline=outline, width=3)
    x1, y1, x2, _ = rect
    draw.text((x1 + 24, y1 + 18), title, font=font(29, True), fill=title_color)
    y = y1 + 66
    for line in lines:
        draw.text((x1 + 26, y), f"• {line}", font=font(22), fill="#243746")
        y += 34


def arrow(
    draw: ImageDraw.ImageDraw,
    start: tuple[int, int],
    end: tuple[int, int],
    color: str = "#2F80ED",
    width: int = 7,
) -> None:
    draw.line((start, end), fill=color, width=width)
    x2, y2 = end
    x1, y1 = start
    dx, dy = x2 - x1, y2 - y1
    length = max((dx * dx + dy * dy) ** 0.5, 1)
    ux, uy = dx / length, dy / length
    px, py = -uy, ux
    size = 18
    p1 = (x2, y2)
    p2 = (x2 - ux * size + px * size * 0.7, y2 - uy * size + py * size * 0.7)
    p3 = (x2 - ux * size - px * size * 0.7, y2 - uy * size - py * size * 0.7)
    draw.polygon((p1, p2, p3), fill=color)


def main() -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    image = Image.new("RGB", (WIDTH, HEIGHT), "#F7F9FC")
    draw = ImageDraw.Draw(image)

    draw.text((70, 34), "WriteMirror 0.6.0 システムアーキテクチャ", font=font(43, True), fill="#17324D")
    draw.text(
        (72, 94),
        "本人の回答を優先し、筆跡をクラウドへ送らないオンデバイスAI構成",
        font=font(25),
        fill="#4E6275",
    )

    box(
        draw,
        (55, 190, 420, 475),
        "入力",
        [
            "Surface Pen / マウス",
            "座標・時刻・筆圧・傾き",
            "対象文字",
            "本人の主観回答",
        ],
        "#EAF3FF",
        "#2F80ED",
    )

    draw.rounded_rectangle(
        (500, 155, 1325, 860), radius=30, fill="#FFFFFF", outline="#1B998B", width=5
    )
    draw.text((535, 175), "Surface Pro 11 / Windows 11 ARM64（端末内）", font=font(31, True), fill="#176C64")

    box(
        draw,
        (545, 245, 920, 425),
        "操作・記録",
        ["WPF / Windows Ink", "再生・固定観測規則", "本人回答の優先制御"],
        "#EFFAF7",
        "#1B998B",
    )
    box(
        draw,
        (965, 245, 1280, 425),
        "文字・音声",
        ["Windows日本語文字候補", "Windows日本語音声出力", "AIとは別の端末機能"],
        "#F5F0FF",
        "#7A5AF8",
    )
    box(
        draw,
        (545, 485, 920, 700),
        "軌跡AIモデル",
        ["128点×3特徴量", "384→128→32→128→384", "QDQ INT8 ONNX", "モデルをアプリへ同梱"],
        "#FFF7E8",
        "#E49B33",
    )
    box(
        draw,
        (965, 485, 1280, 700),
        "Windows ML / NPU",
        ["EnsureReadyAsync", "QNN実行プロバイダー", "CPUフォールバック無効", "Qualcomm Hexagon NPU"],
        "#FFF2F1",
        "#D55D56",
    )
    box(
        draw,
        (665, 735, 1160, 825),
        "データ方針",
        ["既定はメモリーのみ／明示選択時だけ端末内JSON保存"],
        "#F1F4F8",
        "#78909C",
    )

    box(
        draw,
        (1400, 190, 1750, 520),
        "出力",
        [
            "筆順の再生",
            "中立的な観測",
            "任意のAI再構成表示",
            "日本語音声",
            "診断・採点はしない",
        ],
        "#EFFAF7",
        "#1B998B",
    )

    arrow(draw, (420, 330), (500, 330))
    arrow(draw, (1325, 345), (1400, 345))
    arrow(draw, (920, 590), (965, 590), color="#D55D56")

    box(
        draw,
        (55, 915, 900, 1085),
        "開発時の学習経路（実行時には不要）",
        [
            "KanjiVG 11,662件（CC BY-SA 3.0）",
            "PyTorch自己教師あり学習 → ONNX量子化 → アプリへ同梱",
        ],
        "#FFF7E8",
        "#E49B33",
    )
    box(
        draw,
        (950, 915, 1750, 1085),
        "ネットワーク境界",
        [
            "初回の認定QNN実行プロバイダー準備だけ通信する場合あり",
            "推論・筆跡処理・音声・文字候補は端末内／クラウド推論なし",
        ],
        "#EAF3FF",
        "#2F80ED",
    )

    draw.line((1370, 155, 1370, 860), fill="#B8C4D0", width=3)
    draw.text((1392, 790), "クラウド推論なし", font=font(23, True), fill="#4E6275")
    draw.text((1392, 825), "筆跡アップロードなし", font=font(23, True), fill="#4E6275")

    image.save(OUTPUT, format="PNG", optimize=True)
    print(OUTPUT)


if __name__ == "__main__":
    main()
