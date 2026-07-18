"""Audit public project content for Chinese text, mojibake, and private data."""

from __future__ import annotations

import argparse
import io
import re
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TEXT_SUFFIXES = {
    ".md", ".txt", ".cs", ".xaml", ".xml", ".json", ".ps1", ".cmd",
    ".py", ".csproj", ".sln", ".appxmanifest", ".rels", ".yaml", ".yml",
}
OFFICE_SUFFIXES = {".docx", ".pptx"}
EXCLUDED_PARTS = {
    ".git", ".cache", ".nuget-packages", ".dotnet-home", ".venv-trajectory",
    ".venv-trajectory-x64", "bin", "obj", "artifacts", "dist", "archive",
    "TelemetryDependencies", "AppPackages", "data",
}

SIMPLIFIED_CHINESE = re.compile(
    r"[这们为发个认难开关进问题应现过还从对给级书识别训练户儿监计划档说选测续际边总仅显语启云览种网软处实观长门东专业达变风无许]"
)
CHINESE_PHRASES = re.compile(
    r"中文|已经|进行|功能|应用|模型|数据|训练|用户|孩子|可以|没有|还是|然后|这个|问题|需要|选择|结果|说明|文档|管理监督|书写识别"
)
MOJIBAKE = re.compile(r"�|繝・|縺・|蜿悶|譁�|ã‚|ãƒ|Â |â€|ƒVƒ|ƒeƒ")
PRIVATE = re.compile(
    r"C:\\Users\\(?!Public\\)[^\\\s<>\"']+|"
    r"(?:password|パスワード)\s*[:=】]\s*\S+|"
    r"(?:username|user name|ユーザー名)\s*[:=】]\s*\S+",
    re.IGNORECASE,
)


@dataclass(frozen=True)
class Finding:
    category: str
    location: str
    match: str
    context: str


def decode_text(data: bytes) -> str | None:
    for encoding in ("utf-8-sig", "utf-16", "cp932"):
        try:
            return data.decode(encoding)
        except UnicodeDecodeError:
            continue
    return None


def inspect_text(text: str, location: str) -> list[Finding]:
    findings: list[Finding] = []
    patterns = (
        ("simplified-chinese", SIMPLIFIED_CHINESE),
        ("chinese-phrase", CHINESE_PHRASES),
        ("mojibake", MOJIBAKE),
        ("private-data", PRIVATE),
    )
    for category, pattern in patterns:
        for match in pattern.finditer(text):
            start = max(0, match.start() - 45)
            end = min(len(text), match.end() + 45)
            context = re.sub(r"\s+", " ", text[start:end]).strip()
            findings.append(Finding(category, location, match.group(0), context))
    return findings


def inspect_archive(data: bytes, location: str) -> list[Finding]:
    findings: list[Finding] = []
    try:
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            for name in archive.namelist():
                suffix = Path(name).suffix.lower()
                if suffix not in TEXT_SUFFIXES:
                    continue
                text = decode_text(archive.read(name))
                if text is not None:
                    findings.extend(inspect_text(text, f"{location}!{name}"))
    except zipfile.BadZipFile:
        findings.append(Finding("invalid-archive", location, "", "ZIP構造を読み取れません"))
    return findings


def inspect_file(path: Path) -> list[Finding]:
    data = path.read_bytes()
    location = str(path.relative_to(ROOT)) if path.is_relative_to(ROOT) else str(path)
    if path.suffix.lower() in OFFICE_SUFFIXES:
        return inspect_archive(data, location)
    text = decode_text(data)
    return [] if text is None else inspect_text(text, location)


def project_files() -> list[Path]:
    roots = [
        ROOT / "README.md",
        ROOT / "WriteMirror.sln",
        ROOT / "docs",
        ROOT / "src",
        ROOT / "tests",
        ROOT / "experiments",
        ROOT / "packaging",
        ROOT / "scripts",
        ROOT / "tools",
    ]
    files: list[Path] = []
    for root in roots:
        if root.is_file():
            files.append(root)
            continue
        if not root.exists():
            continue
        for path in root.rglob("*"):
            if not path.is_file() or any(part in EXCLUDED_PARTS for part in path.parts):
                continue
            if path.name == Path(__file__).name:
                continue
            if path.suffix.lower() in TEXT_SUFFIXES | OFFICE_SUFFIXES:
                files.append(path)
    return sorted(set(files))


def release_files(release_dir: Path) -> list[Path]:
    return sorted(
        path for path in release_dir.rglob("*")
        if path.is_file() and path.suffix.lower() in TEXT_SUFFIXES | OFFICE_SUFFIXES
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--release-dir", type=Path)
    arguments = parser.parse_args()

    files = project_files()
    if arguments.release_dir:
        files.extend(release_files(arguments.release_dir.resolve()))
    findings: list[Finding] = []
    for path in sorted(set(files)):
        findings.extend(inspect_file(path))

    if findings:
        for finding in findings:
            print(
                f"{finding.category}\t{finding.location}\t{finding.match}\t{finding.context}",
                file=sys.stderr,
            )
        print(f"FAIL: {len(findings)} findings in {len(files)} files", file=sys.stderr)
        return 1

    print(f"PASS: no Chinese, mojibake, or private-data findings in {len(files)} files")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
