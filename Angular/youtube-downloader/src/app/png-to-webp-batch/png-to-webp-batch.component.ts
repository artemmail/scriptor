import { CommonModule } from '@angular/common';
import { Component, HostListener, OnDestroy, Signal, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSliderModule } from '@angular/material/slider';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { saveAs } from 'file-saver';
import { Title } from '@angular/platform-browser';
import { createZipBlob } from './zip-utils';

interface BatchItem {
  readonly id: string;
  readonly file: File;
  readonly name: string;
  readonly size: number;
  status: 'pending' | 'processing' | 'done' | 'error';
  error?: string;
  previewUrl: string | null;
  resultBlob?: Blob;
  resultSize?: number;
  durationMs?: number;
}

@Component({
  selector: 'app-png-to-webp-batch',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatCardModule,
    MatDividerModule,
    MatIconModule,
    MatListModule,
    MatProgressBarModule,
    MatSliderModule,
    MatSnackBarModule,
  ],
  templateUrl: './png-to-webp-batch.component.html',
  styleUrls: ['./png-to-webp-batch.component.css'],
})
export class PngToWebpBatchComponent implements OnDestroy {
  readonly quality = signal(0.9);
  readonly items = signal<BatchItem[]>([]);
  readonly converting = signal(false);
  readonly dragOver = signal(false);

  readonly completedCount: Signal<number> = computed(
    () => this.items().filter(item => item.status === 'done').length,
  );

  readonly errorCount: Signal<number> = computed(
    () => this.items().filter(item => item.status === 'error').length,
  );

  readonly totalCount: Signal<number> = computed(() => this.items().length);

  readonly convertedReady: Signal<boolean> = computed(
    () => this.items().some(item => item.status === 'done'),
  );

  readonly overallProgress: Signal<number> = computed(() => {
    const total = this.totalCount();
    if (!total) {
      return 0;
    }
    const processed = this.items().filter(item => item.status === 'done' || item.status === 'error').length;
    const currentProcessing = this.items().some(item => item.status === 'processing');
    const baseProgress = (processed / total) * 100;
    return currentProcessing ? Math.min(99, baseProgress + 0.5) : Math.round(baseProgress);
  });

  constructor(
    private readonly snackBar: MatSnackBar,
    private readonly title: Title,
  ) {
    this.title.setTitle('Batch PNG → WebP — конвертер изображений YouScriptor');
  }

  @HostListener('window:paste', ['$event'])
  async onPaste(event: ClipboardEvent): Promise<void> {
    if (!event.clipboardData) {
      return;
    }
    const files = Array.from(event.clipboardData.files).filter(file => this.isSupportedImage(file));
    if (!files.length) {
      return;
    }
    event.preventDefault();
    this.addFiles(files);
    this.snackBar.open(`Добавлено ${files.length} изображение(-й) из буфера обмена`, '', { duration: 2000 });
  }

  ngOnDestroy(): void {
    this.items().forEach(item => this.revokePreview(item));
  }

  onFileInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files?.length) {
      return;
    }
    this.addFiles(Array.from(input.files));
    input.value = '';
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(true);
  }

  onDragLeave(event: DragEvent): void {
    if (event.currentTarget === event.target) {
      this.dragOver.set(false);
    }
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(false);
    if (!event.dataTransfer?.files?.length) {
      return;
    }
    const files = Array.from(event.dataTransfer.files).filter(file => this.isSupportedImage(file));
    if (!files.length) {
      this.snackBar.open('Перетащите изображения в поддерживаемых форматах', 'OK', { duration: 2500 });
      return;
    }
    this.addFiles(files);
  }

  removeItem(id: string): void {
    const [target] = this.items().filter(item => item.id === id);
    if (target) {
      this.revokePreview(target);
      this.items.update(items => items.filter(item => item.id !== id));
    }
  }

  clearAll(): void {
    this.items().forEach(item => this.revokePreview(item));
    this.items.set([]);
    this.converting.set(false);
  }

  async convertAll(): Promise<void> {
    if (!this.items().length) {
      this.snackBar.open('Добавьте изображения для конвертации', 'OK', { duration: 2500 });
      return;
    }
    if (this.converting()) {
      return;
    }
    this.converting.set(true);

    for (const item of this.items()) {
      if (item.status === 'done') {
        continue;
      }
      item.status = 'processing';
      item.error = undefined;
      try {
        const { blob, duration } = await this.convertFile(item.file);
        item.resultBlob = blob;
        item.resultSize = blob.size;
        item.durationMs = duration;
        item.status = 'done';
      } catch (err) {
        console.error('Failed to convert file', item.file.name, err);
        item.error = 'Ошибка конвертации. Попробуйте другое изображение.';
        item.status = 'error';
      }
      this.items.update(items => [...items]);
    }

    this.converting.set(false);
    if (this.completedCount()) {
      this.snackBar.open('Конвертация завершена', '', { duration: 2000 });
    }
  }

  async downloadAll(): Promise<void> {
    const ready = this.items().filter(item => item.status === 'done' && item.resultBlob);
    if (!ready.length) {
      this.snackBar.open('Нет готовых изображений для скачивания', 'OK', { duration: 2500 });
      return;
    }
    const files = ready.map(item => ({
      name: `${item.name.replace(/\.[^.]+$/, '')}.webp`,
      blob: item.resultBlob!,
    }));
    const archive = await createZipBlob(files);
    const timestamp = new Date()
      .toISOString()
      .replaceAll(':', '-')
      .replaceAll('.', '-');
    saveAs(archive, `webp-batch-${timestamp}.zip`);
  }

  async convertSingle(item: BatchItem): Promise<void> {
    if (this.converting()) {
      return;
    }
    item.status = 'processing';
    item.error = undefined;
    this.items.update(items => [...items]);
    try {
      const { blob, duration } = await this.convertFile(item.file);
      item.resultBlob = blob;
      item.resultSize = blob.size;
      item.durationMs = duration;
      item.status = 'done';
      this.snackBar.open(`${item.name} преобразован`, '', { duration: 2000 });
    } catch (err) {
      console.error('Failed to convert file', item.file.name, err);
      item.error = 'Ошибка конвертации. Попробуйте другое изображение.';
      item.status = 'error';
    }
    this.items.update(items => [...items]);
  }

  async downloadSingle(item: BatchItem): Promise<void> {
    if (item.status !== 'done' || !item.resultBlob) {
      this.snackBar.open('Изображение ещё не готово', 'OK', { duration: 2000 });
      return;
    }
    const baseName = item.name.replace(/\.[^.]+$/, '');
    saveAs(item.resultBlob, `${baseName}.webp`);
  }

  onQualityInput(value: number | null | undefined): void {
    if (value === null || value === undefined || Number.isNaN(value)) {
      return;
    }
    this.quality.set(Math.min(1, Math.max(0.1, value)));
  }

  formatBytes(size: number | undefined): string {
    if (!size && size !== 0) {
      return '—';
    }
    if (size === 0) {
      return '0 Б';
    }
    const units = ['Б', 'КБ', 'МБ', 'ГБ'];
    const idx = Math.min(Math.floor(Math.log(size) / Math.log(1024)), units.length - 1);
    const value = size / Math.pow(1024, idx);
    return `${value.toFixed(value >= 10 || idx === 0 ? 0 : 1)} ${units[idx]}`;
  }

  trackById(index: number, item: BatchItem): string {
    return item.id;
  }

  private isSupportedImage(file: File): boolean {
    if (file.type.startsWith('image/')) {
      return true;
    }
    const lower = file.name.toLowerCase();
    return ['.png', '.jpg', '.jpeg', '.webp', '.bmp', '.gif', '.tiff', '.tif', '.avif', '.heic', '.heif']
      .some(ext => lower.endsWith(ext));
  }

  private addFiles(files: File[]): void {
    if (!files.length) {
      return;
    }
    const existingNames = new Set(this.items().map(item => `${item.name}_${item.size}`));
    const newItems: BatchItem[] = [];
    for (const file of files) {
      if (!this.isSupportedImage(file)) {
        continue;
      }
      const uniqueKey = `${file.name}_${file.size}`;
      if (existingNames.has(uniqueKey)) {
        continue;
      }
      const previewUrl = URL.createObjectURL(file);
      newItems.push({
        id: crypto.randomUUID(),
        file,
        name: file.name,
        size: file.size,
        status: 'pending',
        previewUrl,
      });
      existingNames.add(uniqueKey);
    }
    if (!newItems.length) {
      this.snackBar.open('Изображения уже добавлены или не поддерживаются', 'OK', { duration: 2500 });
      return;
    }
    this.items.update(items => [...items, ...newItems]);
    this.snackBar.open(`Добавлено ${newItems.length} изображение(-й)`, '', { duration: 2000 });
  }

  private async convertFile(file: File): Promise<{ blob: Blob; duration: number }> {
    const start = performance.now();
    const image = await this.loadImage(file);
    const canvas = document.createElement('canvas');
    const width = (image as HTMLImageElement).naturalWidth || (image as HTMLImageElement).width;
    const height = (image as HTMLImageElement).naturalHeight || (image as HTMLImageElement).height;
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    if (!ctx) {
      throw new Error('Не удалось создать контекст canvas');
    }
    ctx.drawImage(image, 0, 0);

    const blob = await new Promise<Blob>((resolve, reject) => {
      canvas.toBlob(result => {
        if (result) {
          resolve(result);
        } else {
          reject(new Error('Не удалось создать WebP'));
        }
      }, 'image/webp', this.quality());
    });
    const duration = performance.now() - start;
    return { blob, duration };
  }

  private loadImage(file: File): Promise<HTMLImageElement> {
    return new Promise((resolve, reject) => {
      const image = new Image();
      const objectUrl = URL.createObjectURL(file);
      image.onload = () => {
        URL.revokeObjectURL(objectUrl);
        resolve(image);
      };
      image.onerror = () => {
        URL.revokeObjectURL(objectUrl);
        reject(new Error('Не удалось загрузить изображение'));
      };
      image.src = objectUrl;
    });
  }

  private revokePreview(item: BatchItem): void {
    if (item.previewUrl) {
      URL.revokeObjectURL(item.previewUrl);
      item.previewUrl = null;
    }
  }
}
