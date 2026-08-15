#!/usr/bin/env python3
"""Academic_Report.md -> Academic_Report.pdf, laid out as a submission document.

Usage:  python3 Docs/build_pdf.py
Needs:  pip install markdown pypdf reportlab   (+ Google Chrome, driven headless to print)

What it does beyond a plain Markdown render:
  * a dedicated title page, then the abstract, then a two-level contents page
  * real page numbers in the contents, resolved by printing, measuring and reprinting
    until the mapping stops changing (adding the numbers can itself move a page break)
  * roman numerals on the front matter, arabic numbering from Chapter 1
  * a running chapter title in the header of every body page
Rebuilds in place, so the committed PDF always matches the Markdown.
"""
import io, os, re, subprocess, sys

DOCS = os.path.dirname(os.path.abspath(__file__))
MD = os.path.join(DOCS, "Academic_Report.md")
HTML = os.path.join(DOCS, ".report_build.html")      # intermediate, safe to delete
PDF = os.path.join(DOCS, "Academic_Report.pdf")
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

import markdown

CSS = """
@page { size: A4; margin: 22mm 18mm 20mm 18mm; }

html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
body {
  font-family: "Charter", "Georgia", "Times New Roman", serif;
  font-size: 10.5pt; line-height: 1.52; color: #14161a; margin: 0; padding: 0;
}

/* ---- title page ----------------------------------------------------------- */
.titlepage { page-break-after: always; break-after: page; padding-top: 32mm; }
.titlepage h1 {
  font-size: 21pt; line-height: 1.3; font-weight: 600; margin: 0 0 9mm 0;
  letter-spacing: -0.2pt;
}
.titlepage .kicker { font-size: 12pt; color: #3d434b; margin: 0 0 13mm 0; letter-spacing: 0.2pt; }
.titlepage table { width: 100%; font-size: 10pt; margin-top: 5mm; }
.titlepage table th { display: none; }
.titlepage table td { border: none; border-bottom: 0.4pt solid #dde1e7; padding: 4.5pt 2pt; background: none; }
.titlepage table td:first-child { width: 34%; font-weight: 600; }

/* ---- front matter (abstract, contents) ------------------------------------ */
.front { page-break-after: always; break-after: page; }
.front h2 { page-break-before: avoid; break-before: auto; }

ul.toc { list-style: none; margin: 0; padding: 0; font-size: 10.5pt; }
ul.toc li { display: flex; align-items: baseline; margin: 0 0 3.5pt 0; }
ul.toc li.sub { font-size: 9.7pt; padding-left: 9mm; color: #2c3138; margin-bottom: 2pt; }
ul.toc li.chap { font-weight: 600; margin-top: 6pt; }
ul.toc .leader { flex: 1; margin: 0 4pt; border-bottom: 0.5pt dotted #b6bcc5; transform: translateY(-2.5pt); }
ul.toc .pg { font-variant-numeric: tabular-nums; color: #2c3138; }

/* ---- headings ------------------------------------------------------------- */
h2, h3, h4 { font-weight: 600; page-break-after: avoid; break-after: avoid; letter-spacing: -0.1pt; }
h2 { font-size: 15pt; margin: 0 0 11pt 0; padding-bottom: 4pt; border-bottom: 0.7pt solid #c9ced6; }
h3 { font-size: 12pt; margin: 16pt 0 6pt 0; }
h4 { font-size: 10.8pt; margin: 13pt 0 5pt 0; }
.body h2 { page-break-before: always; break-before: page; }
.body > h2:first-child { page-break-before: avoid; break-before: auto; }

/* ---- text ----------------------------------------------------------------- */
p { margin: 0 0 8pt 0; orphans: 3; widows: 3; text-align: justify; hyphens: auto; }
.titlepage p, ul.toc li { text-align: left; hyphens: manual; }

ul, ol { margin: 0 0 9pt 0; padding-left: 17pt; }
li { margin-bottom: 3.5pt; text-align: justify; hyphens: auto; }
li > ul, li > ol { margin-top: 3.5pt; }

/* ---- tables --------------------------------------------------------------- */
table { width: 100%; border-collapse: collapse; margin: 9pt 0 12pt 0; font-size: 8.9pt; line-height: 1.38; }
thead { display: table-header-group; }
tr { page-break-inside: avoid; break-inside: avoid; }
th, td {
  border: 0.5pt solid #c9ced6; padding: 3.6pt 5pt; text-align: left;
  vertical-align: top; word-wrap: break-word; hyphens: none;
}
th { background: #eef1f5; font-weight: 600; }
tbody tr:nth-child(even) td { background: #f8f9fb; }
/* after the striping rule, or it would win on equal specificity and stripe the title page */
.titlepage table tbody tr td { background: none; }

/* ---- code ---------------------------------------------------------------- */
code {
  font-family: "SF Mono", "Menlo", "Consolas", monospace;
  font-size: 8.7pt; background: #f1f3f6; padding: 0.5pt 2.5pt; border-radius: 2pt;
  word-wrap: break-word; hyphens: none;
}
pre {
  background: #f6f7f9; border: 0.5pt solid #d7dbe2; border-radius: 3pt;
  padding: 7pt 9pt; margin: 8pt 0 11pt 0; font-size: 8.2pt; line-height: 1.42;
  white-space: pre-wrap; page-break-inside: avoid; break-inside: avoid; text-align: left;
}
pre code { background: none; padding: 0; font-size: inherit; }

/* ---- misc ---------------------------------------------------------------- */
blockquote {
  margin: 9pt 0 11pt 0; padding: 2pt 0 2pt 11pt;
  border-left: 2pt solid #9aa3ae; color: #34383e; font-style: italic;
}
hr { display: none; }
a { color: #14161a; text-decoration: none; }
strong { font-weight: 600; }
"""

MD_EXT = ["tables", "fenced_code", "sane_lists", "attr_list"]
PT_PER_MM = 2.83465


# ---------------------------------------------------------------- source split
def split_source(src):
    """title block / abstract / body, plus the heading list the contents is built from."""
    title_md = src[:src.index("\n---\n")]

    abs_start = src.index("## Abstract")
    abs_md = src[abs_start:src.index("\n---\n", abs_start)]

    body_md = src[src.index("## 1. Introduction"):]

    headings = []
    for line in body_md.splitlines():
        if line.startswith("## "):
            headings.append(("chap", line[3:].strip()))
        elif line.startswith("### "):
            headings.append(("sub", line[4:].strip()))
    return title_md, abs_md, body_md, headings


def title_html(title_md):
    lines = title_md.strip().splitlines()
    h1 = lines[0].lstrip("# ").strip()
    kicker = lines[2].strip().strip("*") if len(lines) > 2 else ""
    table = markdown.markdown("\n".join(lines[3:]), extensions=MD_EXT)
    return f"<div class='titlepage'><h1>{h1}</h1><p class='kicker'>{kicker}</p>{table}</div>"


def toc_html(headings, pages):
    """pages maps heading text -> printed page label; an unresolved entry renders as a dash."""
    rows = []
    for kind, text in headings:
        cls = "chap" if kind == "chap" else "sub"
        rows.append(f"<li class='{cls}'><span>{text}</span>"
                    f"<span class='leader'></span><span class='pg'>{pages.get(text, '—')}</span></li>")
    return "<div class='front'><h2>Contents</h2><ul class='toc'>" + "".join(rows) + "</ul></div>"


def compose(title_md, abs_md, body_md, headings, pages):
    return ("<!doctype html><html lang='en'><head><meta charset='utf-8'>"
            "<title>POS Tech Support — Academic Report</title>"
            f"<style>{CSS}</style></head><body>"
            f"{title_html(title_md)}"
            f"<div class='front'>{markdown.markdown(abs_md, extensions=MD_EXT)}</div>"
            f"{toc_html(headings, pages)}"
            f"<div class='body'>{markdown.markdown(body_md, extensions=MD_EXT)}</div>"
            "</body></html>")


# ---------------------------------------------------------------- print + measure
def print_pdf(doc_html):
    io.open(HTML, "w", encoding="utf-8").write(doc_html)
    r = subprocess.run(
        [CHROME, "--headless", "--disable-gpu", "--no-sandbox", "--no-pdf-header-footer",
         "--virtual-time-budget=20000", f"--print-to-pdf={PDF}", "file://" + HTML],
        capture_output=True, text=True, timeout=300)
    if not os.path.exists(PDF):
        print("chrome failed\n", r.stdout, r.stderr)
        sys.exit(1)


def norm(s):
    return re.sub(r"\s+", " ", re.sub(r"[^0-9a-zA-Z. ]+", " ", s)).strip().lower()


def measure(headings):
    """Which PDF page each heading lands on, and where the body starts."""
    from pypdf import PdfReader
    reader = PdfReader(PDF)
    texts = [norm(p.extract_text() or "") for p in reader.pages]

    body_start = 1
    for i, t in enumerate(texts):
        if t.startswith("1. introduction"):
            body_start = i
            break

    found, cursor = {}, body_start
    for _, text in headings:
        needle = norm(text)
        for i in range(cursor, len(texts)):
            if needle and needle in texts[i]:
                found[text] = i
                cursor = i
                break
    return found, body_start, len(reader.pages)


def label_map(found, body_start, total):
    """Front matter gets roman numerals, the body arabic from 1; the title page is unnumbered."""
    roman = ["i", "ii", "iii", "iv", "v", "vi", "vii", "viii"]
    labels = {}
    for i in range(total):
        if i == 0:
            labels[i] = ""
        elif i < body_start:
            labels[i] = roman[min(i - 1, len(roman) - 1)]
        else:
            labels[i] = str(i - body_start + 1)
    return labels, {text: labels[page] for text, page in found.items()}


# ---------------------------------------------------------------- stamping
def stamp(labels, found, headings, body_start):
    """Page number bottom-centre, running chapter title top-right. Merged in after printing
    because Chrome cannot print either without also printing a URL/date footer."""
    from pypdf import PdfReader, PdfWriter
    from reportlab.pdfgen import canvas
    from reportlab.lib.pagesizes import A4

    chapter_at = {found[t]: t for k, t in headings if k == "chap" and t in found}
    running, current = {}, None
    for i in range(len(labels)):
        current = chapter_at.get(i, current)
        running[i] = current if i >= body_start else None

    overlay_path = os.path.join(DOCS, ".page_numbers.pdf")
    c = canvas.Canvas(overlay_path, pagesize=A4)
    w, h = A4
    for i in range(len(labels)):
        if labels[i]:
            c.setFont("Times-Roman", 9)
            c.setFillGray(0.35)
            c.drawCentredString(w / 2, 11 * PT_PER_MM, labels[i])
        if running.get(i):
            c.setFont("Times-Italic", 8)
            c.setFillGray(0.5)
            c.drawRightString(w - 18 * PT_PER_MM, h - 13 * PT_PER_MM, running[i])
        c.showPage()
    c.save()

    writer = PdfWriter(clone_from=PDF)
    overlay = PdfReader(overlay_path)
    for i, page in enumerate(writer.pages):
        page.merge_page(overlay.pages[i])
        page.compress_content_streams()
    writer.compress_identical_objects()
    writer.add_metadata({
        "/Title": "Designing a Diagnostic-Reasoning Serious Game for Point-of-Sale Technical Support",
        "/Subject": "Final Year Project — Academic Report",
        "/Keywords": "serious games; troubleshooting; diagnostic reasoning; game architecture; "
                     "ScriptableObject; large language models; Unity",
    })
    with open(PDF, "wb") as f:
        writer.write(f)
    os.remove(overlay_path)


def main():
    title_md, abs_md, body_md, headings = split_source(io.open(MD, encoding="utf-8").read())

    # Contents page numbers change pagination, so print → measure → reprint until stable.
    toc_pages, labels, found, body_start, total = {}, {}, {}, 1, 0
    for attempt in range(1, 5):
        print_pdf(compose(title_md, abs_md, body_md, headings, toc_pages))
        found, body_start, total = measure(headings)
        labels, fresh = label_map(found, body_start, total)
        if fresh == toc_pages:
            print(f"    contents stable after {attempt} pass(es)")
            break
        toc_pages = fresh
    else:
        print("    contents did not settle in 4 passes — numbers may be off by a page")

    stamp(labels, found, headings, body_start)
    if os.path.exists(HTML):
        os.remove(HTML)
    print(f"OK  {PDF}\n    {total} pages ({body_start} front matter), {os.path.getsize(PDF)/1024:.0f} KB")


if __name__ == "__main__":
    main()
