#!/usr/bin/env node
/*
 * generate-requirements-docx.js
 *
 * Generates "docs/NNM Advisory - Requirements Specification.docx" from the
 * authoritative requirements markdown at
 * .kiro/specs/nnm-advisory-website/requirements.md
 *
 * Produces an Office Open XML (.docx) package containing:
 *   - A cover page with three distinct, labelled fields:
 *       Project Name:  NNM Advisory Website
 *       Document Type: Requirements Specification
 *       Date Created:  <DD Month YYYY>
 *   - A Table of Contents field (TOC \o "1-2") listing top-level and
 *     second-level headings with page numbers (Word populates page numbers
 *     on open / field update).
 *   - Every section from requirements.md with heading levels mapped 1:1 to
 *     Word built-in heading styles:
 *       #    -> Heading1
 *       ##   -> Heading2
 *       ###  -> Heading3
 *       #### -> Heading4
 *   - Numbered acceptance criteria and bulleted glossary / nested lists.
 *
 * No third-party dependencies: uses only Node's built-in `zlib` and `fs`.
 * A minimal, correct ZIP (store/deflate) writer builds the .docx package.
 *
 * Usage:  node docs/generate-requirements-docx.js
 */

'use strict';

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

// ---------------------------------------------------------------------------
// Paths
// ---------------------------------------------------------------------------
const repoRoot = path.resolve(__dirname, '..');
const sourceMd = path.join(
  repoRoot,
  '.kiro',
  'specs',
  'nnm-advisory-website',
  'requirements.md'
);
const outDir = path.join(repoRoot, 'docs');
const outDocx = path.join(outDir, 'NNM Advisory - Requirements Specification.docx');

// ---------------------------------------------------------------------------
// XML helpers
// ---------------------------------------------------------------------------
function escapeXml(s) {
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

/**
 * Render a run of text, splitting on **bold** markers. Preserves spaces with
 * xml:space="preserve".
 */
function runsFromInline(text) {
  const parts = [];
  // Split on bold segments **...**
  const regex = /\*\*(.+?)\*\*/g;
  let last = 0;
  let m;
  while ((m = regex.exec(text)) !== null) {
    if (m.index > last) {
      parts.push({ text: text.slice(last, m.index), bold: false });
    }
    parts.push({ text: m[1], bold: true });
    last = regex.lastIndex;
  }
  if (last < text.length) {
    parts.push({ text: text.slice(last), bold: false });
  }
  if (parts.length === 0) {
    parts.push({ text: '', bold: false });
  }
  return parts
    .map((p) => {
      const rpr = p.bold ? '<w:rPr><w:b/></w:rPr>' : '';
      return `<w:r>${rpr}<w:t xml:space="preserve">${escapeXml(p.text)}</w:t></w:r>`;
    })
    .join('');
}

function paragraph(text, { style, bold, size, align, spacingAfter, color } = {}) {
  const pPrParts = [];
  if (style) pPrParts.push(`<w:pStyle w:val="${style}"/>`);
  if (align) pPrParts.push(`<w:jc w:val="${align}"/>`);
  if (spacingAfter != null) pPrParts.push(`<w:spacing w:after="${spacingAfter}"/>`);
  const pPr = pPrParts.length ? `<w:pPr>${pPrParts.join('')}</w:pPr>` : '';

  const rPrParts = [];
  if (bold) rPrParts.push('<w:b/>');
  if (size) rPrParts.push(`<w:sz w:val="${size}"/><w:szCs w:val="${size}"/>`);
  if (color) rPrParts.push(`<w:color w:val="${color}"/>`);
  const rPr = rPrParts.length ? `<w:rPr>${rPrParts.join('')}</w:rPr>` : '';

  return `<w:p>${pPr}<w:r>${rPr}<w:t xml:space="preserve">${escapeXml(text)}</w:t></w:r></w:p>`;
}

function headingParagraph(text, styleId) {
  return `<w:p><w:pPr><w:pStyle w:val="${styleId}"/></w:pPr>${runsFromInline(text)}</w:p>`;
}

function bodyParagraph(text) {
  return `<w:p>${runsFromInline(text)}</w:p>`;
}

/** A list item paragraph using numbering definition `numId` at `ilvl`. */
function listParagraph(text, numId, ilvl) {
  return (
    `<w:p><w:pPr><w:pStyle w:val="ListParagraph"/>` +
    `<w:numPr><w:ilvl w:val="${ilvl}"/><w:numId w:val="${numId}"/></w:numPr>` +
    `</w:pPr>${runsFromInline(text)}</w:p>`
  );
}

function pageBreak() {
  return '<w:p><w:r><w:br w:type="page"/></w:r></w:p>';
}

// ---------------------------------------------------------------------------
// Markdown parsing -> ordered list of document blocks
// ---------------------------------------------------------------------------
function parseMarkdown(md) {
  const lines = md.split(/\r?\n/);
  const blocks = [];

  for (let i = 0; i < lines.length; i++) {
    const raw = lines[i];
    const line = raw.replace(/\s+$/, '');

    if (line.trim() === '') continue;
    if (line.trim() === '---') continue; // horizontal rule separators

    const h = /^(#{1,6})\s+(.*)$/.exec(line);
    if (h) {
      blocks.push({ type: 'heading', level: h[1].length, text: h[2].trim() });
      continue;
    }

    // Ordered list item:  "1. text"   (top level, no indent)
    const ol = /^(\d+)\.\s+(.*)$/.exec(line);
    if (ol) {
      blocks.push({ type: 'ol', indent: 0, text: ol[2].trim() });
      continue;
    }

    // Nested bullet under an ordered item:  "   - text" (indented dash)
    const nestedBullet = /^(\s+)-\s+(.*)$/.exec(raw);
    if (nestedBullet) {
      blocks.push({ type: 'ul', indent: 1, text: nestedBullet[2].trim() });
      continue;
    }

    // Top-level bullet:  "- text"
    const ul = /^-\s+(.*)$/.exec(line);
    if (ul) {
      blocks.push({ type: 'ul', indent: 0, text: ul[1].trim() });
      continue;
    }

    // Plain paragraph
    blocks.push({ type: 'p', text: line.trim() });
  }

  return blocks;
}

// ---------------------------------------------------------------------------
// Build document.xml body from blocks
// ---------------------------------------------------------------------------
const HEADING_STYLE = {
  1: 'Heading1',
  2: 'Heading2',
  3: 'Heading3',
  4: 'Heading4',
  5: 'Heading5',
  6: 'Heading6',
};

function formatDate(d) {
  const months = [
    'January', 'February', 'March', 'April', 'May', 'June',
    'July', 'August', 'September', 'October', 'November', 'December',
  ];
  const dd = String(d.getDate()).padStart(2, '0');
  return `${dd} ${months[d.getMonth()]} ${d.getFullYear()}`;
}

function buildCoverPage() {
  const dateStr = formatDate(new Date());
  const parts = [];
  // Vertical spacing to push content down the cover page
  parts.push(paragraph('', { spacingAfter: 2400 }));
  parts.push(paragraph('', { spacingAfter: 2400 }));

  // Main title
  parts.push(
    paragraph('NNM Advisory', {
      align: 'center',
      bold: true,
      size: 72,
      color: '1F3864',
      spacingAfter: 120,
    })
  );
  parts.push(
    paragraph('Requirements Specification', {
      align: 'center',
      bold: true,
      size: 44,
      color: '2E74B5',
      spacingAfter: 2400,
    })
  );

  // Three distinct, labelled fields
  parts.push(labelledField('Project Name', 'NNM Advisory Website'));
  parts.push(labelledField('Document Type', 'Requirements Specification'));
  parts.push(labelledField('Date Created', dateStr));

  parts.push(pageBreak());
  return parts.join('');
}

/** A single labelled cover-page field: bold label + value on one centered line. */
function labelledField(label, value) {
  return (
    `<w:p><w:pPr><w:jc w:val="center"/><w:spacing w:after="240"/></w:pPr>` +
    `<w:r><w:rPr><w:b/><w:sz w:val="28"/><w:szCs w:val="28"/></w:rPr>` +
    `<w:t xml:space="preserve">${escapeXml(label)}: </w:t></w:r>` +
    `<w:r><w:rPr><w:sz w:val="28"/><w:szCs w:val="28"/></w:rPr>` +
    `<w:t xml:space="preserve">${escapeXml(value)}</w:t></w:r>` +
    `</w:p>`
  );
}

function buildTableOfContents() {
  const parts = [];
  parts.push(headingParagraph('Table of Contents', 'TOCHeading'));
  // TOC field spanning heading levels 1-2 (top-level and second-level).
  // Word prompts to update field (populates page numbers) on open.
  parts.push(
    '<w:p>' +
      '<w:pPr><w:pStyle w:val="TOC1"/></w:pPr>' +
      '<w:r><w:fldChar w:fldCharType="begin" w:dirty="true"/></w:r>' +
      '<w:r><w:instrText xml:space="preserve"> TOC \\o "1-2" \\h \\z \\u </w:instrText></w:r>' +
      '<w:r><w:fldChar w:fldCharType="separate"/></w:r>' +
      '<w:r><w:rPr><w:i/></w:rPr><w:t xml:space="preserve">Right-click and choose "Update Field" to populate the table of contents with page numbers.</w:t></w:r>' +
      '<w:r><w:fldChar w:fldCharType="end"/></w:r>' +
    '</w:p>'
  );
  parts.push(pageBreak());
  return parts.join('');
}

function buildBodyFromBlocks(blocks) {
  const out = [];
  // Numbering: numId 1 is a multi-level ordered list; numId 2 is bullets.
  // We restart the ordered numbering at the start of each "Acceptance
  // Criteria" heading so every requirement's criteria count from 1.
  let orderedNumId = 1;

  for (let i = 0; i < blocks.length; i++) {
    const b = blocks[i];
    switch (b.type) {
      case 'heading': {
        const style = HEADING_STYLE[b.level] || 'Heading6';
        out.push(headingParagraph(b.text, style));
        // When we hit an "Acceptance Criteria" heading, allocate a fresh
        // numbering instance so the list restarts at 1.
        if (/acceptance criteria/i.test(b.text)) {
          orderedNumId = allocateOrderedNum();
        }
        break;
      }
      case 'ol':
        out.push(listParagraph(b.text, orderedNumId, 0));
        break;
      case 'ul':
        out.push(listParagraph(b.text, 2, b.indent === 1 ? 1 : 0));
        break;
      case 'p':
      default:
        out.push(bodyParagraph(b.text));
        break;
    }
  }
  return out.join('');
}

// Ordered-list numbering instances. Each "Acceptance Criteria" block gets its
// own numId (all pointing at the same abstract definition) so numbering
// restarts at 1 for every requirement.
const orderedNumInstances = []; // numId values
let nextNumId = 3; // 1 reserved (first ordered), 2 reserved (bullets)
function allocateOrderedNum() {
  const id = nextNumId++;
  orderedNumInstances.push(id);
  return id;
}

// ---------------------------------------------------------------------------
// numbering.xml
// ---------------------------------------------------------------------------
function buildNumberingXml() {
  // Abstract 0: decimal ordered list (one level used, lvl 0)
  // Abstract 1: bullet list (two levels)
  const abstractOrdered =
    '<w:abstractNum w:abstractNumId="0">' +
    '<w:multiLevelType w:val="singleLevel"/>' +
    '<w:lvl w:ilvl="0">' +
    '<w:start w:val="1"/><w:numFmt w:val="decimal"/>' +
    '<w:lvlText w:val="%1."/><w:lvlJc w:val="left"/>' +
    '<w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr>' +
    '</w:lvl>' +
    '</w:abstractNum>';

  const abstractBullet =
    '<w:abstractNum w:abstractNumId="1">' +
    '<w:multiLevelType w:val="hybridMultilevel"/>' +
    '<w:lvl w:ilvl="0">' +
    '<w:start w:val="1"/><w:numFmt w:val="bullet"/>' +
    '<w:lvlText w:val="\u2022"/><w:lvlJc w:val="left"/>' +
    '<w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr>' +
    '<w:rPr><w:rFonts w:ascii="Symbol" w:hAnsi="Symbol" w:hint="default"/></w:rPr>' +
    '</w:lvl>' +
    '<w:lvl w:ilvl="1">' +
    '<w:start w:val="1"/><w:numFmt w:val="bullet"/>' +
    '<w:lvlText w:val="o"/><w:lvlJc w:val="left"/>' +
    '<w:pPr><w:ind w:left="1440" w:hanging="360"/></w:pPr>' +
    '<w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New" w:hint="default"/></w:rPr>' +
    '</w:lvl>' +
    '</w:abstractNum>';

  // num instances
  const nums = [];
  // numId 1 -> ordered abstract 0 (default first ordered list)
  nums.push('<w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>');
  // numId 2 -> bullet abstract 1
  nums.push('<w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num>');
  // Each allocated ordered instance restarts numbering via lvlOverride.
  for (const id of orderedNumInstances) {
    nums.push(
      `<w:num w:numId="${id}"><w:abstractNumId w:val="0"/>` +
        '<w:lvlOverride w:ilvl="0"><w:startOverride w:val="1"/></w:lvlOverride>' +
        '</w:num>'
    );
  }

  return (
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">' +
    abstractOrdered +
    abstractBullet +
    nums.join('') +
    '</w:numbering>'
  );
}

// ---------------------------------------------------------------------------
// styles.xml  (built-in heading styles + TOC styles)
// ---------------------------------------------------------------------------
function buildStylesXml() {
  const heading = (id, name, outlineLvl, size, color, before) =>
    `<w:style w:type="paragraph" w:styleId="${id}">` +
    `<w:name w:val="${name}"/>` +
    '<w:basedOn w:val="Normal"/>' +
    '<w:next w:val="Normal"/>' +
    '<w:qFormat/>' +
    `<w:pPr><w:keepNext/><w:spacing w:before="${before}" w:after="120"/>` +
    `<w:outlineLvl w:val="${outlineLvl}"/></w:pPr>` +
    `<w:rPr><w:rFonts w:asciiTheme="majorHAnsi" w:hAnsiTheme="majorHAnsi"/>` +
    `<w:b/><w:color w:val="${color}"/><w:sz w:val="${size}"/><w:szCs w:val="${size}"/></w:rPr>` +
    '</w:style>';

  const tocStyle = (id, name, indent) =>
    `<w:style w:type="paragraph" w:styleId="${id}">` +
    `<w:name w:val="${name}"/>` +
    '<w:basedOn w:val="Normal"/><w:next w:val="Normal"/>' +
    `<w:pPr><w:spacing w:after="100"/><w:ind w:left="${indent}"/></w:pPr>` +
    '</w:style>';

  return (
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">' +
    // Doc defaults
    '<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Calibri"/>' +
    '<w:sz w:val="22"/><w:szCs w:val="22"/></w:rPr></w:rPrDefault>' +
    '<w:pPrDefault><w:pPr><w:spacing w:after="160" w:line="259" w:lineRule="auto"/></w:pPr></w:pPrDefault>' +
    '</w:docDefaults>' +
    // Normal
    '<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>' +
    // Headings 1-6 -> built-in names so Word recognises them for the TOC
    heading('Heading1', 'heading 1', 0, 36, '1F3864', 360) +
    heading('Heading2', 'heading 2', 1, 30, '2E74B5', 280) +
    heading('Heading3', 'heading 3', 2, 26, '2E74B5', 240) +
    heading('Heading4', 'heading 4', 3, 24, '404040', 200) +
    heading('Heading5', 'heading 5', 4, 22, '404040', 160) +
    heading('Heading6', 'heading 6', 5, 22, '404040', 160) +
    // TOC heading (not itself in the TOC)
    '<w:style w:type="paragraph" w:styleId="TOCHeading"><w:name w:val="TOC Heading"/>' +
    '<w:basedOn w:val="Heading1"/><w:next w:val="Normal"/><w:qFormat/>' +
    '<w:pPr><w:outlineLvl w:val="9"/></w:pPr></w:style>' +
    // TOC entry styles
    tocStyle('TOC1', 'toc 1', 0) +
    tocStyle('TOC2', 'toc 2', 220) +
    // List paragraph
    '<w:style w:type="paragraph" w:styleId="ListParagraph"><w:name w:val="List Paragraph"/>' +
    '<w:basedOn w:val="Normal"/><w:qFormat/><w:pPr><w:ind w:left="720"/></w:pPr></w:style>' +
    '</w:styles>'
  );
}

// ---------------------------------------------------------------------------
// document.xml
// ---------------------------------------------------------------------------
function buildDocumentXml(blocks) {
  const cover = buildCoverPage();
  const toc = buildTableOfContents();
  const body = buildBodyFromBlocks(blocks);

  const sectPr =
    '<w:sectPr>' +
    '<w:pgSz w:w="11906" w:h="16838"/>' +
    '<w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>' +
    '</w:sectPr>';

  return (
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" ' +
    'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">' +
    '<w:body>' +
    cover +
    toc +
    body +
    sectPr +
    '</w:body>' +
    '</w:document>'
  );
}

// ---------------------------------------------------------------------------
// Static package parts
// ---------------------------------------------------------------------------
const CONTENT_TYPES_XML =
  '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
  '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">' +
  '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>' +
  '<Default Extension="xml" ContentType="application/xml"/>' +
  '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>' +
  '<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>' +
  '<Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>' +
  '<Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>' +
  '<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>' +
  '<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>' +
  '</Types>';

const ROOT_RELS_XML =
  '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
  '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
  '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>' +
  '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>' +
  '<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>' +
  '</Relationships>';

const DOCUMENT_RELS_XML =
  '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
  '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
  '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>' +
  '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>' +
  '<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>' +
  '</Relationships>';

// updateFields=true tells Word to refresh the TOC (page numbers) when opened.
const SETTINGS_XML =
  '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
  '<w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">' +
  '<w:updateFields w:val="true"/>' +
  '</w:settings>';

function buildCoreXml() {
  const now = new Date().toISOString().replace(/\.\d{3}Z$/, 'Z');
  return (
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<cp:coreProperties ' +
    'xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" ' +
    'xmlns:dc="http://purl.org/dc/elements/1.1/" ' +
    'xmlns:dcterms="http://purl.org/dc/terms/" ' +
    'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">' +
    '<dc:title>NNM Advisory Website - Requirements Specification</dc:title>' +
    '<dc:creator>NNM Advisory</dc:creator>' +
    `<dcterms:created xsi:type="dcterms:W3CDTF">${now}</dcterms:created>` +
    `<dcterms:modified xsi:type="dcterms:W3CDTF">${now}</dcterms:modified>` +
    '</cp:coreProperties>'
  );
}

const APP_XML =
  '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
  '<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">' +
  '<Application>NNM Advisory docgen (Node.js)</Application>' +
  '</Properties>';

// ---------------------------------------------------------------------------
// Minimal ZIP writer (store + deflate), with correct CRC32 + local/central dirs
// ---------------------------------------------------------------------------
const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) {
      c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
    t[n] = c >>> 0;
  }
  return t;
})();

function crc32(buf) {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) {
    c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  }
  return (c ^ 0xffffffff) >>> 0;
}

function dosDateTime(d) {
  const time =
    (d.getHours() << 11) | (d.getMinutes() << 5) | (Math.floor(d.getSeconds() / 2));
  const date =
    ((d.getFullYear() - 1980) << 9) | ((d.getMonth() + 1) << 5) | d.getDate();
  return { time: time & 0xffff, date: date & 0xffff };
}

function buildZip(entries) {
  const localParts = [];
  const central = [];
  let offset = 0;
  const { time, date } = dosDateTime(new Date());

  for (const e of entries) {
    const nameBuf = Buffer.from(e.name, 'utf8');
    const content = Buffer.from(e.data, 'utf8');
    const crc = crc32(content);
    const deflated = zlib.deflateRawSync(content, { level: 9 });
    const useDeflate = deflated.length < content.length;
    const stored = useDeflate ? deflated : content;
    const method = useDeflate ? 8 : 0;

    // Local file header
    const lfh = Buffer.alloc(30);
    lfh.writeUInt32LE(0x04034b50, 0);
    lfh.writeUInt16LE(20, 4); // version needed
    lfh.writeUInt16LE(0, 6); // flags
    lfh.writeUInt16LE(method, 8);
    lfh.writeUInt16LE(time, 10);
    lfh.writeUInt16LE(date, 12);
    lfh.writeUInt32LE(crc, 14);
    lfh.writeUInt32LE(stored.length, 18);
    lfh.writeUInt32LE(content.length, 22);
    lfh.writeUInt16LE(nameBuf.length, 26);
    lfh.writeUInt16LE(0, 28); // extra len

    localParts.push(lfh, nameBuf, stored);

    // Central directory header
    const cdh = Buffer.alloc(46);
    cdh.writeUInt32LE(0x02014b50, 0);
    cdh.writeUInt16LE(20, 4); // version made by
    cdh.writeUInt16LE(20, 6); // version needed
    cdh.writeUInt16LE(0, 8); // flags
    cdh.writeUInt16LE(method, 10);
    cdh.writeUInt16LE(time, 12);
    cdh.writeUInt16LE(date, 14);
    cdh.writeUInt32LE(crc, 16);
    cdh.writeUInt32LE(stored.length, 20);
    cdh.writeUInt32LE(content.length, 24);
    cdh.writeUInt16LE(nameBuf.length, 28);
    cdh.writeUInt16LE(0, 30); // extra len
    cdh.writeUInt16LE(0, 32); // comment len
    cdh.writeUInt16LE(0, 34); // disk start
    cdh.writeUInt16LE(0, 36); // internal attrs
    cdh.writeUInt32LE(0, 38); // external attrs
    cdh.writeUInt32LE(offset, 42); // local header offset

    central.push(cdh, nameBuf);

    offset += lfh.length + nameBuf.length + stored.length;
  }

  const centralBuf = Buffer.concat(central);
  const localBuf = Buffer.concat(localParts);

  const eocd = Buffer.alloc(22);
  eocd.writeUInt32LE(0x06054b50, 0);
  eocd.writeUInt16LE(0, 4);
  eocd.writeUInt16LE(0, 6);
  eocd.writeUInt16LE(entries.length, 8);
  eocd.writeUInt16LE(entries.length, 10);
  eocd.writeUInt32LE(centralBuf.length, 12);
  eocd.writeUInt32LE(localBuf.length, 16);
  eocd.writeUInt16LE(0, 20);

  return Buffer.concat([localBuf, centralBuf, eocd]);
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------
function main() {
  const md = fs.readFileSync(sourceMd, 'utf8');
  const blocks = parseMarkdown(md);

  // Build body first so ordered-list numId instances are allocated before
  // numbering.xml is generated.
  const documentXml = buildDocumentXml(blocks);
  const numberingXml = buildNumberingXml();
  const stylesXml = buildStylesXml();

  const entries = [
    { name: '[Content_Types].xml', data: CONTENT_TYPES_XML },
    { name: '_rels/.rels', data: ROOT_RELS_XML },
    { name: 'word/document.xml', data: documentXml },
    { name: 'word/_rels/document.xml.rels', data: DOCUMENT_RELS_XML },
    { name: 'word/styles.xml', data: stylesXml },
    { name: 'word/numbering.xml', data: numberingXml },
    { name: 'word/settings.xml', data: SETTINGS_XML },
    { name: 'docProps/core.xml', data: buildCoreXml() },
    { name: 'docProps/app.xml', data: APP_XML },
  ];

  if (!fs.existsSync(outDir)) fs.mkdirSync(outDir, { recursive: true });
  const zip = buildZip(entries);
  fs.writeFileSync(outDocx, zip);

  const headings = blocks.filter((b) => b.type === 'heading');
  console.log(`Generated: ${outDocx}`);
  console.log(`  Size: ${zip.length} bytes`);
  console.log(`  Blocks parsed: ${blocks.length}`);
  console.log(`  Headings: ${headings.length}`);
  console.log(`  Ordered-list restart instances: ${orderedNumInstances.length}`);
}

main();
