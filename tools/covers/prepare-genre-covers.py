"""Makes the genre covers the app ships from the design sources.

Reads design/covers/essentials (the full-size PNGs and manifest.json) and writes
src/MelodayMusic.App/Assets/Genres: each cover as an 800 px JPEG, and genres.json listing
each genre's name and file. Run again whenever the sources change:

    python tools/covers/prepare-genre-covers.py
"""
import json
import pathlib

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = ROOT / "design" / "covers" / "essentials"
TARGET = ROOT / "src" / "MelodayMusic.App" / "Assets" / "Genres"
SIZE, QUALITY = 800, 90


def main() -> None:
    manifest = json.loads((SOURCE / "manifest.json").read_text(encoding="utf-8"))
    TARGET.mkdir(parents=True, exist_ok=True)
    genres = []
    for cover in manifest["covers"]:
        source = SOURCE / cover["filename"]
        if not source.exists():
            continue
        name = pathlib.Path(cover["filename"]).stem + ".jpg"
        image = Image.open(source).convert("RGB").resize((SIZE, SIZE), Image.LANCZOS)
        image.save(TARGET / name, "JPEG", quality=QUALITY, optimize=True)
        genres.append({"genre": cover["genre"], "file": name})
    (TARGET / "genres.json").write_text(json.dumps(genres, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"{len(genres)} genre covers written to {TARGET}")


if __name__ == "__main__":
    main()
