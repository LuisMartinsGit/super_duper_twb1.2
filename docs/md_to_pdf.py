"""
Convert GDD_The_Waning_Border.md to a styled PDF using fpdf2.
Usage: python md_to_pdf.py
"""
import os
import re
from fpdf import FPDF

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MD_PATH = os.path.join(SCRIPT_DIR, "GDD_The_Waning_Border.md")
PDF_PATH = os.path.join(SCRIPT_DIR, "GDD_The_Waning_Border.pdf")

# Colors
PURPLE_DARK = (26, 26, 46)
PURPLE_MID = (44, 22, 84)
PURPLE_LIGHT = (123, 45, 142)
PURPLE_ACCENT = (74, 14, 92)
TH_BG = (61, 31, 109)
ROW_EVEN = (245, 240, 250)
ROW_ODD = (255, 255, 255)
TEXT_COLOR = (26, 26, 26)
CODE_BG = (240, 232, 245)


def sanitize(text):
    """Replace Unicode chars that standard PDF fonts can't handle."""
    replacements = {
        '\u2014': '--',   # em dash
        '\u2013': '-',    # en dash
        '\u2018': "'",    # left single quote
        '\u2019': "'",    # right single quote
        '\u201c': '"',    # left double quote
        '\u201d': '"',    # right double quote
        '\u2022': '-',    # bullet
        '\u2026': '...',  # ellipsis
        '\u00d7': 'x',   # multiplication sign
        '\u2212': '-',    # minus sign
        '\u2248': '~',    # approximately
        '\u2264': '<=',   # less than or equal
        '\u2265': '>=',   # greater than or equal
        '\u00b1': '+/-',  # plus-minus
        '\u2192': '->',   # right arrow
        '\u2190': '<-',   # left arrow
        '\u00b7': '.',    # middle dot
        '\u2019': "'",    # right single quotation
    }
    for old, new in replacements.items():
        text = text.replace(old, new)
    # Strip any remaining non-latin1 chars
    return text.encode('latin-1', errors='replace').decode('latin-1')


class GDDPdf(FPDF):
    def __init__(self):
        super().__init__('P', 'mm', 'A4')
        self.set_auto_page_break(True, margin=20)
        self.set_margins(20, 20, 20)

    def header(self):
        if self.page_no() > 1:
            self.set_font('Helvetica', 'I', 7)
            self.set_text_color(140, 140, 140)
            self.cell(0, 5, 'The Waning Border - Game Design Document', align='C')
            self.ln(3)

    def footer(self):
        self.set_y(-15)
        self.set_font('Helvetica', 'I', 7)
        self.set_text_color(140, 140, 140)
        self.cell(0, 10, f'Page {self.page_no()}', align='C')

    def section_title(self, title, level=1):
        """Render a heading."""
        title = sanitize(title)
        if level == 1:
            self.add_page()
            self.set_font('Helvetica', 'B', 20)
            self.set_text_color(*PURPLE_DARK)
            self.cell(0, 12, title, new_x="LMARGIN", new_y="NEXT")
            self.set_draw_color(*PURPLE_ACCENT)
            self.set_line_width(0.8)
            self.line(20, self.get_y(), 190, self.get_y())
            self.ln(5)
        elif level == 2:
            self.ln(5)
            self.set_font('Helvetica', 'B', 14)
            self.set_text_color(*PURPLE_MID)
            self.cell(0, 10, title, new_x="LMARGIN", new_y="NEXT")
            self.set_draw_color(*PURPLE_LIGHT)
            self.set_line_width(0.4)
            self.line(20, self.get_y(), 190, self.get_y())
            self.ln(3)
        elif level == 3:
            self.ln(4)
            self.set_font('Helvetica', 'B', 12)
            self.set_text_color(61, 31, 109)
            self.cell(0, 8, title, new_x="LMARGIN", new_y="NEXT")
            self.ln(2)
        elif level == 4:
            self.ln(3)
            self.set_font('Helvetica', 'B', 10)
            self.set_text_color(91, 58, 138)
            self.cell(0, 7, title, new_x="LMARGIN", new_y="NEXT")
            self.ln(1)

    def body_text(self, text):
        """Render body paragraph with inline bold and code."""
        self.set_text_color(*TEXT_COLOR)
        self.set_font('Helvetica', '', 9)
        # Process inline formatting
        self._write_rich_text(text)
        self.ln(3)

    def _write_rich_text(self, text):
        """Write text with bold (**text**) and code (`text`) formatting."""
        text = sanitize(text)
        # Split into segments by bold and code markers
        parts = re.split(r'(\*\*.*?\*\*|`[^`]+`)', text)
        for part in parts:
            if part.startswith('**') and part.endswith('**'):
                self.set_font('Helvetica', 'B', 9)
                self.set_text_color(*PURPLE_MID)
                self.write(5, part[2:-2])
                self.set_font('Helvetica', '', 9)
                self.set_text_color(*TEXT_COLOR)
            elif part.startswith('`') and part.endswith('`'):
                self.set_font('Courier', '', 8)
                self.set_text_color(80, 40, 120)
                self.write(5, part[1:-1])
                self.set_font('Helvetica', '', 9)
                self.set_text_color(*TEXT_COLOR)
            else:
                self.write(5, part)

    def bullet_item(self, text, indent=0):
        """Render a bullet point."""
        x = 22 + indent * 6
        self.set_x(x)
        self.set_font('Helvetica', '', 9)
        self.set_text_color(*TEXT_COLOR)
        bullet = '-'  # bullet character
        self.cell(5, 5, bullet)
        self._write_rich_text(text)
        self.ln(2)

    def render_table(self, headers, rows):
        """Render a table with styled headers and alternating row colors."""
        headers = [sanitize(h) for h in headers]
        rows = [[sanitize(c) for c in row] for row in rows]
        page_width = 170  # usable width
        n_cols = len(headers)

        # Calculate column widths based on content
        col_widths = self._calc_col_widths(headers, rows, page_width)

        # Check if table fits on current page (rough estimate)
        needed = 8 + len(rows) * 6
        if self.get_y() + needed > 270:
            self.add_page()

        # Header row
        self.set_font('Helvetica', 'B', 8)
        self.set_fill_color(*TH_BG)
        self.set_text_color(255, 255, 255)
        for i, h in enumerate(headers):
            self.cell(col_widths[i], 7, h, border=1, fill=True, align='C')
        self.ln()

        # Data rows
        self.set_font('Helvetica', '', 8)
        for row_idx, row in enumerate(rows):
            bg = ROW_EVEN if row_idx % 2 == 0 else ROW_ODD
            self.set_fill_color(*bg)
            self.set_text_color(*TEXT_COLOR)

            # Calculate row height based on content
            max_lines = 1
            cell_texts = []
            for i, cell in enumerate(row):
                cell = cell.strip()
                # Estimate lines needed
                if col_widths[i] > 0:
                    char_width = 1.8  # approx chars per mm for 8pt
                    max_chars = int(col_widths[i] * char_width)
                    if max_chars > 0:
                        lines = max(1, (len(cell) + max_chars - 1) // max_chars)
                    else:
                        lines = 1
                else:
                    lines = 1
                max_lines = max(max_lines, lines)
                cell_texts.append(cell)

            row_height = max(6, max_lines * 4.5)

            for i, cell in enumerate(cell_texts):
                x = self.get_x()
                y = self.get_y()
                self.rect(x, y, col_widths[i], row_height, 'DF')
                self.set_xy(x + 1, y + 1)
                # Use multi_cell for wrapping but restore position
                self.multi_cell(col_widths[i] - 2, 4, cell, border=0)
                self.set_xy(x + col_widths[i], y)

            self.set_y(self.get_y() + row_height)

            # Check for page break mid-table
            if self.get_y() > 270:
                self.add_page()
                # Re-draw header on new page
                self.set_font('Helvetica', 'B', 8)
                self.set_fill_color(*TH_BG)
                self.set_text_color(255, 255, 255)
                for i, h in enumerate(headers):
                    self.cell(col_widths[i], 7, h, border=1, fill=True, align='C')
                self.ln()
                self.set_font('Helvetica', '', 8)

        self.ln(3)

    def _calc_col_widths(self, headers, rows, total_width):
        """Calculate column widths proportional to content length."""
        n_cols = len(headers)
        if n_cols == 0:
            return []

        max_lens = [len(h) for h in headers]
        for row in rows:
            for i, cell in enumerate(row):
                if i < n_cols:
                    max_lens[i] = max(max_lens[i], len(cell.strip()))

        # Give a minimum width of 15mm and distribute proportionally
        total_chars = sum(max_lens) or 1
        widths = [max(12, (l / total_chars) * total_width) for l in max_lens]

        # Normalize to total_width
        scale = total_width / sum(widths)
        widths = [w * scale for w in widths]
        return widths

    def code_block(self, text):
        """Render a code block."""
        text = sanitize(text)
        self.set_fill_color(26, 26, 46)
        self.set_text_color(224, 208, 240)
        self.set_font('Courier', '', 7.5)

        lines = text.split('\n')
        block_height = len(lines) * 4 + 6
        if self.get_y() + block_height > 275:
            self.add_page()

        x = self.get_x()
        y = self.get_y()
        self.rect(x, y, 170, block_height, 'F')
        self.set_xy(x + 3, y + 3)
        for line in lines:
            self.cell(164, 4, line, new_x="LMARGIN", new_y="NEXT")
            self.set_x(x + 3)
        self.set_y(y + block_height + 2)
        self.set_text_color(*TEXT_COLOR)

    def hr_line(self):
        """Render a horizontal rule."""
        self.set_draw_color(*PURPLE_LIGHT)
        self.set_line_width(0.5)
        y = self.get_y() + 3
        self.line(20, y, 190, y)
        self.ln(8)


def parse_markdown(md_text):
    """Parse markdown into structured elements."""
    elements = []
    lines = md_text.split('\n')
    i = 0
    in_code_block = False
    code_lines = []
    in_table = False
    table_headers = []
    table_rows = []

    while i < len(lines):
        line = lines[i]

        # Code blocks
        if line.strip().startswith('```'):
            if in_code_block:
                elements.append(('code', '\n'.join(code_lines)))
                code_lines = []
                in_code_block = False
            else:
                # Flush table if any
                if in_table:
                    elements.append(('table', table_headers, table_rows))
                    in_table = False
                    table_headers = []
                    table_rows = []
                in_code_block = True
            i += 1
            continue

        if in_code_block:
            code_lines.append(line)
            i += 1
            continue

        # Table rows
        if '|' in line and line.strip().startswith('|'):
            cells = [c.strip() for c in line.strip().strip('|').split('|')]

            # Check if separator row
            if all(re.match(r'^[-:]+$', c) for c in cells):
                i += 1
                continue

            if not in_table:
                in_table = True
                table_headers = cells
            else:
                table_rows.append(cells)
            i += 1
            continue
        elif in_table:
            elements.append(('table', table_headers, table_rows))
            in_table = False
            table_headers = []
            table_rows = []

        stripped = line.strip()

        # Headings
        if stripped.startswith('#### '):
            elements.append(('h4', stripped[5:].strip()))
        elif stripped.startswith('### '):
            elements.append(('h3', stripped[4:].strip()))
        elif stripped.startswith('## '):
            elements.append(('h2', stripped[3:].strip()))
        elif stripped.startswith('# '):
            elements.append(('h1', stripped[2:].strip()))
        elif stripped == '---':
            elements.append(('hr',))
        elif stripped.startswith('- '):
            # Determine indent level
            indent = (len(line) - len(line.lstrip())) // 2
            elements.append(('bullet', stripped[2:], indent))
        elif stripped.startswith('* '):
            indent = (len(line) - len(line.lstrip())) // 2
            elements.append(('bullet', stripped[2:], indent))
        elif stripped:
            elements.append(('text', stripped))

        i += 1

    # Flush remaining
    if in_table:
        elements.append(('table', table_headers, table_rows))
    if in_code_block and code_lines:
        elements.append(('code', '\n'.join(code_lines)))

    return elements


def main():
    print(f"Reading: {MD_PATH}")
    with open(MD_PATH, "r", encoding="utf-8") as f:
        md_text = f.read()

    print("Parsing Markdown...")
    elements = parse_markdown(md_text)

    print("Generating PDF...")
    pdf = GDDPdf()
    pdf.add_page()

    # Title page
    pdf.ln(40)
    pdf.set_font('Helvetica', 'B', 32)
    pdf.set_text_color(*PURPLE_DARK)
    pdf.cell(0, 15, 'The Waning Border', align='C', new_x="LMARGIN", new_y="NEXT")
    pdf.set_font('Helvetica', '', 16)
    pdf.set_text_color(*PURPLE_MID)
    pdf.cell(0, 10, 'Game Design Document', align='C', new_x="LMARGIN", new_y="NEXT")
    pdf.ln(5)
    pdf.set_draw_color(*PURPLE_ACCENT)
    pdf.set_line_width(1.0)
    pdf.line(60, pdf.get_y(), 150, pdf.get_y())
    pdf.ln(10)
    pdf.set_font('Helvetica', '', 10)
    pdf.set_text_color(100, 100, 100)
    pdf.cell(0, 7, 'Version 1.2', align='C', new_x="LMARGIN", new_y="NEXT")
    pdf.cell(0, 7, 'Unity 6 (6000.0.37f1) - DOTS/ECS (Entities 1.3.14)', align='C', new_x="LMARGIN", new_y="NEXT")
    pdf.cell(0, 7, 'Genre: Real-Time Strategy - Players: 1-8', align='C', new_x="LMARGIN", new_y="NEXT")
    pdf.cell(0, 7, 'Platform: PC (Windows)', align='C', new_x="LMARGIN", new_y="NEXT")
    pdf.ln(15)
    pdf.cell(0, 7, 'March 2026', align='C', new_x="LMARGIN", new_y="NEXT")

    # Skip the title elements in the parsed content (already rendered as title page)
    skip_title = True

    for elem in elements:
        etype = elem[0]

        # Skip the very first heading since we already have a title page
        if skip_title and etype == 'h1':
            skip_title = False
            continue
        skip_title = False

        if etype == 'h1':
            pdf.section_title(elem[1], 1)
        elif etype == 'h2':
            pdf.section_title(elem[1], 2)
        elif etype == 'h3':
            pdf.section_title(elem[1], 3)
        elif etype == 'h4':
            pdf.section_title(elem[1], 4)
        elif etype == 'text':
            pdf.body_text(elem[1])
        elif etype == 'bullet':
            pdf.bullet_item(elem[1], elem[2] if len(elem) > 2 else 0)
        elif etype == 'table':
            headers = elem[1]
            rows = elem[2]
            # Ensure all rows have same column count as headers
            norm_rows = []
            for row in rows:
                if len(row) < len(headers):
                    row = row + [''] * (len(headers) - len(row))
                elif len(row) > len(headers):
                    row = row[:len(headers)]
                norm_rows.append(row)
            pdf.render_table(headers, norm_rows)
        elif etype == 'code':
            pdf.code_block(elem[1])
        elif etype == 'hr':
            pdf.hr_line()

    print(f"Writing PDF: {PDF_PATH}")
    pdf.output(PDF_PATH)

    size_kb = os.path.getsize(PDF_PATH) / 1024
    print(f"Done! PDF created: {PDF_PATH} ({size_kb:.0f} KB)")


if __name__ == "__main__":
    main()
