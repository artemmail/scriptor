import { Component, DestroyRef, ElementRef, HostListener, OnInit, ViewChild } from '@angular/core';
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
  contentsOpen = false;
  fontSize = 19;
  dragging = false;
  noteText = '';
  noteLeft = 0;
  noteTop = 0;
  private dragDepth = 0;
  private noteLink: HTMLElement | null = null;
  private noteTimer?: ReturnType<typeof setTimeout>;
  private destroyed = false;

  constructor(private sanitizer: DomSanitizer, private subtitles: SubtitleService,
    private destroyRef: DestroyRef, private title: Title, private readerFiles: ReaderFileTransferService) {
    title.setTitle('FB2 Reader — читать и конвертировать книги | YouScriptor');
    destroyRef.onDestroy(() => { this.destroyed = true; this.hideNote(); });
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
      // Parser builds HTML from an explicit element/attribute allowlist, never from source markup.
      this.content = this.sanitizer.bypassSecurityTrustHtml(book.html);
      this.book = book;
      this.filename = file.name;
      this.activeChapter = '';
      this.contentsOpen = false;
      this.title.setTitle(`${book.title} — FB2 Reader | YouScriptor`);
      window.scrollTo({ top: 0, behavior: 'instant' });
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
  @HostListener('window:scroll')
  @HostListener('window:resize')
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
    this.activeChapter = id;
    this.contentsOpen = false;
    this.scrollTo(target);
  }

  goToStart(event: Event): void {
    event.preventDefault();
    this.activeChapter = '';
    this.contentsOpen = false;
    if (this.reader) this.scrollTo(this.reader.nativeElement);
  }

  private scrollTo(target: HTMLElement): void {
    // Wait for the mobile contents panel to collapse before measuring the target.
    requestAnimationFrame(() => {
      if (this.destroyed || !target.isConnected) return;
      target.focus({ preventScroll: true });
      target.scrollIntoView({ behavior: 'instant', block: 'start' });
    });
  }

  onBookClick(event: MouseEvent): void {
    const link = (event.target as Element).closest('a');
    const href = link?.getAttribute('href');
    if (href && /^#fb2-part-\d+$/.test(href)) this.navigate(href.slice(1), event);
  }

  changeFont(delta: number): void { this.fontSize = Math.max(16, Math.min(26, this.fontSize + delta)); }

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
