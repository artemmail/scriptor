import { afterNextRender, Component, DestroyRef, ElementRef, HostListener, Injector, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { DomSanitizer, SafeHtml, Title } from '@angular/platform-browser';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { SubtitleService } from '../services/subtitle.service';
import { decodeFb2, Fb2Book, parseFb2 } from './fb2-parser';
import { ReaderFileTransferService } from '../shared/reader-file-transfer.service';

interface ReadingPosition { index: number; offset: number; }
const POSITION_PREFIX = 'fb2-reader:position:';
const READING_LINE = 120;
const CHAPTER_POSITION_TOLERANCE = 4;

@Component({
  selector: 'app-fb2-reader', standalone: true,
  imports: [CommonModule, RouterModule, MatIconModule, MatMenuModule],
  templateUrl: './fb2-reader.component.html',
  styleUrls: ['../shared/account-page.css', './fb2-reader.component.css']
})
export class Fb2ReaderComponent implements OnInit {
  @ViewChild('reader') reader?: ElementRef<HTMLElement>;
  @ViewChild('noteHint') noteHint?: ElementRef<HTMLElement>;
  book: Fb2Book | null = null;
  content: SafeHtml = '';
  filename = '';
  loading = false;
  exporting = '';
  error = '';
  notice = '';
  activeChapter = '';
  currentChapterTitle = 'Начало книги';
  readingPercent = 0;
  contentsOpen = false;
  fontSize = 19;
  dragging = false;
  noteText = '';
  noteLeft = 0;
  noteTop = 0;
  private dragDepth = 0;
  private noteLink: HTMLElement | null = null;
  private noteTimer?: ReturnType<typeof setTimeout>;
  private saveTimer?: ReturnType<typeof setTimeout>;
  private scrollFrame = 0;
  private renderVersion = 0;
  private restoringPosition = false;
  private readingBlocks: HTMLElement[] = [];
  private chapterElements: HTMLElement[] = [];
  private currentPosition: ReadingPosition | null = null;
  private destroyed = false;

  constructor(private sanitizer: DomSanitizer, private subtitles: SubtitleService,
    private destroyRef: DestroyRef, private title: Title, private readerFiles: ReaderFileTransferService,
    private injector: Injector) {
    title.setTitle('FB2 Reader — читать и конвертировать книги | YouScriptor');
    destroyRef.onDestroy(() => {
      this.flushPosition();
      this.destroyed = true;
      cancelAnimationFrame(this.scrollFrame);
      this.hideNote();
    });
  }

  get busy(): boolean { return this.loading || !!this.exporting; }

  ngOnInit(): void {
    const file = this.readerFiles.takeFor('fb2');
    if (file) void this.loadFile(file);
  }

  async openFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || this.busy) return;
    await this.loadFile(file);
  }

  private async loadFile(file: File): Promise<void> {
    this.hideNote();
    this.error = ''; this.notice = '';
    if (this.readerFiles.redirectIfNeeded(file, 'fb2')) return;
    if (!/\.fb2$/i.test(file.name)) { this.error = 'Выберите файл .fb2. Если книга в ZIP-архиве, сначала распакуйте его.'; return; }
    if (file.size > 25 * 1024 * 1024) { this.error = 'Размер книги превышает 25 МБ. Выберите файл меньшего размера.'; return; }
    this.loading = true;
    try {
      const bytes = await file.arrayBuffer();
      if (this.destroyed) return;
      const book = parseFb2(decodeFb2(bytes), file.name);
      this.flushPosition();
      clearTimeout(this.saveTimer);
      cancelAnimationFrame(this.scrollFrame);
      this.scrollFrame = 0;
      this.currentPosition = null;
      this.restoringPosition = true;
      this.readingBlocks = [];
      this.chapterElements = [];
      // Parser builds HTML from an explicit element/attribute allowlist, never from source markup.
      this.content = this.sanitizer.bypassSecurityTrustHtml(book.html);
      this.book = book;
      this.filename = file.name;
      this.activeChapter = '';
      this.currentChapterTitle = 'Начало книги';
      this.readingPercent = 0;
      this.contentsOpen = false;
      this.title.setTitle(`${book.title} — FB2 Reader | YouScriptor`);
      const version = ++this.renderVersion;
      afterNextRender(() => this.restorePosition(version), { injector: this.injector });
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'Не удалось прочитать книгу. Попробуйте другой файл FB2.';
    } finally { this.loading = false; }
  }

  @HostListener('document:dragenter', ['$event'])
  onDragEnter(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes('Files')) return;
    event.preventDefault();
    this.dragDepth++;
    this.dragging = !this.busy;
    this.hideNote();
  }

  @HostListener('document:dragover', ['$event'])
  onDragOver(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes('Files')) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = this.busy ? 'none' : 'copy';
    this.dragging = !this.busy;
  }

  @HostListener('document:dragleave', ['$event'])
  onDragLeave(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes('Files')) return;
    this.dragDepth = Math.max(0, this.dragDepth - 1);
    if (!this.dragDepth || !event.relatedTarget) this.resetDrag();
  }

  @HostListener('document:drop', ['$event'])
  onDrop(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes('Files') && !event.dataTransfer?.files.length) return;
    event.preventDefault();
    this.resetDrag();
    if (this.busy) return;
    const files = event.dataTransfer?.files;
    if (!files || files.length !== 1) {
      this.error = 'Перетащите одну книгу в формате FB2.';
      this.notice = '';
      return;
    }
    void this.loadFile(files[0]);
  }

  @HostListener('document:dragend')
  resetDrag(): void { this.dragging = false; this.dragDepth = 0; }

  @HostListener('window:blur')
  onWindowBlur(): void { this.resetDrag(); this.hideNote(); }

  @HostListener('window:scroll')
  @HostListener('window:resize')
  onReaderScroll(): void {
    this.hideNote();
    if (!this.book || this.restoringPosition || this.scrollFrame) return;
    this.scrollFrame = requestAnimationFrame(() => {
      this.scrollFrame = 0;
      this.updateReadingPosition();
    });
  }

  @HostListener('window:pagehide')
  onPageHide(): void { this.flushPosition(); }

  private restorePosition(version: number): void {
    if (this.destroyed || version !== this.renderVersion || !this.reader || !this.book) return;
    const article = this.reader.nativeElement;
    this.readingBlocks = Array.from(article.querySelectorAll<HTMLElement>('p,h1,h2,h3,h4,h5,h6,img,table'));
    this.chapterElements = this.book.toc.map(chapter => article.querySelector<HTMLElement>(`#${chapter.id}`))
      .filter((element): element is HTMLElement => !!element);
    const position = this.readStoredPosition();
    const block = position && this.readingBlocks[position.index];
    if (block) {
      const top = window.scrollY + block.getBoundingClientRect().top + position.offset - this.readingLine;
      window.scrollTo({ top, behavior: 'instant' });
    } else {
      window.scrollTo({ top: 0, behavior: 'instant' });
    }
    this.restoringPosition = false;
    this.updateReadingPosition();
  }

  private get readingLine(): number { return READING_LINE; }

  private updateReadingPosition(): void {
    if (!this.book || !this.reader || this.restoringPosition) return;
    const article = this.reader.nativeElement;
    const rect = article.getBoundingClientRect();
    const line = this.readingLine;
    const total = Math.max(1, rect.height - window.innerHeight + line);
    this.readingPercent = rect.height <= window.innerHeight ? 100
      : Math.round(Math.max(0, Math.min(1, (line - rect.top) / total)) * 100);
    // On the final screen the viewport cannot scroll far enough to place the last chapter at the top.
    // Section links use a 120px scroll margin; allow for fractional pixel rounding.
    const chapterLine = (this.readingPercent === 100 ? Math.max(line, window.innerHeight * 0.4) : line)
      + CHAPTER_POSITION_TOLERANCE;
    const chapterIndex = this.lastElementBefore(this.chapterElements, chapterLine);
    this.activeChapter = rect.top > line || chapterIndex < 0 ? '' : this.book.toc[chapterIndex]?.id || '';
    this.currentChapterTitle = this.activeChapter ? this.book.toc[chapterIndex].title : 'Начало книги';
    const blockIndex = this.lastElementBefore(this.readingBlocks, line);
    if (blockIndex >= 0 && rect.top <= line) {
      this.currentPosition = { index: blockIndex, offset: Math.max(0, Math.round(line - this.readingBlocks[blockIndex].getBoundingClientRect().top)) };
    } else this.currentPosition = { index: -1, offset: 0 };
    clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => this.flushPosition(), 300);
  }

  private lastElementBefore(elements: HTMLElement[], line: number): number {
    let low = 0, high = elements.length;
    while (low < high) {
      const middle = (low + high) >> 1;
      if (elements[middle].getBoundingClientRect().top <= line) low = middle + 1;
      else high = middle;
    }
    return low - 1;
  }

  private readStoredPosition(): ReadingPosition | null {
    try {
      const value = JSON.parse(window.localStorage.getItem(POSITION_PREFIX + this.filename) || 'null');
      return value && Number.isInteger(value.index) && value.index >= -1 && Number.isFinite(value.offset) && value.offset >= 0
        ? value as ReadingPosition : null;
    } catch { return null; }
  }

  private flushPosition(): void {
    clearTimeout(this.saveTimer);
    if (!this.filename || !this.currentPosition) return;
    try { window.localStorage.setItem(POSITION_PREFIX + this.filename, JSON.stringify(this.currentPosition)); }
    catch { /* Storage may be unavailable or full; reading still works. */ }
  }

  showNote(event: MouseEvent | FocusEvent): void {
    const link = (event.target as Element).closest<HTMLElement>('a[data-fb2-note]');
    const id = link?.getAttribute('href')?.slice(1);
    if (!link || !id || !/^fb2-part-\d+$/.test(id)) return;
    this.keepNoteOpen();
    if (link === this.noteLink) return;
    const target = this.reader?.nativeElement.querySelector<HTMLElement>(`#${id}`);
    if (!target) return;
    // Some books put the note ID on its heading, followed by paragraphs in the same section.
    let note = target.innerText.trim();
    if (/^H[1-6]$/.test(target.tagName)) {
      for (let sibling = target.nextElementSibling; sibling && !/^H[1-6]$/.test(sibling.tagName) && sibling.tagName !== 'SECTION'; sibling = sibling.nextElementSibling)
        note += '\n\n' + (sibling as HTMLElement).innerText.trim();
    }
    if (!note) return;
    this.hideNote();
    this.noteLink = link;
    this.noteText = note;
    link.setAttribute('aria-describedby', 'fb-note-hint');
    const rect = link.getBoundingClientRect();
    this.noteLeft = Math.max(12, Math.min(rect.left, window.innerWidth - 372));
    this.noteTop = Math.min(rect.bottom + 10, window.innerHeight - 120);
    requestAnimationFrame(() => {
      if (this.destroyed || this.noteLink !== link || !this.noteHint) return;
      const hint = this.noteHint.nativeElement.getBoundingClientRect();
      this.noteLeft = Math.max(12, Math.min(rect.left, window.innerWidth - hint.width - 12));
      const below = rect.bottom + 10;
      this.noteTop = below + hint.height <= window.innerHeight - 12
        ? below : Math.max(12, rect.top - hint.height - 10);
    });
  }

  onNoteLeave(event: MouseEvent | FocusEvent): void {
    const next = event.relatedTarget as Node | null;
    if (next && (this.noteLink?.contains(next) || this.noteHint?.nativeElement.contains(next))) return;
    if (event.type === 'mouseout' && this.noteLink === document.activeElement) return;
    this.scheduleNoteClose();
  }

  keepNoteOpen(): void { clearTimeout(this.noteTimer); }
  scheduleNoteClose(): void {
    this.keepNoteOpen();
    this.noteTimer = setTimeout(() => this.hideNote(), 150);
  }

  @HostListener('document:keydown.escape')
  hideNote(): void {
    this.keepNoteOpen();
    this.noteLink?.removeAttribute('aria-describedby');
    this.noteLink = null;
    this.noteText = '';
  }

  navigate(id: string, event?: Event): void {
    event?.preventDefault();
    this.hideNote();
    const target = this.reader?.nativeElement.querySelector<HTMLElement>(`#${id}`);
    if (!target) return;
    this.contentsOpen = false;
    this.scrollTo(target);
  }

  goToStart(event: Event): void {
    event.preventDefault();
    this.contentsOpen = false;
    if (this.reader) this.scrollTo(this.reader.nativeElement);
  }

  private scrollTo(target: HTMLElement): void {
    // Wait for the mobile contents panel to collapse before measuring the target.
    requestAnimationFrame(() => {
      if (this.destroyed || !target.isConnected) return;
      target.focus({ preventScroll: true });
      target.scrollIntoView({ behavior: 'instant', block: 'start' });
      this.updateReadingPosition();
    });
  }

  onBookClick(event: MouseEvent): void {
    const link = (event.target as Element).closest('a');
    const href = link?.getAttribute('href');
    if (href && /^#fb2-part-\d+$/.test(href)) this.navigate(href.slice(1), event);
  }

  changeFont(delta: number): void {
    this.fontSize = Math.max(16, Math.min(26, this.fontSize + delta));
    requestAnimationFrame(() => this.updateReadingPosition());
  }

  export(format: 'md' | 'pdf' | 'docx'): void {
    if (!this.book || this.busy) return;
    this.error = ''; this.notice = '';
    const name = (this.book.title.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '-').replace(/[. ]+$/g, '').slice(0, 100) || 'Книга') + '.' + format;
    if (format === 'md') {
      this.save(new Blob([this.book.markdown], { type: 'text/markdown;charset=utf-8' }), name);
      return;
    }
    this.exporting = format;
    // Pandoc native anchors survive PDF and DOCX conversion; the .md file uses HTML anchors.
    const markdown = this.book.markdown.replace(/<a id="(fb2-part-\d+)"><\/a>/g, '[]{#$1}');
    const request = format === 'pdf'
      ? this.subtitles.generatePdfFromMarkdown('fb2-book', markdown)
      : this.subtitles.generateWordFromMarkdown('fb2-book', markdown);
    request.pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.exporting = '')).subscribe({
      next: blob => this.save(blob, name),
      error: () => this.error = 'Не удалось подготовить документ. Книга остаётся открытой — попробуйте экспорт ещё раз или скачайте Markdown.'
    });
  }

  private save(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url; link.download = filename;
    document.body.appendChild(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    this.notice = 'Файл готов и передан браузеру для скачивания.';
  }
}
