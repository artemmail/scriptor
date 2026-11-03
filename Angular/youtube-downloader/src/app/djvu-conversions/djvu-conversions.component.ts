import { CommonModule } from '@angular/common';
import { Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';
import { RouterModule } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { firstValueFrom } from 'rxjs';
import { DjvuBookmark, DjvuWorker, loadLibrary } from '../djvu-reader/djvu-reader.component';
import { DjvuOutlineEntry, planDjvuConversion } from '../djvu-reader/djvu-to-fb2';
import { ReaderFileTransferService } from '../shared/reader-file-transfer.service';

interface BookJob {
  id: string; title: string; fileName: string; status: string; totalBlocks: number;
  completedBlocks: number; createdAt: string; updatedAt: string; completedAt?: string; error?: string;
}
interface QueueResponse { jobs: BookJob[]; remainingVideoCredits: number; bookCost: number; }

@Component({
  selector: 'app-djvu-conversions', standalone: true,
  imports: [CommonModule, MatIconModule, RouterModule],
  templateUrl: './djvu-conversions.component.html',
  styleUrls: ['../shared/account-page.css', './djvu-conversions.component.css']
})
export class DjvuConversionsComponent implements OnInit, OnDestroy {
  jobs: BookJob[] = [];
  balance?: number;
  busy = false;
  refreshing = false;
  dragging = false;
  stage = '';
  error = '';
  notice = '';
  progress = 0;
  total = 0;
  openingId = '';
  private poll?: ReturnType<typeof setInterval>;
  private worker?: DjvuWorker;
  private destroyed = false;

  constructor(private http: HttpClient, private readerFiles: ReaderFileTransferService, title: Title) { title.setTitle('Мои книги — DjVu в FB2 | YouScriptor'); }

  ngOnInit(): void {
    void this.refresh();
    const pending = this.readerFiles.takeForConversion();
    if (pending) void this.upload(pending);
    this.poll = setInterval(() => void this.refresh(), 8000);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    if (this.poll) clearInterval(this.poll);
    this.worker?.terminate();
  }

  async refresh(): Promise<void> {
    if (this.refreshing) return;
    this.refreshing = true;
    try {
      const response = await firstValueFrom(this.http.get<QueueResponse>('/api/djvu-books'));
      if (this.destroyed) return;
      this.jobs = response.jobs;
      this.balance = response.remainingVideoCredits;
    } catch {
      if (!this.jobs.length) this.error = 'Не удалось загрузить очередь. Обновите страницу.';
    } finally { this.refreshing = false; }
  }

  async openFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) await this.upload(file);
  }

  async upload(file: File): Promise<void> {
    if (this.busy) return;
    this.error = ''; this.notice = '';
    if (!/\.djvu?$/i.test(file.name)) { this.error = 'Выберите файл DjVu (.djvu или .djv).'; return; }
    if (file.size > 100 * 1024 * 1024) { this.error = 'Размер файла превышает 100 МБ.'; return; }
    if (this.balance !== undefined && this.balance < 10) { this.error = 'Для конвертации нужно 10 видео-кредитов.'; return; }
    this.busy = true;
    this.stage = 'Читаем текстовый слой и оглавление…';
    let worker: DjvuWorker | undefined;
    try {
      const buffer = await file.arrayBuffer();
      if (new TextDecoder('ascii').decode(buffer.slice(0, 8)) !== 'AT&TFORM')
        throw new Error('Это не файл DjVu. Проверьте формат книги.');
      const library = await loadLibrary();
      worker = new library.Worker('/assets/vendor/djvu/djvu.js');
      this.worker = worker;
      await worker.createDocument(buffer);
      if (!await worker.doc.isBundled().run())
        throw new Error('Нужна книга DjVu, собранная в один файл.');
      const count = await worker.doc.getPagesQuantity().run();
      if (!Number.isInteger(count) || count < 1 || count > 1200)
        throw new Error('Конвертация доступна для книг до 1200 страниц.');
      const contents = await worker.doc.getContents().run();
      const chapters = await this.readContents(worker, contents, count);
      const pages: string[] = [];
      let characters = 0;
      this.total = count;
      for (let page = 1; page <= count; page++) {
        let value = '';
        try { value = await worker.doc.getPage(page).getText().run() || ''; }
        catch { /* A page may have no decodable text layer. */ }
        characters += value.length;
        if (characters > 1_200_000) throw new Error('В книге слишком много текста (предел — 1,2 млн символов).');
        pages.push(value);
        this.progress = page;
      }
      if (!characters) throw new Error('В этом DjVu нет текстового слоя. Нужна книга с распознанным текстом.');
      const plan = planDjvuConversion(pages, chapters);
      if (!plan.blocks.length || plan.blocks.length > 250)
        throw new Error('Не удалось подготовить книгу: допустимо не более 250 блоков текста.');
      this.stage = 'Добавляем книгу в очередь…';
      const title = file.name.replace(/\.djvu?$/i, '') || 'Книга';
      await firstValueFrom(this.http.post('/api/djvu-books', {
        fileName: file.name, title,
        chapters: plan.chapters.map(ch => ({ title: ch.title, page: ch.page, depth: ch.depth })),
        blocks: plan.blocks
      }));
      this.notice = 'Книга добавлена в очередь. Теперь можно закрыть браузер и вернуться позже.';
      await this.refresh();
    } catch (error) {
      this.error = error instanceof HttpErrorResponse
        ? error.error?.error || 'Не удалось добавить книгу в очередь. Попробуйте ещё раз.'
        : error instanceof Error ? error.message : 'Не удалось прочитать DjVu.';
    } finally {
      worker?.terminate();
      this.worker = undefined;
      this.busy = false;
      this.stage = '';
      this.progress = 0;
      this.total = 0;
    }
  }

  private async readContents(worker: DjvuWorker, entries: DjvuBookmark[], count: number): Promise<DjvuOutlineEntry[]> {
    const result: DjvuOutlineEntry[] = [];
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

  async retry(job: BookJob): Promise<void> {
    this.error = '';
    try {
      await firstValueFrom(this.http.post(`/api/djvu-books/${job.id}/retry`, {}));
      await this.refresh();
    } catch (error) {
      this.error = error instanceof HttpErrorResponse ? error.error?.error || 'Не удалось повторить задачу.' : 'Не удалось повторить задачу.';
    }
  }

  async download(job: BookJob): Promise<void> {
    this.error = '';
    try {
      const blob = await firstValueFrom(this.http.get(`/api/djvu-books/${job.id}/download`, { responseType: 'blob' }));
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = this.bookFileName(job);
      anchor.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch { this.error = 'Не удалось скачать FB2. Попробуйте ещё раз.'; }
  }

  async open(job: BookJob): Promise<void> {
    if (this.openingId) return;
    this.error = '';
    this.openingId = job.id;
    try {
      const blob = await firstValueFrom(this.http.get(`/api/djvu-books/${job.id}/download`, { responseType: 'blob' }));
      const file = new File([blob], this.bookFileName(job), { type: 'application/x-fictionbook+xml' });
      if (!this.readerFiles.redirectIfNeeded(file, 'djvu'))
        throw new Error('Не удалось открыть FB2 Reader.');
    } catch {
      this.error = 'Не удалось открыть FB2. Попробуйте ещё раз.';
    } finally { this.openingId = ''; }
  }

  private bookFileName(job: BookJob): string {
    return `${job.title.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '-').slice(0, 100) || 'Книга'}.fb2`;
  }

  label(status: string): string {
    return ({ queued: 'В очереди', processing: 'Обрабатывается', completed: 'Готово',
      failed: 'Ошибка', awaiting_credits: 'Нужны кредиты' } as Record<string, string>)[status] || status;
  }

  @HostListener('document:dragover', ['$event']) onDragOver(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes('Files')) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = this.busy ? 'none' : 'copy';
    this.dragging = !this.busy;
  }
  @HostListener('document:dragleave', ['$event']) onDragLeave(event: DragEvent): void {
    if (!event.relatedTarget) this.dragging = false;
  }
  @HostListener('document:drop', ['$event']) onDrop(event: DragEvent): void {
    if (!event.dataTransfer?.files.length) return;
    event.preventDefault(); this.dragging = false;
    if (event.dataTransfer.files.length === 1) void this.upload(event.dataTransfer.files[0]);
    else this.error = 'Перетащите одну книгу DjVu.';
  }
}
