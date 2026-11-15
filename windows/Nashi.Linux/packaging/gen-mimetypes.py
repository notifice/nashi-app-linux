#!/usr/bin/env python3
"""Keeps the MimeType= line of nashi.desktop in sync with the input
extensions of Resources/FeatureCatalog.json (the conversionMatrix keys).

  gen-mimetypes.py --write   rewrite nashi.desktop
  gen-mimetypes.py --check   exit 1 if nashi.desktop is stale
"""
import json, pathlib, sys

HERE = pathlib.Path(__file__).resolve().parent
CATALOG = HERE.parents[2] / "Resources" / "FeatureCatalog.json"
DESKTOP = HERE / "nashi.desktop"

MIME = {
    "jpg": "image/jpeg", "png": "image/png", "webp": "image/webp", "heic": "image/heic",
    "tiff": "image/tiff", "svg": "image/svg+xml", "avif": "image/avif", "bmp": "image/bmp",
    "gif": "image/gif",
    "mp3": "audio/mpeg", "m4a": "audio/mp4", "wav": "audio/x-wav", "flac": "audio/flac",
    "ogg": "audio/ogg", "opus": "audio/ogg", "aiff": "audio/x-aiff", "wma": "audio/x-ms-wma",
    "mp4": "video/mp4", "mov": "video/quicktime", "mkv": "video/x-matroska",
    "webm": "video/webm", "avi": "video/x-msvideo", "wmv": "video/x-ms-wmv",
    "pdf": "application/pdf", "ppt": "application/vnd.ms-powerpoint",
    "pptx": "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    "docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    "xlsx": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    "xls": "application/vnd.ms-excel", "ods": "application/vnd.oasis.opendocument.spreadsheet",
    "pages": "application/vnd.apple.pages", "md": "text/markdown", "rtf": "application/rtf",
    "html": "text/html", "epub": "application/epub+zip", "txt": "text/plain",
    "csv": "text/csv", "tsv": "text/tab-separated-values", "srt": "application/x-subrip",
    "vtt": "text/vtt", "zip": "application/zip", "tar": "application/x-tar",
    "gz": "application/gzip", "rar": "application/vnd.rar",
}
# Extensions with no MIME type on Linux desktops (Apple-only bundle/clipping formats).
NO_MIME = {"rtfd", "textclipping"}


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "--check"
    exts = list(json.loads(CATALOG.read_text())["conversionMatrix"])
    unknown = [e for e in exts if e not in MIME and e not in NO_MIME]
    if unknown:
        sys.exit("gen-mimetypes: no MIME mapping for: " + ", ".join(unknown))
    line = "MimeType=" + ";".join(sorted({MIME[e] for e in exts if e in MIME})) + ";"
    current = DESKTOP.read_text()
    new = "\n".join(line if l.startswith("MimeType=") else l for l in current.splitlines()) + "\n"
    if mode == "--write":
        DESKTOP.write_text(new)
    elif current != new:
        sys.exit("gen-mimetypes: nashi.desktop is stale; run packaging/gen-mimetypes.py --write")


main()
