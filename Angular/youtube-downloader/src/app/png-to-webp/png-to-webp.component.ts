import { CommonModule } from '@angular/common';
import { Component, HostListener, OnDestroy, Signal, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSliderModule } from '@angular/material/slider';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

interface ConversionResult {
  previewUrl: string;
  blob: Blob;
  size: number;
  durationMs: number;
}

@Component({
  selector: 'app-png-to-webp',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatDividerModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSliderModule,
    MatSnackBarModule,
  ],
  templateUrl: './png-to-webp.component.html',
  styleUrls: ['./png-to-webp.component.css']
})
export class PngToWebpComponent implements OnDestroy {
  readonly quality = signal(0.92);
  readonly processing = signal(false);
  readonly originalFile = signal<File | null>(null);
  readonly originalUrl = signal<string | null>(null);
  readonly originalSize = signal<number>(0);
  readonly originalDimensions = signal<{ width: number; height: number } | null>(null);
  readonly result = signal<ConversionResult | null>(null);
  readonly error = signal<string | null>(null);

  private sourceImage: ImageBitmap | HTMLImageElement | null = null;
  private canvas: HTMLCanvasElement | null = null;
  private lastObjectUrls: string[] = [];
  private pendingQuality: number | null = null;

  readonly originalSizeLabel: Signal<string> = computed(() => this.formatBytes(this.originalSize()));
  readonly resultSizeLabel: Signal<string> = computed(() => {
    const res = this.result();
    return res ? this.formatBytes(res.size) : '—';
  });

  readonly compressionRatio: Signal<string> = computed(() => {
    const original = this.originalSize();
    const res = this.result();
    if (!original || !res) return '—';
    const ratio = original / res.size;
    return ratio ? ratio.toFixed(2) + '×' : '—';
  });

  constructor(private snackBar: MatSnackBar) {}

  @HostListener('window:paste', ['$event'])
  async onPaste(event: ClipboardEvent): Promise<void> {
    if (!event.clipboardData) return;
    const file = Array.from(event.clipboardData.files).find(f => f.type === 'image/png' || f.type === 'image/x-png' || f.name.toLowerCase().endsWith('.png'));
    if (file) {
      event.preventDefault();
      await this.handleFile(file);
      this.snackBar.open('PNG получен из буфера обмена', '', { duration: 2000 });
    }
  }

  async onFileInput(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length) {
      await this.handleFile(input.files[0]);
      input.value = '';
    }
  }

  async onDrop(event: DragEvent): Promise<void> {
    event.preventDefault();
    if (event.dataTransfer?.files?.length) {
      const file = Array.from(event.dataTransfer.files).find(f => this.isPngFile(f));
      if (file) {
        await this.handleFile(file);
      } else {
        this.snackBar.open('Перетащите PNG изображение', 'OK', { duration: 2500 });
      }
    }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
  }

  async handleFile(file: File): Promise<void> {
    if (!this.isPngFile(file)) {
      this.snackBar.open('Поддерживаются только PNG изображения', 'OK', { duration: 2500 });
      return;
    }

    this.resetState();
    this.processing.set(true);
    this.error.set(null);

    try {
      this.originalFile.set(file);
      this.originalSize.set(file.size);
      const originalUrl = URL.createObjectURL(file);
      this.registerObjectUrl(originalUrl);
      this.originalUrl.set(originalUrl);

      this.sourceImage = await this.loadImage(file);
      const dimensions = this.getImageDimensions(this.sourceImage);
      this.originalDimensions.set(dimensions);

      await this.convertToWebp();
    } catch (err) {
      console.error(err);
      this.error.set('Не удалось преобразовать изображение. Проверьте поддержку WebP в браузере.');
    } finally {
      this.processing.set(false);
    }
  }

  async onQualityChange(value: number): Promise<void> {
    this.quality.set(value);
    if (!this.originalFile()) {
      return;
    }
    if (this.processing()) {
      this.pendingQuality = value;
      return;
    }
    this.processing.set(true);
    try {
      await this.convertToWebp();
    } catch (err) {
      console.error(err);
      this.error.set('Не удалось обновить WebP. Попробуйте другое качество.');
    } finally {
      this.processing.set(false);
      const nextQuality = this.pendingQuality;
      this.pendingQuality = null;
      if (nextQuality !== null && nextQuality !== value) {
        await this.onQualityChange(nextQuality);
      }
    }
  }

  async onSliderInput(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const value = input.valueAsNumber;
    if (Number.isNaN(value)) {
      return;
    }
    await this.onQualityChange(value);
  }

  downloadResult(): void {
    const res = this.result();
    const original = this.originalFile();
    if (!res || !original) return;

    const link = document.createElement('a');
    const suggestedName = original.name.replace(/\.png$/i, '') + `.webp`;
    link.href = res.previewUrl;
    link.download = suggestedName;
    link.click();
  }

  clear(): void {
    this.resetState();
  }

  ngOnDestroy(): void {
    this.cleanupObjectUrls();
    if (this.sourceImage && 'close' in this.sourceImage) {
      try {
        (this.sourceImage as ImageBitmap).close();
      } catch { /* noop */ }
    }
  }

  private async convertToWebp(): Promise<void> {
    if (!this.sourceImage) return;

    const quality = this.quality();
    const canvas = this.canvas ?? document.createElement('canvas');
    const ctx = canvas.getContext('2d');

    if (!ctx) {
      throw new Error('Canvas недоступен');
    }

    const { width, height } = this.getImageDimensions(this.sourceImage);
    if (!width || !height) {
      throw new Error('Невозможно определить размеры изображения');
    }

    canvas.width = width;
    canvas.height = height;
    ctx.clearRect(0, 0, width, height);
    ctx.drawImage(this.sourceImage, 0, 0, width, height);
    this.canvas = canvas;

    const start = performance.now();
    const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/webp', quality));
    const durationMs = performance.now() - start;

    if (!blob) {
      throw new Error('WebP не поддерживается');
    }

    this.cleanupResultUrl();
    const previewUrl = URL.createObjectURL(blob);
    this.registerObjectUrl(previewUrl);
    this.result.set({
      blob,
      previewUrl,
      size: blob.size,
      durationMs,
    });
    this.error.set(null);
  }

  private async loadImage(file: File): Promise<ImageBitmap | HTMLImageElement> {
    if (typeof window !== 'undefined' && 'createImageBitmap' in window) {
      try {
        return await createImageBitmap(file);
      } catch (error) {
        console.warn('createImageBitmap error, falling back to HTMLImageElement', error);
      }
    }

    return await new Promise<HTMLImageElement>((resolve, reject) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = () => reject('Не удалось загрузить изображение');
      img.src = URL.createObjectURL(file);
      this.registerObjectUrl(img.src);
    });
  }

  private getImageDimensions(source: ImageBitmap | HTMLImageElement): { width: number; height: number } {
    return { width: source.width, height: source.height };
  }

  private isPngFile(file: File): boolean {
    return file.type === 'image/png' || file.type === 'image/x-png' || file.name.toLowerCase().endsWith('.png');
  }

  private formatBytes(bytes: number): string {
    if (!bytes) return '0 Б';
    const units = ['Б', 'КБ', 'МБ', 'ГБ'];
    const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
    const size = bytes / Math.pow(1024, exponent);
    return `${size.toFixed(size >= 100 ? 0 : size >= 10 ? 1 : 2)} ${units[exponent]}`;
  }

  private resetState(): void {
    this.cleanupResultUrl();
    this.cleanupObjectUrls();
    this.error.set(null);
    this.result.set(null);
    this.originalFile.set(null);
    this.originalUrl.set(null);
    this.originalDimensions.set(null);
    this.originalSize.set(0);
    this.canvas = null;
    this.pendingQuality = null;
    if (this.sourceImage && 'close' in this.sourceImage) {
      try {
        (this.sourceImage as ImageBitmap).close();
      } catch { /* noop */ }
    }
    this.sourceImage = null;
  }

  private registerObjectUrl(url: string | null): void {
    if (!url) return;
    this.lastObjectUrls.push(url);
  }

  private cleanupResultUrl(): void {
    const res = this.result();
    if (res) {
      URL.revokeObjectURL(res.previewUrl);
      this.lastObjectUrls = this.lastObjectUrls.filter(url => url !== res.previewUrl);
    }
  }

  private cleanupObjectUrls(): void {
    this.lastObjectUrls.forEach(url => URL.revokeObjectURL(url));
    this.lastObjectUrls = [];
  }
}
