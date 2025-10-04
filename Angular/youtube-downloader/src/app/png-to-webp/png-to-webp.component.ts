import { CommonModule } from '@angular/common';
import { Component, HostListener, OnDestroy, Signal, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSliderModule } from '@angular/material/slider';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { ImageEditorDialogComponent, ImageEditorDialogResult } from './image-editor-dialog.component';

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
    MatDialogModule,
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
  readonly editedImageUrl = signal<string | null>(null);
  readonly workingDimensions = signal<{ width: number; height: number } | null>(null);
  readonly workingFileSize = signal<number>(0);
  readonly result = signal<ConversionResult | null>(null);
  readonly error = signal<string | null>(null);

  private sourceImage: ImageBitmap | HTMLImageElement | null = null;
  private originalImage: ImageBitmap | HTMLImageElement | null = null;
  private canvas: HTMLCanvasElement | null = null;
  private lastObjectUrls: string[] = [];
  private pendingQuality: number | null = null;
  private currentImageBlob: Blob | null = null;

  readonly originalSizeLabel: Signal<string> = computed(() => this.formatBytes(this.originalSize()));
  readonly resultSizeLabel: Signal<string> = computed(() => {
    const res = this.result();
    return res ? this.formatBytes(res.size) : '—';
  });

  readonly workingSizeLabel: Signal<string> = computed(() => this.formatBytes(this.workingFileSize()));

  readonly workingDimensionsLabel: Signal<string> = computed(() => {
    const dims = this.workingDimensions();
    return dims ? `${dims.width} × ${dims.height} px` : '—';
  });

  readonly compressionRatio: Signal<string> = computed(() => {
    const original = this.originalSize();
    const res = this.result();
    if (!original || !res) return '—';
    const ratio = original / res.size;
    return ratio ? ratio.toFixed(2) + '×' : '—';
  });

  readonly hasEdits: Signal<boolean> = computed(() => this.editedImageUrl() !== null);

  readonly workingPreviewUrl: Signal<string | null> = computed(() =>
    this.editedImageUrl() ?? this.originalUrl(),
  );

  constructor(private snackBar: MatSnackBar, private dialog: MatDialog) {}

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
      this.originalImage = this.sourceImage;
      const dimensions = this.getImageDimensions(this.sourceImage);
      this.originalDimensions.set(dimensions);
      this.workingDimensions.set(dimensions);
      this.workingFileSize.set(file.size);
      this.currentImageBlob = file;
      this.setEditedPreviewUrl(null);

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

  async openEditor(): Promise<void> {
    if (!this.currentImageBlob) {
      return;
    }

    const dialogRef = this.dialog.open(ImageEditorDialogComponent, {
      data: {
        blob: this.currentImageBlob,
        name: this.originalFile()?.name ?? 'image.png',
      },
      panelClass: 'image-editor-panel',
      width: '90vw',
      maxWidth: '960px',
    });

    const result = await firstValueFrom(dialogRef.afterClosed());
    if (!result) {
      return;
    }

    await this.applyEditorResult(result);
  }

  async resetEdits(): Promise<void> {
    const file = this.originalFile();
    if (!file || !this.originalImage) {
      return;
    }

    this.processing.set(true);
    try {
      this.setEditedPreviewUrl(null);
      this.currentImageBlob = file;
      this.workingFileSize.set(file.size);
      this.releaseSourceImage();
      this.sourceImage = this.originalImage;
      const dims = this.originalDimensions();
      this.workingDimensions.set(dims ? { ...dims } : null);
      await this.convertToWebp();
    } catch (err) {
      console.error(err);
      this.error.set('Не удалось сбросить правки.');
    } finally {
      this.processing.set(false);
    }
  }

  clear(): void {
    this.resetState();
  }

  ngOnDestroy(): void {
    this.setEditedPreviewUrl(null);
    this.cleanupObjectUrls();
    this.releaseSourceImage();
    if (this.originalImage && 'close' in this.originalImage) {
      try {
        (this.originalImage as ImageBitmap).close();
      } catch {
        /* noop */
      }
    }
    this.originalImage = null;
    this.sourceImage = null;
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

  private async loadImage(blob: Blob): Promise<ImageBitmap | HTMLImageElement> {
    if (typeof window !== 'undefined' && 'createImageBitmap' in window) {
      try {
        return await createImageBitmap(blob);
      } catch (error) {
        console.warn('createImageBitmap error, falling back to HTMLImageElement', error);
      }
    }

    return await new Promise<HTMLImageElement>((resolve, reject) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = () => reject('Не удалось загрузить изображение');
      img.src = URL.createObjectURL(blob);
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
    this.setEditedPreviewUrl(null);
    this.cleanupObjectUrls();
    this.error.set(null);
    this.result.set(null);
    this.originalFile.set(null);
    this.originalUrl.set(null);
    this.originalDimensions.set(null);
    this.workingDimensions.set(null);
    this.workingFileSize.set(0);
    this.originalSize.set(0);
    this.canvas = null;
    this.pendingQuality = null;
    this.currentImageBlob = null;
    this.releaseSourceImage();
    if (this.originalImage && 'close' in this.originalImage) {
      try {
        (this.originalImage as ImageBitmap).close();
      } catch {
        /* noop */
      }
    }
    this.sourceImage = null;
    this.originalImage = null;
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

  private async applyEditorResult(result: ImageEditorDialogResult): Promise<void> {
    this.processing.set(true);
    try {
      this.currentImageBlob = result.blob;
      this.workingFileSize.set(result.blob.size);
      const editedUrl = URL.createObjectURL(result.blob);
      this.setEditedPreviewUrl(editedUrl);
      await this.setActiveImageFromBlob(result.blob);
      await this.convertToWebp();
    } catch (err) {
      console.error(err);
      this.error.set('Не удалось применить изменения изображения.');
    } finally {
      this.processing.set(false);
    }
  }

  private setEditedPreviewUrl(url: string | null): void {
    const previous = this.editedImageUrl();
    if (previous) {
      URL.revokeObjectURL(previous);
      this.lastObjectUrls = this.lastObjectUrls.filter(item => item !== previous);
    }
    if (url) {
      this.registerObjectUrl(url);
    }
    this.editedImageUrl.set(url);
  }

  private async setActiveImageFromBlob(blob: Blob): Promise<void> {
    const image = await this.loadImage(blob);
    this.replaceSourceImage(image);
  }

  private replaceSourceImage(image: ImageBitmap | HTMLImageElement): void {
    this.releaseSourceImage();
    this.sourceImage = image;
    const dims = this.getImageDimensions(image);
    this.workingDimensions.set(dims);
  }

  private releaseSourceImage(): void {
    if (this.sourceImage && this.sourceImage !== this.originalImage && 'close' in this.sourceImage) {
      try {
        (this.sourceImage as ImageBitmap).close();
      } catch {
        /* noop */
      }
    }
  }
}
