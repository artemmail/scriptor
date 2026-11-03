export interface Fb2Chapter { id: string; title: string; depth: number; }
export interface Fb2Book {
  title: string;
  authors: string;
  language: string;
  html: string;
  markdown: string;
  toc: Fb2Chapter[];
}

const children = (element: Element, name: string) => Array.from(element.children).filter(e => e.localName === name);
const child = (element: Element | undefined, name: string) => element && children(element, name)[0];
const text = (element?: Element) => element?.textContent?.replace(/\s+/g, ' ').trim() || '';
// FB2 titles may contain several paragraphs. ATX headings must occupy one line.
const headingText = (element?: Element) => {
  if (!element) return '';
  const paragraphs = children(element, 'p');
  return paragraphs.length ? paragraphs.map(p => text(p)).filter(Boolean).join(' ') : text(element);
};
const escapeHtml = (value: string) => value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
// The document exporter accepts LaTeX delimiters \(...\) and \[...\].
// Literal book punctuation must use entities so prose is never interpreted as math.
const escapeMarkdown = (value: string) => value.replace(/&/g, '&amp;').replace(/[\\`*_{}\[\]()#+.!|<>~$-]/g,
  character => '()[]<>'.includes(character) ? `&#${character.charCodeAt(0)};` : '\\' + character);
const href = (element: Element) => element.getAttributeNS('http://www.w3.org/1999/xlink', 'href') || element.getAttribute('href') || '';

export function decodeFb2(bytes: ArrayBuffer): string {
  const data = new Uint8Array(bytes);
  let encoding = 'utf-8';
  if ((data[0] === 0xff && data[1] === 0xfe) || (data[0] === 0x3c && data[1] === 0)) encoding = 'utf-16le';
  else if ((data[0] === 0xfe && data[1] === 0xff) || (data[0] === 0 && data[1] === 0x3c)) encoding = 'utf-16be';
  else {
    const declaration = new TextDecoder('ascii').decode(data.slice(0, 512));
    encoding = declaration.match(/^\s*<\?xml[^>]*encoding\s*=\s*["']([^"']+)["']/i)?.[1] || 'utf-8';
  }
  try { return new TextDecoder(encoding, { fatal: true }).decode(data); }
  catch { throw new Error('Не удалось прочитать кодировку книги. Сохраните файл FB2 в UTF-8 и откройте снова.'); }
}

/** Render only known FictionBook elements. No source HTML, CSS or attributes are copied. */
export function parseFb2(xml: string, filename: string): Fb2Book {
  if (/<!DOCTYPE|<!ENTITY/i.test(xml)) throw new Error('Этот FB2 содержит неподдерживаемое объявление DTD. Откройте книгу без DTD.');
  const doc = new DOMParser().parseFromString(xml, 'application/xml');
  if (doc.getElementsByTagName('parsererror').length || doc.documentElement.localName !== 'FictionBook')
    throw new Error('Не удалось открыть книгу. Файл должен быть корректным документом FB2.');
  const root = doc.documentElement;
  const bodies = children(root, 'body');
  if (!bodies.length || !bodies.some(b => text(b))) throw new Error('В файле FB2 нет текста книги.');
  const all = Array.from(root.getElementsByTagName('*'));
  if (all.length > 150000) throw new Error('В книге слишком много элементов. Попробуйте открыть файл меньшего размера.');
  const info = child(child(root, 'description'), 'title-info');
  const title = text(child(info, 'book-title')) || filename.replace(/\.fb2$/i, '') || 'Книга';
  const authors = info ? children(info, 'author').map(a => ['first-name', 'middle-name', 'last-name'].map(n => text(child(a, n))).filter(Boolean).join(' ') || text(child(a, 'nickname'))).filter(Boolean).join(', ') : '';
  const language = text(child(info, 'lang'));
  const toc: Fb2Chapter[] = [];
  const ids = new Map<Element, string>();
  const sourceIds = new Map<string, string>();
  const noteTargets = new Set<string>();
  let sequence = 0;
  for (const element of all) {
    if (['section', 'body'].includes(element.localName) || element.hasAttribute('id')) {
      const id = `fb2-part-${++sequence}`;
      ids.set(element, id);
      const original = element.getAttribute('id');
      if (original && !sourceIds.has(original)) sourceIds.set(original, id);
    }
  }
  // Export anchors only for actual book links. Empty anchors for every wrapper
  // become blank blocks in visual Markdown editors.
  const linkedTargets = new Set<string>();
  for (const element of all.filter(e => e.localName === 'a')) {
    const source = href(element);
    const target = source.startsWith('#') ? sourceIds.get(source.slice(1)) : undefined;
    if (target) linkedTargets.add(target);
  }
  for (const body of bodies.filter(b => /^(notes|footnotes|comments)$/i.test(b.getAttribute('name') || ''))) {
    for (const element of Array.from(body.getElementsByTagName('*'))) {
      const id = ids.get(element);
      if (id) noteTargets.add(id);
    }
  }
  const images = new Map<string, string>();
  for (const binary of children(root, 'binary')) {
    const type = binary.getAttribute('content-type')?.toLowerCase();
    const id = binary.getAttribute('id');
    const data = (binary.textContent || '').replace(/\s/g, '');
    if (id && type && /^image\/(png|jpeg|gif|webp)$/.test(type) && data.length <= 12000000 && /^[A-Za-z0-9+/]+={0,2}$/.test(data))
      images.set(id, `data:${type};base64,${data}`);
  }
  type Rendered = { html: string; md: string };
  const renderChildren = (node: Element, depth: number, headingDepth: number): Rendered => {
    const parts = Array.from(node.childNodes).map(n => render(n, depth, headingDepth));
    return { html: parts.map(p => p.html).join(''), md: parts.map(p => p.md).join('') };
  };
  const render = (node: Node, depth: number, headingDepth = 0): Rendered => {
    if (depth > 100) throw new Error('Структура книги слишком сложная для открытия.');
    if (node.nodeType === Node.TEXT_NODE) {
      const value = (node.textContent || '').replace(/\s+/g, ' ');
      return { html: escapeHtml(value), md: escapeMarkdown(value) };
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return { html: '', md: '' };
    const el = node as Element;
    const name = el.localName;
    if (['script', 'style', 'stylesheet', 'binary', 'description'].includes(name)) return { html: '', md: '' };
    const id = ids.get(el);
    const attr = id ? ` id="${id}" tabindex="-1"` : '';
    const anchor = id && linkedTargets.has(id) ? `\n\n<a id="${id}"></a>\n\n` : '';
    if (name === 'image') {
      const source = href(el);
      const image = source.startsWith('#') ? images.get(source.slice(1)) : undefined;
      if (!image) return { html: '', md: '' };
      const alt = el.getAttribute('alt') || 'Иллюстрация';
      return { html: `<img${attr} src="${image}" alt="${escapeHtml(alt)}" loading="lazy">`, md: `${anchor}\n\n![${escapeMarkdown(alt)}](${image})\n\n` };
    }
    if (name === 'section' || name === 'body') {
      const titleElement = child(el, 'title');
      const titleLabel = headingText(titleElement);
      const label = titleLabel || (name === 'body' ? (el.getAttribute('name') === 'notes' || el.getAttribute('name') === 'footnotes' ? 'Примечания' : el.getAttribute('name') || '') : 'Раздел без названия');
      const hasHeading = !!(titleLabel || (name === 'body' && label));
      if (label && id) toc.push({ id, title: label, depth: headingDepth });
      // Only visible headings add a level; anonymous XML wrappers are not chapters.
      const contents = Array.from(el.children).filter(c => c !== titleElement).map(c => render(c, depth + 1, headingDepth + (hasHeading ? 1 : 0)));
      const level = Math.min(6, headingDepth + 2);
      const heading = hasHeading ? `<h${level}>${escapeHtml(label)}</h${level}>` : '';
      return { html: `<section${attr}>${heading}${contents.map(p => p.html).join('')}</section>`, md: `${anchor}${heading ? '#'.repeat(level) + ' ' + escapeMarkdown(label) + '\n\n' : ''}${contents.map(p => p.md).join('')}\n\n` };
    }
    if (name === 'table') {
      const rows = children(el, 'tr').map(row => Array.from(row.children).filter(c => ['td', 'th'].includes(c.localName)).map(c => render(c, depth + 1, headingDepth)));
      const width = Math.max(0, ...rows.map(row => row.length));
      const mdRows = rows.map(row => '| ' + Array.from({ length: width }, (_, i) => (row[i]?.md || '').trim().replace(/\n/g, ' ')).join(' | ') + ' |');
      if (mdRows.length) mdRows.splice(1, 0, '| ' + Array(width).fill('---').join(' | ') + ' |');
      return { html: `<div class="fb-table"><table${attr}>${rows.map(row => '<tr>' + row.map(cell => cell.html).join('') + '</tr>').join('')}</table></div>`, md: `${anchor}\n\n${mdRows.join('\n')}\n\n` };
    }
    if (name === 'title' || name === 'subtitle') {
      const label = headingText(el);
      const level = Math.min(6, headingDepth + 2);
      return { html: `<h${level}${attr}>${escapeHtml(label)}</h${level}>`, md: `${anchor}\n\n${'#'.repeat(level)} ${escapeMarkdown(label)}\n\n` };
    }
    const inner = renderChildren(el, depth + 1, headingDepth);
    const block = (tag: string, markdown = inner.md.trim()) => ({ html: `<${tag}${attr}>${inner.html}</${tag}>`, md: `${anchor}\n\n${markdown}\n\n` });
    switch (name) {
      case 'p': return block('p');
      case 'strong': return { html: `<strong>${inner.html}</strong>`, md: `**${inner.md}**` };
      case 'emphasis': return { html: `<em>${inner.html}</em>`, md: `*${inner.md}*` };
      case 'strikethrough': return { html: `<s>${inner.html}</s>`, md: `~~${inner.md}~~` };
      case 'sup': case 'sub': return { html: `<${name}>${inner.html}</${name}>`, md: `<${name}>${inner.md}</${name}>` };
      case 'code': return { html: `<code>${escapeHtml(el.textContent || '')}</code>`, md: '`' + (el.textContent || '').replace(/`/g, "'") + '`' };
      case 'empty-line': return { html: '<br>', md: '\n\n' };
      case 'v': return { html: `<p class="fb-verse"${attr}>${inner.html}</p>`, md: `${inner.md.trim()}  \n` };
      case 'poem': case 'stanza': return block('div');
      case 'annotation': case 'cite': case 'epigraph': return block('blockquote', inner.md.trim().split('\n').map(line => '> ' + line).join('\n'));
      case 'text-author': return block('p', `*${inner.md.trim()}*`);
      case 'a': {
        const source = href(el);
        const local = source.startsWith('#') ? sourceIds.get(source.slice(1)) : undefined;
        const target = local ? '#' + local : /^(https?:\/\/|mailto:)/i.test(source) ? source : '';
        if (!target) return inner;
        const note = local && (el.getAttribute('type') === 'note' || noteTargets.has(local));
        return { html: `<a href="${escapeHtml(target)}"${note ? ' data-fb2-note="true"' : ''}${local ? '' : ' target="_blank" rel="noopener noreferrer"'}>${inner.html}</a>`, md: `[${inner.md}](<${target.replace(/[<>\s\\]/g, c => encodeURIComponent(c))}>)` };
      }
      case 'tr': return { html: `<tr>${inner.html}</tr>`, md: inner.md };
      case 'td': case 'th': return { html: `<${name}>${inner.html}</${name}>`, md: inner.md };
      default: return { html: id ? `<span${attr}>${inner.html}</span>` : inner.html, md: anchor + inner.md };
    }
  };
  const cover = child(child(info, 'coverpage'), 'image');
  const coverPart = cover ? render(cover, 0) : { html: '', md: '' };
  const annotation = child(info, 'annotation');
  const annotationPart = annotation ? render(annotation, 0) : { html: '', md: '' };
  const parts = bodies.map(body => render(body, 0));
  return {
    title, authors, language, toc,
    html: `<header class="fb-book-heading">${coverPart.html}<h1>${escapeHtml(title)}</h1>${authors ? `<p>${escapeHtml(authors)}</p>` : ''}</header>${annotationPart.html}${parts.map(p => p.html).join('')}`,
    markdown: `# ${escapeMarkdown(title)}\n\n${authors ? escapeMarkdown(authors) + '\n\n' : ''}${coverPart.md}${annotationPart.md}${parts.map(p => p.md).join('')}`.replace(/\n{3,}/g, '\n\n').trim() + '\n'
  };
}
