import { CommonModule } from '@angular/common';
import { Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { Title } from '@angular/platform-browser';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom, Subject, takeUntil } from 'rxjs';
import { ReaderFileTransferService } from '../shared/reader-file-transfer.service';
import { AuthService } from '../services/AuthService.service';
import { buildFb2FromDjvu, ConversionPlan, planDjvuConversion } from './djvu-to-fb2';

export interface DjvuBookmark { description: string; url: string; children?: DjvuBookmark[]; }
interface DjvuChapter { title: string; page: number; depth: number; }
interface DjvuPng { url: string; width: number; height: number; dpi: number; }
interface DjvuTask<T> {
  run(): Promise<T>;
  getPagesQuantity(): DjvuTask<number>;
  isBundled(): DjvuTask<boolean>;
  getContents(): DjvuTask<DjvuBookmark[]>;
  getPageNumberByUrl(url: string): DjvuTask<number | null>;
  getPage(page: number): DjvuTask<unknown>;
  getText(): DjvuTask<string>;
  createPngObjectUrl(): DjvuTask<DjvuPng>;
}
export interface DjvuWorker {
  doc: DjvuTask<unknown>;
  createDocument(buffer: ArrayBuffer): Promise<void>;
  terminate(): void;
  revokeObjectURL(url: string): void;
}
interface DjvuLibrary { Worker: new (scriptUrl?: string) => DjvuWorker; }
declare global { interface Window { DjVu?: DjvuLibrary; } }

const libraryUrl = '/assets/vendor/djvu/djvu.js';
let libraryPromise: Promise<DjvuLibrary> | undefined;
export function loadLibrary(): Promise<DjvuLibrary> {
  if (window.DjVu) return Promise.resolve(window.DjVu);
  if (!libraryPromise) {
    libraryPromise = new Promise<DjvuLibrary>((resolve, reject) => {
      const script = document.createElement('script');
      script.src = libraryUrl;
      script.async = true;
      script.onload = () => window.DjVu ? resolve(window.DjVu) : reject(new Error('DjVu.js не загрузился.'));
      script.onerror = () => { script.remove(); reject(new Error('Не удалось загрузить модуль DjVu. Обновите страницу.')); };
      document.head.appendChild(script);
    }).catch(error => { libraryPromise = undefined; throw error; });
  }
  return libraryPromise!;
}

@Component({
  selector: 'app-djvu-reader', standalone: true,
  imports: [CommonModule, RouterModule, MatIconModule],
  templateUrl: './djvu-reader.component.html',
  styleUrls: ['../shared/account-page.css', '../fb2-reader/fb2-reader.component.css', './djvu-reader.component.css']
})
export class DjvuReaderComponent implements OnInit, OnDestroy {
  filename = '';
  pageCount = 0;
  pageNumber = 1;
  chapters: DjvuChapter[] = [];
  pageText = '';
  imageUrl = '';
  imageWidth = 0;
  imageHeight = 0;
  mode: 'page' | 'text' = 'page';
  loading = false;
  pageLoading = false;
  dragging = false;
  contentsOpen = false;
  error = '';
  notice = '';
  conversionStage: 'idle' | 'extracting' | 'processing' | 'error' | 'done' = 'idle';
  conversionDone = 0;
  conversionTotal = 0;
  conversionError = '';
  convertedFile?: File;
  private sourceFile?: File;
  private worker?: DjvuWorker;
  private dragDepth = 0;
  private generation = 0;
  private pageRequest = 0;
  private conversionPlan?: ConversionPlan;
  private conversionIndex = 0;
  private readonly cancelled = new Subject<void>();

  constructor(title: Title, private readerFiles: ReaderFileTransferService,
    private http: HttpClient, private auth: AuthService) { title.setTitle('DjVu Reader — читать книги онлайн | YouScriptor'); }

  ngOnInit(): void {
    const file = this.readerFiles.takeFor('djvu');
    if (file) void this.loadFile(file);
  }

  get hasBook(): boolean { return this.pageCount > 0; }
  get busy(): boolean { return this.loading || this.pageLoading; }
  get converting(): boolean { return this.conversionStage === 'extracting' || this.conversionStage === 'processing'; }
  get canResumeConversion(): boolean { return !!this.conversionPlan; }

  async openFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) await this.loadFile(file);
  }

  async loadFile(file: File): Promise<void> {
    if (this.loading || this.converting) return;
    this.error = ''; this.notice = '';
    if (this.readerFiles.redirectIfNeeded(file, 'djvu')) return;
    if (!/\.djvu?$/i.test(file.name)) { this.error = 'Выберите файл .djvu или .djv.'; return; }
    if (file.size > 100 * 1024 * 1024) { this.error = 'Размер файла превышает 100 МБ.'; return; }
    this.loading = true;
    const generation = ++this.generation;
    let candidate: DjvuWorker | undefined;
    try {
      const buffer = await file.arrayBuffer();
      const magic = new TextDecoder('ascii').decode(buffer.slice(0, 8));
      if (magic !== 'AT&TFORM') throw new Error('Это не файл DjVu. Проверьте формат книги.');
      const library = await loadLibrary();
      candidate = new library.Worker(libraryUrl);
      await candidate.createDocument(buffer);
      if (!await candidate.doc.isBundled().run())
        throw new Error('Этот DjVu состоит из нескольких отдельных файлов. Откройте версию, собранную в один файл.');
      const count = await candidate.doc.getPagesQuantity().run();
      if (!Number.isInteger(count) || count < 1 || count > 20000) throw new Error('В книге нет доступных страниц.');
      const entries = await candidate.doc.getContents().run();
      const chapters = await this.readContents(candidate, entries, count);
      if (generation !== this.generation) return;
      this.releaseImage();
      this.worker?.terminate();
      this.worker = candidate;
      candidate = undefined;
      this.filename = file.name;
      this.sourceFile = file;
      this.pageCount = count;
      this.chapters = chapters;
      this.pageNumber = 1;
      this.pageText = '';
      this.mode = 'page';
      this.contentsOpen = false;
      this.conversionPlan = undefined;
      this.conversionIndex = 0;
      this.conversionStage = 'idle';
      this.conversionError = '';
      this.convertedFile = undefined;
      await this.showPage(1);
    } catch (error) {
      if (generation === this.generation)
        this.error = error instanceof Error ? error.message : 'Не удалось прочитать DjVu. Попробуйте другой файл.';
    } finally {
      candidate?.terminate();
      if (generation === this.generation) this.loading = false;
    }
  }

  private async readContents(worker: DjvuWorker, entries: DjvuBookmark[], count: number): Promise<DjvuChapter[]> {
    const result: DjvuChapter[] = [];
    const visit = async (items: DjvuBookmark[], depth: number): Promise<void> => {
      if (!Array.isArray(items) || depth > 10) return;
      for (const item of items) {
        if (result.length >= 1000) return;
        if (item && typeof item.url === 'string' && typeof item.description === 'string') {
          const page = await worker.doc.getPageNumberByUrl(item.url).run();
          if (page !== null && Number.isInteger(page) && page >= 1 && page <= count)
            result.push({ title: item.description.trim() || `Страница ${page}`, page, depth });
        }
        if (item?.children) await visit(item.children, depth + 1);
      }
    };
    await visit(entries, 0);
    return result;
  }

  async showPage(page: number): Promise<void> {
    if (!this.worker || !Number.isInteger(page) || page < 1 || page > this.pageCount) return;
    const worker = this.worker;
    const request = ++this.pageRequest;
    this.pageNumber = page;
    this.pageText = '';
    this.releaseImage();
    this.error = '';
    this.pageLoading = true;
    this.contentsOpen = false;
    try {
      let text = '';
      try { text = await worker.doc.getPage(page).getText().run(); }
      catch { /* Some DjVu pages have no decodable text layer; keep the image readable. */ }
      if (request !== this.pageRequest || worker !== this.worker) return;
      this.pageText = (text || '').replace(/[\u000b\u001d\u001e\u001f]/g, '\n').trim();
      if (this.mode === 'page') await this.loadImage(worker, page, request);
    } catch (error) {
      if (request === this.pageRequest) this.error = error instanceof Error ? error.message : 'Не удалось открыть страницу.';
    } finally {
      if (request === this.pageRequest) this.pageLoading = false;
    }
  }

  private async loadImage(worker: DjvuWorker, page: number, request: number): Promise<void> {
    const image = await worker.doc.getPage(page).createPngObjectUrl().run();
    if (request !== this.pageRequest || worker !== this.worker || this.mode !== 'page') {
      worker.revokeObjectURL(image.url);
      return;
    }
    this.imageUrl = image.url;
    this.imageWidth = image.width;
    this.imageHeight = image.height;
  }

  async setMode(mode: 'page' | 'text'): Promise<void> {
    if (mode === this.mode || !this.worker) return;
    this.mode = mode;
    this.error = '';
    if (this.pageLoading && !this.pageText) {
      await this.showPage(this.pageNumber);
      return;
    }
    if (mode === 'text') {
      ++this.pageRequest;
      this.releaseImage();
      this.pageLoading = false;
    } else {
      this.pageLoading = true;
      const request = ++this.pageRequest;
      try { await this.loadImage(this.worker, this.pageNumber, request); }
      catch (error) {
        if (request === this.pageRequest) this.error = error instanceof Error ? error.message : 'Не удалось показать изображение страницы.';
      } finally { if (request === this.pageRequest) this.pageLoading = false; }
    }
  }

  goToPageInput(value: string, input: HTMLInputElement): void {
    const page = Number(value);
    if (Number.isInteger(page) && page >= 1 && page <= this.pageCount) void this.showPage(page);
    else this.notice = `Введите номер страницы от 1 до ${this.pageCount}.`;
    input.value = String(this.pageNumber);
  }

  private releaseImage(): void {
    if (this.imageUrl) this.worker?.revokeObjectURL(this.imageUrl);
    this.imageUrl = '';
    this.imageWidth = this.imageHeight = 0;
  }

  async convertToFb2(): Promise<void> {
    if (!this.worker || this.converting) return;
    this.conversionError = '';
    if (!this.auth.getAccessToken()) {
      this.conversionError = 'Для нейросетевой конвертации войдите в аккаунт.';
      this.conversionStage = 'error';
      return;
    }
    try {
      if (!this.conversionPlan) {
        if (this.pageCount > 1200) throw new Error('Конвертация доступна для книг до 1200 страниц.');
        this.conversionStage = 'extracting';
        this.conversionDone = 0;
        this.conversionTotal = this.pageCount;
        const pages: string[] = [];
        let totalCharacters = 0;
        for (let page = 1; page <= this.pageCount; page++) {
          let text = '';
          try { text = await this.worker.doc.getPage(page).getText().run() || ''; }
          catch { /* Continue: a page can lack a decodable text layer. */ }
          totalCharacters += text.length;
          if (totalCharacters > 1_200_000) throw new Error('В книге слишком много текста для одной конвертации (предел — 1,2 млн символов).');
          pages.push(text);
          this.conversionDone = page;
        }
        if (!totalCharacters) throw new Error('В этом DjVu нет текстового слоя. Для конвертации нужна книга с распознанным текстом.');
        const plan = planDjvuConversion(pages, this.chapters);
        if (!plan.blocks.length) throw new Error('Не удалось извлечь текст для конвертации.');
        if (plan.blocks.length > 250) throw new Error('Книга слишком велика для одной конвертации (предел — 250 блоков).');
        this.conversionPlan = plan;
        this.conversionIndex = 0;
      }
      const plan = this.conversionPlan;
      this.conversionStage = 'processing';
      this.conversionTotal = plan.blocks.length;
      this.conversionDone = this.conversionIndex;
      while (this.conversionIndex < plan.blocks.length) {
        const block = plan.blocks[this.conversionIndex];
        const chapter = plan.chapters[block.chapterIndex];
        const previous = chapter.fragments.at(-1) || '';
        const context = new DOMParser().parseFromString(`<root>${previous}</root>`, 'application/xml').documentElement.textContent?.slice(-700) || '';
        const result = await firstValueFrom(this.http.post<{ fragment: string }>('/api/djvu-conversion/process-block', {
          text: block.text, chapterTitle: chapter.title, previousContext: context
        }).pipe(takeUntil(this.cancelled)));
        if (!result.fragment) throw new Error('Нейросеть вернула пустой блок. Повторите обработку.');
        chapter.fragments.push(result.fragment);
        this.conversionIndex++;
        this.conversionDone = this.conversionIndex;
      }
      const bookTitle = this.filename.replace(/\.djvu?$/i, '') || 'Книга';
      const xml = buildFb2FromDjvu(bookTitle, plan.chapters);
      const filename = (bookTitle.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '-').slice(0, 100) || 'Книга') + '.fb2';
      this.convertedFile = new File([xml], filename, { type: 'application/x-fictionbook+xml' });
      this.conversionStage = 'done';
    } catch (error) {
      this.conversionError = error instanceof HttpErrorResponse
        ? error.status === 401 ? 'Для нейросетевой конвертации войдите в аккаунт.'
          : error.error?.error || 'Сервер не обработал блок книги. Попробуйте продолжить.'
        : error instanceof Error ? error.message : 'Не удалось конвертировать книгу.';
      this.conversionStage = 'error';
    }
  }

  downloadFb2(): void {
    if (!this.convertedFile) return;
    const url = URL.createObjectURL(this.convertedFile);
    const link = document.createElement('a');
    link.href = url;
    link.download = this.convertedFile.name;
    document.body.appendChild(link);
    link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  openConvertedFb2(): void {
    if (this.convertedFile) this.readerFiles.redirectIfNeeded(this.convertedFile, 'djvu');
  }

  queueConversion(): void {
    if (this.sourceFile) this.readerFiles.queueDjvuConversion(this.sourceFile);
  }

  @HostListener('document:dragenter', ['$event'])
  onDragEnter(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes('Files')) return;
    event.preventDefault(); this.dragDepth++; this.dragging = !this.loading && !this.converting;
  }
  @HostListener('document:dragover', ['$event'])
  onDragOver(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes('Files')) return;
    event.preventDefault(); event.dataTransfer.dropEffect = this.loading || this.converting ? 'none' : 'copy';
    this.dragging = !this.loading && !this.converting;
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
    event.preventDefault(); this.resetDrag();
    if (this.loading || this.converting) return;
    if (event.dataTransfer?.files.length !== 1) { this.error = 'Перетащите одну книгу DjVu.'; return; }
    void this.loadFile(event.dataTransfer.files[0]);
  }
  @HostListener('document:dragend')
  @HostListener('window:blur')
  resetDrag(): void { this.dragging = false; this.dragDepth = 0; }

  ngOnDestroy(): void {
    this.cancelled.next();
    this.cancelled.complete();
    ++this.generation;
    ++this.pageRequest;
    this.releaseImage();
    this.worker?.terminate();
    this.worker = undefined;
  }
}
