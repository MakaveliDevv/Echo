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
    PageBreak,
    Paragraph,
    Preformatted,
    SimpleDocTemplate,
    Spacer,
)


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Assets" / "EchoProtocol" / "Documentation" / "EchoProtocol_Process_Documentation.md"
OUTPUT = ROOT / "Assets" / "EchoProtocol" / "Documentation" / "EchoProtocol_Process_Documentation.pdf"


def register_fonts() -> tuple[str, str, str]:
    """Register Windows fonts when available, with safe ReportLab fallbacks."""
    regular_candidates = [
        Path(r"C:\Windows\Fonts\arial.ttf"),
        Path(r"C:\Windows\Fonts\segoeui.ttf"),
        Path(r"C:\Windows\Fonts\calibri.ttf"),
    ]
    bold_candidates = [
        Path(r"C:\Windows\Fonts\arialbd.ttf"),
        Path(r"C:\Windows\Fonts\segoeuib.ttf"),
        Path(r"C:\Windows\Fonts\calibrib.ttf"),
    ]
    mono_candidates = [
        Path(r"C:\Windows\Fonts\consola.ttf"),
        Path(r"C:\Windows\Fonts\cour.ttf"),
    ]

    regular = "Helvetica"
    bold = "Helvetica-Bold"
    mono = "Courier"

    for candidate in regular_candidates:
        if candidate.exists():
            pdfmetrics.registerFont(TTFont("DocRegular", str(candidate)))
            regular = "DocRegular"
            break

    for candidate in bold_candidates:
        if candidate.exists():
            pdfmetrics.registerFont(TTFont("DocBold", str(candidate)))
            bold = "DocBold"
            break

    for candidate in mono_candidates:
        if candidate.exists():
            pdfmetrics.registerFont(TTFont("DocMono", str(candidate)))
            mono = "DocMono"
            break

    return regular, bold, mono


def normalize_text(text: str) -> str:
    """Keep the PDF clean by replacing typographic symbols with robust equivalents."""
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
    return (
        text.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
    )


def inline_markdown(text: str) -> str:
    """Small inline formatter for bold and code spans."""
    text = escape_html(normalize_text(text.strip()))
    text = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", text)
    text = re.sub(r"`([^`]+)`", r"<font name='DocMono'>\1</font>", text)
    return text


def make_styles(regular: str, bold: str, mono: str) -> dict[str, ParagraphStyle]:
    base = getSampleStyleSheet()
    dark = colors.HexColor("#121722")
    cyan = colors.HexColor("#00D8FF")
    muted = colors.HexColor("#5D687A")

    return {
        "Title": ParagraphStyle(
            "Title",
            parent=base["Title"],
            fontName=bold,
            fontSize=26,
            leading=31,
            alignment=TA_CENTER,
            textColor=dark,
            spaceAfter=8,
        ),
        "Subtitle": ParagraphStyle(
            "Subtitle",
            parent=base["Normal"],
            fontName=regular,
            fontSize=11,
            leading=15,
            alignment=TA_CENTER,
            textColor=muted,
            spaceAfter=18,
        ),
        "H2": ParagraphStyle(
            "H2",
            parent=base["Heading2"],
            fontName=bold,
            fontSize=17,
            leading=21,
            textColor=dark,
            spaceBefore=16,
            spaceAfter=8,
        ),
        "H3": ParagraphStyle(
            "H3",
            parent=base["Heading3"],
            fontName=bold,
            fontSize=12.5,
            leading=16,
            textColor=colors.HexColor("#1F6E89"),
            spaceBefore=10,
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
            fontSize=8.6,
            leading=11,
            textColor=colors.HexColor("#0E5368"),
            backColor=colors.HexColor("#EEF9FC"),
            borderPadding=6,
            leftIndent=4,
            rightIndent=4,
            spaceBefore=4,
            spaceAfter=8,
        ),
        "Caption": ParagraphStyle(
            "Caption",
            parent=base["Normal"],
            fontName=regular,
            fontSize=8,
            leading=10,
            textColor=muted,
            alignment=TA_CENTER,
        ),
        "Small": ParagraphStyle(
            "Small",
            parent=base["Normal"],
            fontName=regular,
            fontSize=8.5,
            leading=11,
            textColor=muted,
        ),
        "Accent": ParagraphStyle(
            "Accent",
            parent=base["BodyText"],
            fontName=bold,
            fontSize=10,
            leading=14,
            textColor=cyan,
            alignment=TA_CENTER,
            spaceAfter=12,
        ),
    }


def make_list(items: list[str], ordered: bool, styles: dict[str, ParagraphStyle]) -> ListFlowable:
    flowables = [
        ListItem(
            Paragraph(inline_markdown(item), styles["Bullet"]),
            leftIndent=12,
        )
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


def add_pending_list(story, pending_items, pending_ordered, styles):
    if pending_items:
        story.append(make_list(pending_items, pending_ordered, styles))
        pending_items.clear()


def build_story(markdown: str, styles: dict[str, ParagraphStyle]):
    story = []
    lines = normalize_text(markdown).splitlines()

    title_used = False
    in_code = False
    code_lines: list[str] = []
    pending_items: list[str] = []
    pending_ordered = False

    for raw_line in lines:
        line = raw_line.rstrip()
        stripped = line.strip()

        if stripped.startswith("```"):
            if in_code:
                story.append(Preformatted("\n".join(code_lines), styles["Code"]))
                code_lines.clear()
                in_code = False
            else:
                add_pending_list(story, pending_items, pending_ordered, styles)
                in_code = True
            continue

        if in_code:
            code_lines.append(line)
            continue

        if not stripped:
            add_pending_list(story, pending_items, pending_ordered, styles)
            continue

        heading_match = re.match(r"^(#{1,3})\s+(.+)$", stripped)
        bullet_match = re.match(r"^-\s+(.+)$", stripped)
        numbered_match = re.match(r"^\d+\.\s+(.+)$", stripped)

        if heading_match:
            add_pending_list(story, pending_items, pending_ordered, styles)
            level = len(heading_match.group(1))
            text = heading_match.group(2)

            if level == 1 and not title_used:
                story.append(Spacer(1, 24))
                story.append(Paragraph(inline_markdown(text), styles["Title"]))
                story.append(Paragraph("Shader eindopdracht - proces, iteraties en final result", styles["Subtitle"]))
                story.append(Paragraph("Unity URP | Compute Shader | Hologram Scanwave", styles["Accent"]))
                story.append(Spacer(1, 10))
                title_used = True
                continue

            if level == 2:
                story.append(Paragraph(inline_markdown(text), styles["H2"]))
            else:
                story.append(Paragraph(inline_markdown(text), styles["H3"]))
            continue

        if bullet_match:
            item = bullet_match.group(1)
            if pending_items and pending_ordered:
                add_pending_list(story, pending_items, pending_ordered, styles)
            pending_ordered = False
            pending_items.append(item)
            continue

        if numbered_match:
            item = numbered_match.group(1)
            if pending_items and not pending_ordered:
                add_pending_list(story, pending_items, pending_ordered, styles)
            pending_ordered = True
            pending_items.append(item)
            continue

        add_pending_list(story, pending_items, pending_ordered, styles)
        story.append(Paragraph(inline_markdown(stripped), styles["Body"]))

        # Keep major chapters from crowding each other too much.
        if stripped.startswith("Vereisten uit de opdracht:"):
            story.append(Spacer(1, 4))

    add_pending_list(story, pending_items, pending_ordered, styles)

    return story


def draw_header_footer(canvas, doc):
    canvas.saveState()
    width, height = A4

    canvas.setFillColor(colors.HexColor("#00D8FF"))
    canvas.rect(0, height - 8, width, 8, stroke=0, fill=1)

    canvas.setStrokeColor(colors.HexColor("#D7DEE8"))
    canvas.setLineWidth(0.5)
    canvas.line(doc.leftMargin, 16 * mm, width - doc.rightMargin, 16 * mm)

    canvas.setFillColor(colors.HexColor("#5D687A"))
    canvas.setFont("DocRegular" if "DocRegular" in pdfmetrics.getRegisteredFontNames() else "Helvetica", 8)
    canvas.drawString(doc.leftMargin, 10 * mm, "Echo Protocol - Procesdocumentatie")
    canvas.drawRightString(width - doc.rightMargin, 10 * mm, f"Pagina {doc.page}")
    canvas.restoreState()


def main() -> int:
    if not SOURCE.exists():
        print(f"Source markdown not found: {SOURCE}", file=sys.stderr)
        return 1

    regular, bold, mono = register_fonts()
    styles = make_styles(regular, bold, mono)
    markdown = SOURCE.read_text(encoding="utf-8-sig")
    story = build_story(markdown, styles)

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)

    doc = SimpleDocTemplate(
        str(OUTPUT),
        pagesize=A4,
        rightMargin=17 * mm,
        leftMargin=17 * mm,
        topMargin=18 * mm,
        bottomMargin=20 * mm,
        title="Echo Protocol Procesdocumentatie",
        author="Renat",
        subject="Shader eindopdracht procesdocumentatie",
    )

    doc.build(story, onFirstPage=draw_header_footer, onLaterPages=draw_header_footer)
    print(OUTPUT)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
