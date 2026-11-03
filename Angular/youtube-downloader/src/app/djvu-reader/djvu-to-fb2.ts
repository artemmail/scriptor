export interface DjvuOutlineEntry { title: string; page: number; depth: number; }
export interface ConversionChapter extends DjvuOutlineEntry { fragments: string[]; }
export interface ConversionBlock { chapterIndex: number; text: string; }
export interface ConversionPlan { chapters: ConversionChapter[]; blocks: ConversionBlock[]; textLength: number; }

const escapeXml = (value: string) => value.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/g, ' ').replace(/&/g, '&amp;').replace(/</g, '&lt;')
  .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&apos;');

function headingPosition(text: string, heading: string, from: number): { start: number; end: number } | undefined {
  const clean = (value: string) => value.toLocaleLowerCase().replace(/ё/g, 'е').replace(/[^\p{L}\p{N}]/gu, '');
  const needle = clean(heading);
  if (needle.length < 4) return undefined;
  let normalized = '';
  const offsets: number[] = [];
  for (let i = from; i < text.length; i++) {
    const character = clean(text[i]);
    if (character) { normalized += character; offsets.push(i); }
  }
  const at = normalized.indexOf(needle);
  return at < 0 ? undefined : { start: offsets[at], end: offsets[at + needle.length - 1] + 1 };
}

function splitText(text: string, max = 6000): string[] {
  const blocks: string[] = [];
  let remaining = text.trim();
  while (remaining.length > max) {
    const slice = remaining.slice(0, max);
    let cut = Math.max(slice.lastIndexOf('\n', max), slice.lastIndexOf(' ', max));
    if (cut < max / 2) cut = max;
    blocks.push(remaining.slice(0, cut).trim());
    remaining = remaining.slice(cut).trim();
  }
  if (remaining) blocks.push(remaining);
  return blocks;
}

export function planDjvuConversion(pages: string[], outline: DjvuOutlineEntry[]): ConversionPlan {
  const entries = outline.filter(ch => Number.isInteger(ch.page) && ch.page >= 1 && ch.page <= pages.length &&
    ch.title.trim() && !/^p\d{3,}\.djvu?$/i.test(ch.title.trim()));
  const chapters: ConversionChapter[] = entries.map(ch => ({ ...ch, title: ch.title.trim(), fragments: [] }));
  const parts: string[][] = chapters.map(() => []);
  if (!chapters.length) { chapters.push({ title: 'Текст книги', page: 1, depth: 0, fragments: [] }); parts.push([]); }
  if (chapters[0].page > 1) {
    chapters.unshift({ title: 'До первой главы', page: 1, depth: 0, fragments: [] });
    parts.unshift([]);
  }

  const starts = new Map<number, number[]>();
  chapters.forEach((chapter, index) => {
    const at = starts.get(chapter.page) || [];
    at.push(index);
    starts.set(chapter.page, at);
  });
  let current = 0;
  let textLength = 0;
  for (let page = 1; page <= pages.length; page++) {
    const text = (pages[page - 1] || '').replace(/\r\n?/g, '\n').replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/g, ' ').trim();
    if (!text) {
      for (const chapterIndex of starts.get(page) || []) current = chapterIndex;
      continue;
    }
    textLength += text.length;
    let cursor = 0;
    for (const chapterIndex of starts.get(page) || []) {
      const found = headingPosition(text, chapters[chapterIndex].title, cursor);
      const boundary = found?.start ?? cursor;
      const before = text.slice(cursor, boundary).trim();
      if (before) parts[current].push(`[Страница ${page}]\n${before}`);
      current = chapterIndex;
      cursor = found?.end ?? cursor;
    }
    const rest = text.slice(cursor).trim();
    if (rest) parts[current].push(`[Страница ${page}]\n${rest}`);
  }

  const blocks: ConversionBlock[] = [];
  parts.forEach((pageParts, chapterIndex) => {
    let pending = '';
    for (const part of pageParts) {
      if (pending && pending.length + part.length + 2 > 6000) {
        for (const text of splitText(pending)) blocks.push({ chapterIndex, text });
        pending = '';
      }
      pending += (pending ? '\n\n' : '') + part;
    }
    if (pending) for (const text of splitText(pending)) blocks.push({ chapterIndex, text });
  });
  return { chapters, blocks, textLength };
}

export function buildFb2FromDjvu(title: string, chapters: ConversionChapter[]): string {
  type Section = { chapter: ConversionChapter; children: Section[] };
  const roots: Section[] = [];
  const stack: Section[] = [];
  for (const chapter of chapters) {
    const section = { chapter, children: [] as Section[] };
    while (stack.length && stack[stack.length - 1].chapter.depth >= chapter.depth) stack.pop();
    if (stack.length) stack[stack.length - 1].children.push(section);
    else roots.push(section);
    stack.push(section);
  }
  const render = (section: Section): string =>
    `<section><title><p>${escapeXml(section.chapter.title)}</p></title>` +
    section.chapter.fragments.join('') + section.children.map(render).join('') + '</section>';
  const body = roots.map(render).join('');
  const hasCyrillic = /[А-Яа-яЁё]/.test(title + chapters.map(ch => ch.title + ch.fragments.join('').slice(0, 200)).join(' '));
  const xml = `<?xml version="1.0" encoding="utf-8"?>\n` +
    `<FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0">` +
    `<description><title-info><genre>other</genre><book-title>${escapeXml(title)}</book-title>` +
    `<lang>${hasCyrillic ? 'ru' : 'en'}</lang></title-info></description><body>${body}</body></FictionBook>`;
  const document = new DOMParser().parseFromString(xml, 'application/xml');
  if (document.getElementsByTagName('parsererror').length) throw new Error('Не удалось собрать корректный FB2. Повторите обработку последнего блока.');
  return xml;
}
