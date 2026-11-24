"""Validate synthetic renderer output, not physical printer behavior."""
import json
import re
import sys
from pathlib import Path

import pymupdf


root = Path(sys.argv[1])
samples = sorted(root.glob("*.pdf"))
assert len(samples) == 10, f"Expected 10 PDF cases, found {len(samples)}"
cold = sorted((root / "cold").glob("*.pdf"))
assert {path.name for path in cold} == {
    "A4-001-items.pdf", "Thermal80-003-items.pdf", "Thermal80-005-items.pdf"
}, "Missing independent cold-render cases"
for path in samples + cold:
    metadata = json.loads(path.with_suffix(".json").read_text(encoding="utf-8"))
    count, paper = metadata["count"], metadata["paper"]
    with pymupdf.open(path) as pdf:
        text = "\n".join(page.get_text() for page in pdf)
        items = [int(value) for value in re.findall(r"Example product (\d+)\b", text)]
        assert items == list(range(1, count + 1)), (path.name, items)
        assert text.count("TOTAL PAYABLE") == 1, path.name
        assert f"INR {count * 10:.2f}" in text.replace(",", ""), path.name
        page_mm = []
        for page in pdf:
            width = page.rect.width * 25.4 / 72
            height = page.rect.height * 25.4 / 72
            expected_width = 80 if paper == "Thermal80" else 210
            assert abs(width - expected_width) < 1, (path.name, width)
            if paper == "A4":
                assert abs(height - 297) < 1, (path.name, height)
            words = page.get_text("words")
            assert words, f"Empty page: {path.name}"
            bottom = max(word[3] for word in words) * 25.4 / 72
            page_mm.append({"height": round(height, 1), "textBottom": round(bottom, 1),
                            "blankTail": round(height - bottom, 1)})
        if paper == "Thermal80" and count in (1, 3, 10):
            assert len(pdf) == 1, (path.name, len(pdf))
            limit = {1: 110, 3: 130, 10: 180}[count]
            assert page_mm[0]["height"] <= limit, (path.name, page_mm)
        metadata["pages"] = page_mm
        path.with_suffix(".json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
        assert metadata["firstMs"] >= 0 and metadata["repeatMs"] >= 0, path.name
        print(str(path.relative_to(root)), page_mm,
              f"initial render {metadata['firstMs']:.1f}ms",
              f"repeat render {metadata['repeatMs']:.1f}ms")
