import { CommonModule } from '@angular/common';
import { Component, HostListener, OnDestroy, computed, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { saveAs } from 'file-saver';
import { createZipBlob } from './zip-utils';

interface BatchItem {
  readonly id: string;
  readonly file: File;
  readonly name: string;
  readonly size: number;
  status: 'pending' | 'processing' | 'done' | 'error';
  error?: string;
  previewUrl: string;
  previewFailed?: boolean;
  resultUrl?: string;
  resultBlob?: Blob;
  resultSize?: number;
  width?: number;
  height?: number;
}

@Component({
  selector: 'app-png-to-webp-batch',
  standalone: true,
  imports: [CommonModule, RouterLink, MatIconModule, MatSnackBarModule],
  templateUrl: './png-to-webp-batch.component.html',
  styleUrls: ['../shared/account-page.css', './png-to-webp-batch.component.css'],
})
export class PngToWebpBatchComponent implements OnDestroy {
  readonly quality = signal(0.82);
  readonly qualityPercent = computed(() => Math.round(this.quality() * 100));
  readonly items = signal<BatchItem[]>([]);
  readonly converting = signal(false);
  readonly archiving = signal(false);
  readonly stopping = signal(false);
  readonly busy = computed(() => this.converting() || this.archiving());
  readonly dragOver = signal(false);
  readonly notice = signal('');
  readonly filter = signal<'all' | 'done' | 'error'>('all');
  readonly completedCount = computed(() => this.items().filter(item => item.status === 'done').length);
  readonly errorCount = computed(() => this.items().filter(item => item.status === 'error').length);
  readonly totalCount = computed(() => this.items().length);
  readonly pendingCount = computed(() => this.items().filter(item => item.status === 'pending' || item.status === 'error').length);
  readonly totalSize = computed(() => this.items().reduce((sum, item) => sum + item.size, 0));
  readonly resultSize = computed(() => this.items().reduce((sum, item) => sum + (item.resultSize ?? 0), 0));
  readonly convertedSourceSize = computed(() => this.items().filter(item => item.status === 'done').reduce((sum, item) => sum + item.size, 0));
  readonly savings = computed(() => this.convertedSourceSize() - this.resultSize());
  readonly savingsPercent = computed(() => this.convertedSourceSize() ? Math.round(Math.abs(this.savings()) / this.convertedSourceSize() * 100) : 0);
  readonly overallProgress = computed(() => this.totalCount() ? Math.round((this.completedCount() + this.errorCount()) / this.totalCount() * 100) : 0);
  readonly visibleItems = computed(() => this.items().filter(item => this.filter() === 'all' || item.status === this.filter()));
  private destroyed = false;
  private dragDepth = 0;

  constructor(private readonly snackBar: MatSnackBar, title: Title) {
    title.setTitle('Пакетный PNG → WebP — конвертер изображений YouScriptor');
  }

  @HostListener('window:paste', ['$event'])
  onPaste(event: ClipboardEvent): void {
    const target = event.target as HTMLElement | null;
    if (target?.closest('input:not([type="range"]), textarea, [contenteditable="true"]')) return;
    const files = Array.from(event.clipboardData?.files ?? []);
    if (!files.length) return;
    event.preventDefault();
    this.addFiles(files);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.items().forEach(item => this.revokeItem(item));
  }

  onFileInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.addFiles(Array.from(input.files ?? []));
    input.value = '';
  }

  onDragEnter(event: DragEvent): void {
    event.preventDefault();
    if (!this.busy()) { this.dragDepth++; this.dragOver.set(true); }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = this.busy() ? 'none' : 'copy';
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragDepth = Math.max(0, this.dragDepth - 1);
    if (!this.dragDepth) this.dragOver.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragDepth = 0;
    this.dragOver.set(false);
    this.addFiles(Array.from(event.dataTransfer?.files ?? []));
  }

  removeItem(id: string): void {
    if (this.busy()) return;
    const target = this.items().find(item => item.id === id);
    if (target) this.revokeItem(target);
    this.items.update(items => items.filter(item => item.id !== id));
    if (!this.totalCount()) this.filter.set('all');
  }

  clearAll(): void {
    if (this.busy()) return;
    this.items().forEach(item => this.revokeItem(item));
    this.items.set([]);
    this.filter.set('all');
    this.notice.set('');
  }

  convertAll(): Promise<void> {
    return this.convertItems(this.items().filter(item => item.status !== 'done'));
  }

  convertSingle(item: BatchItem): Promise<void> {
    return this.convertItems([item]);
  }

  stopConversion(): void { this.stopping.set(true); }

  private async convertItems(queue: BatchItem[]): Promise<void> {
    if (this.busy() || !queue.length) return;
    this.converting.set(true);
    this.stopping.set(false);
    this.notice.set('');
    this.filter.set('all');
    const quality = this.quality();
    try {
      for (const item of queue) {
        if (this.destroyed || this.stopping()) break;
        this.clearResult(item);
        item.status = 'processing';
        item.error = undefined;
        this.refresh();
        try {
          const { blob, width, height } = await this.convertFile(item.file, quality);
          if (this.destroyed) break;
          item.resultBlob = blob;
          item.resultSize = blob.size;
          item.resultUrl = URL.createObjectURL(blob);
          item.width = width;
          item.height = height;
          item.status = 'done';
        } catch (error) {
          if (this.destroyed) break;
          item.error = error instanceof Error ? error.message : 'Не удалось преобразовать изображение.';
          item.status = 'error';
        }
        this.refresh();
        // Allow progress and the stop control to paint between files.
        await new Promise(resolve => setTimeout(resolve, 0));
      }
    } finally {
      this.converting.set(false);
      if (!this.destroyed) this.snackBar.open(this.stopping() ? 'Очередь остановлена. Готовые файлы можно скачать.' :
        this.errorCount() ? 'Обработка завершена. Проверьте файлы с ошибками.' : 'Готово. Скачайте WebP по одному или в ZIP.', '', { duration: 3500 });
      this.stopping.set(false);
    }
  }

  async downloadAll(): Promise<void> {
    if (this.busy()) return;
    const ready = this.items().filter(item => item.status === 'done' && item.resultBlob);
    if (!ready.length) return;
    this.archiving.set(true);
    this.notice.set('');
    try {
      const used = new Set<string>();
      const files = ready.map(item => {
        const base = this.baseName(item.name);
        let name = `${base}.webp`, suffix = 2;
        while (used.has(name.toLowerCase())) name = `${base} (${suffix++}).webp`;
        used.add(name.toLowerCase());
        return { name, blob: item.resultBlob! };
      });
      const archive = await createZipBlob(files);
      if (!this.destroyed) saveAs(archive, `youscriptor-webp-${new Date().toISOString().slice(0, 10)}.zip`);
    } catch {
      if (!this.destroyed) this.notice.set('Не удалось собрать ZIP. Попробуйте скачать готовые файлы по одному.');
    } finally { this.archiving.set(false); }
  }

  downloadSingle(item: BatchItem): void {
    if (item.status === 'done' && item.resultBlob) saveAs(item.resultBlob, `${this.baseName(item.name)}.webp`);
  }

  onQualityInput(value: number): void {
    if (this.busy() || !Number.isFinite(value)) return;
    const quality = Math.min(1, Math.max(0.1, value));
    if (quality === this.quality()) return;
    this.quality.set(quality);
    const hadResults = this.completedCount() > 0;
    this.items().forEach(item => { this.clearResult(item); item.status = 'pending'; item.error = undefined; });
    this.filter.set('all');
    this.refresh();
    if (hadResults) this.snackBar.open('Качество изменено. Конвертируйте файлы заново.', '', { duration: 3000 });
  }

  onSliderInput(event: Event): void { this.onQualityInput(Number((event.target as HTMLInputElement).value)); }

  onPreviewError(item: BatchItem): void { item.previewFailed = true; this.refresh(); }

  formatBytes(size: number | undefined): string {
    if (size === undefined) return '—';
    if (!size) return '0 Б';
    const units = ['Б', 'КБ', 'МБ', 'ГБ'];
    const index = Math.min(Math.floor(Math.log(size) / Math.log(1024)), units.length - 1);
    const value = size / Math.pow(1024, index);
    return `${value.toFixed(value >= 10 || index === 0 ? 0 : 1)} ${units[index]}`;
  }

  differenceLabel(item: BatchItem): string {
    if (item.resultSize === undefined || !item.size) return '';
    const percent = Math.round(Math.abs(item.size - item.resultSize) / item.size * 100);
    return item.resultSize === item.size || percent === 0 ? 'Почти тот же размер' :
      item.resultSize < item.size ? `Меньше на ${percent}%` : `Больше на ${percent}%`;
  }

  trackById(_index: number, item: BatchItem): string { return item.id; }
  private baseName(name: string): string { return name.replace(/\.[^.]+$/, '').replace(/[\\/\u0000-\u001f]/g, '_').trim() || 'image'; }
  private refresh(): void { this.items.update(items => [...items]); }
  private isSupportedImage(file: File): boolean {
    return file.type.startsWith('image/') || /\.(png|jpe?g|webp|bmp|gif|tiff?|avif|heic|heif)$/i.test(file.name);
  }

  private addFiles(files: File[]): void {
    if (!files.length) return;
    if (this.busy()) { this.snackBar.open('Дождитесь окончания обработки, чтобы добавить файлы.', '', { duration: 2500 }); return; }
    const key = (file: File) => `${file.name}_${file.size}_${file.lastModified}`;
    const existing = new Set(this.items().map(item => key(item.file)));
    const added: BatchItem[] = [];
    let duplicates = 0, unsupported = 0;
    for (const file of files) {
      if (!this.isSupportedImage(file)) { unsupported++; continue; }
      if (existing.has(key(file))) { duplicates++; continue; }
      added.push({ id: crypto.randomUUID(), file, name: file.name, size: file.size, status: 'pending', previewUrl: URL.createObjectURL(file) });
      existing.add(key(file));
    }
    this.items.update(items => [...items, ...added]);
    this.filter.set('all');
    this.notice.set(unsupported ? `Пропущено файлов: ${unsupported}. Добавляйте изображения PNG, JPEG, WebP или другие форматы, которые открывает ваш браузер.` : '');
    const message = [added.length ? `Добавлено: ${added.length}.` : '', duplicates ? `Уже в списке: ${duplicates}.` : ''].filter(Boolean).join(' ');
    if (message) this.snackBar.open(message, '', { duration: 2500 });
  }

  private async convertFile(file: File, quality: number): Promise<{ blob: Blob; width: number; height: number }> {
    const image = await this.loadImage(file);
    const canvas = document.createElement('canvas');
    try {
      const width = image.naturalWidth, height = image.naturalHeight;
      if (!width || !height) throw new Error('У изображения не удалось определить размеры.');
      canvas.width = width;
      canvas.height = height;
      const context = canvas.getContext('2d');
      if (!context) throw new Error('Браузеру не хватило ресурсов. Попробуйте изображение меньшего размера.');
      context.drawImage(image, 0, 0);
      const blob = await new Promise<Blob>((resolve, reject) => canvas.toBlob(result => {
        if (!result) reject(new Error('Не удалось создать WebP. Попробуйте изображение меньшего размера.'));
        else if (result.type !== 'image/webp') reject(new Error('Этот браузер не поддерживает создание WebP. Откройте страницу в современном браузере.'));
        else resolve(result);
      }, 'image/webp', quality));
      return { blob, width, height };
    } finally { canvas.width = 0; canvas.height = 0; image.src = ''; }
  }

  private loadImage(file: File): Promise<HTMLImageElement> {
    return new Promise((resolve, reject) => {
      const image = new Image();
      const url = URL.createObjectURL(file);
      image.onload = () => { URL.revokeObjectURL(url); resolve(image); };
      image.onerror = () => { URL.revokeObjectURL(url); reject(new Error('Не удалось открыть изображение. Возможно, файл повреждён или его формат не поддерживается браузером.')); };
      image.src = url;
    });
  }

  private clearResult(item: BatchItem): void {
    if (item.resultUrl) URL.revokeObjectURL(item.resultUrl);
    item.resultUrl = undefined; item.resultBlob = undefined; item.resultSize = undefined;
  }
  private revokeItem(item: BatchItem): void { URL.revokeObjectURL(item.previewUrl); this.clearResult(item); }
}
