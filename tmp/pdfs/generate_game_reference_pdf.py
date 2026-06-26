from __future__ import annotations

import re
import sys
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    ListFlowable,
    ListItem,
    Paragraph,
    Preformatted,
    SimpleDocTemplate,
    Spacer,
)


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Assets" / "EchoProtocol" / "Documentation" / "EchoProtocol_Game_Reference_Research.md"
OUTPUT = ROOT / "Assets" / "EchoProtocol" / "Documentation" / "EchoProtocol_Game_Reference_Research.pdf"


def register_fonts() -> tuple[str, str, str]:
    regular = "Helvetica"
    bold = "Helvetica-Bold"
    mono = "Courier"

    regular_path = Path(r"C:\Windows\Fonts\arial.ttf")
    bold_path = Path(r"C:\Windows\Fonts\arialbd.ttf")
    mono_path = Path(r"C:\Windows\Fonts\consola.ttf")

    if regular_path.exists():
        pdfmetrics.registerFont(TTFont("DocRegular", str(regular_path)))
        regular = "DocRegular"

    if bold_path.exists():
        pdfmetrics.registerFont(TTFont("DocBold", str(bold_path)))
        bold = "DocBold"

    if mono_path.exists():
        pdfmetrics.registerFont(TTFont("DocMono", str(mono_path)))
        mono = "DocMono"

    return regular, bold, mono


def normalize_text(text: str) -> str:
    replacements = {
        "\ufeff": "",
        "\u2011": "-",
        "\u2012": "-",
        "\u2013": "-",
        "\u2014": "-",
        "\u2018": "'",
        "\u2019": "'",
        "\u201c": '"',
        "\u201d": '"',
        "\u00a0": " ",
    }

    for old, new in replacements.items():
        text = text.replace(old, new)

    return text


def escape_html(text: str) -> str:
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def inline_markdown(text: str) -> str:
    text = escape_html(normalize_text(text.strip()))
    text = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", text)
    text = re.sub(r"`([^`]+)`", r"<font name='DocMono'>\1</font>", text)
    return text


def make_styles(regular: str, bold: str, mono: str) -> dict[str, ParagraphStyle]:
    base = getSampleStyleSheet()

    return {
        "Title": ParagraphStyle(
            "Title",
            parent=base["Title"],
            fontName=bold,
            fontSize=25,
            leading=30,
            alignment=TA_CENTER,
            textColor=colors.HexColor("#121722"),
            spaceAfter=8,
        ),
        "Subtitle": ParagraphStyle(
            "Subtitle",
            parent=base["Normal"],
            fontName=regular,
            fontSize=11,
            leading=15,
            alignment=TA_CENTER,
            textColor=colors.HexColor("#5D687A"),
            spaceAfter=16,
        ),
        "H2": ParagraphStyle(
            "H2",
            parent=base["Heading2"],
            fontName=bold,
            fontSize=17,
            leading=21,
            textColor=colors.HexColor("#121722"),
            spaceBefore=15,
            spaceAfter=8,
        ),
        "H3": ParagraphStyle(
            "H3",
            parent=base["Heading3"],
            fontName=bold,
            fontSize=12.3,
            leading=16,
            textColor=colors.HexColor("#1F6E89"),
            spaceBefore=9,
            spaceAfter=5,
        ),
        "Body": ParagraphStyle(
            "Body",
            parent=base["BodyText"],
            fontName=regular,
            fontSize=9.8,
            leading=14,
            textColor=colors.HexColor("#20242B"),
            alignment=TA_LEFT,
            spaceAfter=6,
        ),
        "Bullet": ParagraphStyle(
            "Bullet",
            parent=base["BodyText"],
            fontName=regular,
            fontSize=9.4,
            leading=13,
            leftIndent=12,
            textColor=colors.HexColor("#20242B"),
        ),
        "Code": ParagraphStyle(
            "Code",
            parent=base["Code"],
            fontName=mono,
            fontSize=8.5,
            leading=11,
            textColor=colors.HexColor("#0E5368"),
            backColor=colors.HexColor("#EEF9FC"),
            borderPadding=6,
            leftIndent=4,
            rightIndent=4,
            spaceBefore=4,
            spaceAfter=8,
        ),
        "Accent": ParagraphStyle(
            "Accent",
            parent=base["BodyText"],
            fontName=bold,
            fontSize=10,
            leading=14,
            textColor=colors.HexColor("#00BFE8"),
            alignment=TA_CENTER,
            spaceAfter=12,
        ),
    }


def make_list(items: list[str], ordered: bool, styles: dict[str, ParagraphStyle]) -> ListFlowable:
    flowables = [
        ListItem(Paragraph(inline_markdown(item), styles["Bullet"]), leftIndent=12)
        for item in items
    ]

    return ListFlowable(
        flowables,
        bulletType="1" if ordered else "bullet",
        leftIndent=18,
        bulletFontSize=8,
        bulletColor=colors.HexColor("#00A7C8"),
        spaceAfter=7,
    )


def flush_list(story, items, ordered, styles):
    if items:
        story.append(make_list(items, ordered, styles))
        items.clear()


def build_story(markdown: str, styles: dict[str, ParagraphStyle]):
    story = []
    items: list[str] = []
    ordered = False
    in_code = False
    code_lines: list[str] = []
    title_used = False

    for raw_line in normalize_text(markdown).splitlines():
        line = raw_line.rstrip()
        stripped = line.strip()

        if stripped.startswith("```"):
            if in_code:
                story.append(Preformatted("\n".join(code_lines), styles["Code"]))
                code_lines.clear()
                in_code = False
            else:
                flush_list(story, items, ordered, styles)
                in_code = True
            continue

        if in_code:
            code_lines.append(line)
            continue

        if not stripped:
            flush_list(story, items, ordered, styles)
            continue

        heading = re.match(r"^(#{1,3})\s+(.+)$", stripped)
        bullet = re.match(r"^-\s+(.+)$", stripped)
        numbered = re.match(r"^\d+\.\s+(.+)$", stripped)

        if heading:
            flush_list(story, items, ordered, styles)
            level = len(heading.group(1))
            text = heading.group(2)

            if level == 1 and not title_used:
                story.append(Spacer(1, 24))
                story.append(Paragraph(inline_markdown(text), styles["Title"]))
                story.append(Paragraph("Inspiratieonderzoek voor Echo Protocol", styles["Subtitle"]))
                story.append(Paragraph("Scanner systems | Horror tension | First-person environmental feedback", styles["Accent"]))
                story.append(Spacer(1, 8))
                title_used = True
            elif level == 2:
                story.append(Paragraph(inline_markdown(text), styles["H2"]))
            else:
                story.append(Paragraph(inline_markdown(text), styles["H3"]))
            continue

        if bullet:
            if items and ordered:
                flush_list(story, items, ordered, styles)
            ordered = False
            items.append(bullet.group(1))
            continue

        if numbered:
            if items and not ordered:
                flush_list(story, items, ordered, styles)
            ordered = True
            items.append(numbered.group(1))
            continue

        flush_list(story, items, ordered, styles)
        story.append(Paragraph(inline_markdown(stripped), styles["Body"]))

    flush_list(story, items, ordered, styles)
    return story


def draw_header_footer(canvas, doc):
    canvas.saveState()
    width, height = A4
    canvas.setFillColor(colors.HexColor("#00D8FF"))
    canvas.rect(0, height - 8, width, 8, stroke=0, fill=1)

    canvas.setStrokeColor(colors.HexColor("#D7DEE8"))
    canvas.setLineWidth(0.5)
    canvas.line(doc.leftMargin, 16 * mm, width - doc.rightMargin, 16 * mm)

    font_name = "DocRegular" if "DocRegular" in pdfmetrics.getRegisteredFontNames() else "Helvetica"
    canvas.setFillColor(colors.HexColor("#5D687A"))
    canvas.setFont(font_name, 8)
    canvas.drawString(doc.leftMargin, 10 * mm, "Echo Protocol - Game Reference Research")
    canvas.drawRightString(width - doc.rightMargin, 10 * mm, f"Pagina {doc.page}")
    canvas.restoreState()


def main() -> int:
    if not SOURCE.exists():
        print(f"Source markdown not found: {SOURCE}", file=sys.stderr)
        return 1

    regular, bold, mono = register_fonts()
    styles = make_styles(regular, bold, mono)
    story = build_story(SOURCE.read_text(encoding="utf-8-sig"), styles)

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)

    doc = SimpleDocTemplate(
        str(OUTPUT),
        pagesize=A4,
        rightMargin=17 * mm,
        leftMargin=17 * mm,
        topMargin=18 * mm,
        bottomMargin=20 * mm,
        title="Echo Protocol Game Reference Research",
        author="Renat",
        subject="Inspiratieonderzoek voor Echo Protocol",
    )

    doc.build(story, onFirstPage=draw_header_footer, onLaterPages=draw_header_footer)
    print(OUTPUT)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
