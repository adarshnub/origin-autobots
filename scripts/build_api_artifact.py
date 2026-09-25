from __future__ import annotations

from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "artifacts" / "autobots-api.zip"
INCLUDED_PATHS = (Path("pyproject.toml"), Path("services"), Path("packages"))


def build() -> Path:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(OUTPUT, "w", compression=ZIP_DEFLATED, compresslevel=9) as archive:
        for included in INCLUDED_PATHS:
            source = ROOT / included
            if source.is_file():
                archive.write(source, included.as_posix())
                continue
            for path in sorted(source.rglob("*")):
                if not path.is_file() or "__pycache__" in path.parts or path.suffix in {".pyc", ".pyo"}:
                    continue
                archive.write(path, path.relative_to(ROOT).as_posix())
    return OUTPUT


if __name__ == "__main__":
    artifact = build()
    print(f"Built API source archive: {artifact.relative_to(ROOT).as_posix()} ({artifact.stat().st_size} bytes)")
