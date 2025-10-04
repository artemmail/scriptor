import { CommonModule } from '@angular/common';
import {
  AfterViewInit,
  Component,
  ElementRef,
  Inject,
  OnDestroy,
  Signal,
  ViewChild,
  WritableSignal,
  computed,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleChange, MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';

export interface CropRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface EditorState {
  rotation: number;
  flipHorizontal: boolean;
  flipVertical: boolean;
  crop: CropRect | null;
}

export interface ImageEditorDialogData {
  blob: Blob;
  name: string;
  previousState?: EditorState | null;
}

export interface ImageEditorDialogResult {
  blob: Blob;
  width: number;
  height: number;
  state: EditorState;
}

@Component({
  selector: 'app-image-editor-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule, MatButtonToggleModule],
  templateUrl: './image-editor-dialog.component.html',
  styleUrls: ['./image-editor-dialog.component.css'],
})
export class ImageEditorDialogComponent implements AfterViewInit, OnDestroy {
  @ViewChild('canvas', { static: true }) canvasRef!: ElementRef<HTMLCanvasElement>;
  @ViewChild('canvasContainer', { static: true }) canvasContainerRef!: ElementRef<HTMLDivElement>;

  readonly zoomOptions = [1, 0.5, 0.25] as const;

  readonly zoom: WritableSignal<number> = signal(1);
  readonly rotation: WritableSignal<number> = signal(0);
  readonly flipHorizontal: WritableSignal<boolean> = signal(false);
  readonly flipVertical: WritableSignal<boolean> = signal(false);
  readonly cropRect: WritableSignal<CropRect | null> = signal(null);
  readonly cropInfo: Signal<string> = computed(() => {
    const crop = this.cropRect();
    if (!crop) {
      return 'Полный размер';
    }
    return `${Math.round(crop.width)} × ${Math.round(crop.height)} px`;
  });

  readonly isCropping: WritableSignal<boolean> = signal(false);

  private image: ImageBitmap | HTMLImageElement | null = null;
  private resizeObserver?: ResizeObserver;
  private pointerActive = false;
  private activePointerId: number | null = null;
  private lastPointerPosition: { x: number; y: number } | null = null;
  private cropStart: { x: number; y: number } | null = null;
  private pan: { x: number; y: number } = { x: 0, y: 0 };
  private rafHandle: number | null = null;

  private get canvas(): HTMLCanvasElement {
    return this.canvasRef.nativeElement;
  }

  private get imageWidth(): number {
    return this.image?.width ?? 0;
  }

  private get imageHeight(): number {
    return this.image?.height ?? 0;
  }

  constructor(
    @Inject(MAT_DIALOG_DATA) public data: ImageEditorDialogData,
    private dialogRef: MatDialogRef<ImageEditorDialogComponent, ImageEditorDialogResult>,
  ) {
    this.dialogRef.disableClose = true;
    const previous = this.data.previousState;
    if (previous) {
      this.rotation.set(previous.rotation);
      this.flipHorizontal.set(previous.flipHorizontal);
      this.flipVertical.set(previous.flipVertical);
      this.cropRect.set(previous.crop ? { ...previous.crop } : null);
    }
  }

  ngAfterViewInit(): void {
    void this.initialize();
  }

  ngOnDestroy(): void {
    if (this.rafHandle !== null) {
      cancelAnimationFrame(this.rafHandle);
      this.rafHandle = null;
    }

    this.resizeObserver?.disconnect();

    const canvas = this.canvas;
    canvas.removeEventListener('pointerdown', this.onPointerDown);
    window.removeEventListener('pointermove', this.onPointerMove);
    window.removeEventListener('pointerup', this.onPointerUp);

    if (this.image && 'close' in this.image) {
      try {
        (this.image as ImageBitmap).close();
      } catch {
        /* noop */
      }
    }
  }

  toggleCrop(): void {
    this.isCropping.update(value => !value);
    if (this.isCropping()) {
      this.pointerActive = false;
      this.pan = { x: 0, y: 0 };
    }
  }

  onZoomChange(event: MatButtonToggleChange): void {
    const value = event.value as number;
    if (typeof value !== 'number') {
      return;
    }
    this.zoom.set(value);
    this.pan = { x: 0, y: 0 };
    this.scheduleRender();
  }

  rotateRight(): void {
    this.rotation.update(value => (value + 90) % 360);
    this.scheduleRender();
  }

  rotate180(): void {
    this.rotation.update(value => (value + 180) % 360);
    this.scheduleRender();
  }

  rotate270(): void {
    this.rotation.update(value => (value + 270) % 360);
    this.scheduleRender();
  }

  flipHorizontally(): void {
    this.flipHorizontal.update(value => !value);
    this.scheduleRender();
  }

  flipVertically(): void {
    this.flipVertical.update(value => !value);
    this.scheduleRender();
  }

  reset(): void {
    this.zoom.set(1);
    this.rotation.set(0);
    this.flipHorizontal.set(false);
    this.flipVertical.set(false);
    this.cropRect.set(null);
    this.isCropping.set(false);
    this.pan = { x: 0, y: 0 };
    this.scheduleRender();
  }

  async apply(): Promise<void> {
    if (!this.image) {
      return;
    }

    try {
      const { blob, width, height } = await this.exportTransformedImage();
      const state: EditorState = {
        rotation: this.rotation(),
        flipHorizontal: this.flipHorizontal(),
        flipVertical: this.flipVertical(),
        crop: this.cropRect() ? { ...this.cropRect()! } : null,
      };
      this.dialogRef.close({ blob, width, height, state });
    } catch (error) {
      console.error('Image export failed', error);
      this.dialogRef.close();
    }
  }

  cancel(): void {
    this.dialogRef.close();
  }

  private async initialize(): Promise<void> {
    try {
      this.image = await this.loadImage(this.data.blob);
      this.attachEventListeners();
      this.observeResize();
      this.scheduleRender();
    } catch (error) {
      console.error('Image editor initialization error', error);
      this.dialogRef.close();
    }
  }

  private attachEventListeners(): void {
    const canvas = this.canvas;
    canvas.addEventListener('pointerdown', this.onPointerDown);
    window.addEventListener('pointermove', this.onPointerMove);
    window.addEventListener('pointerup', this.onPointerUp);
    canvas.style.touchAction = 'none';
  }

  private observeResize(): void {
    this.resizeObserver = new ResizeObserver(() => {
      this.resizeCanvas();
      this.scheduleRender();
    });
    this.resizeObserver.observe(this.canvasContainerRef.nativeElement);
    this.resizeCanvas();
  }

  private resizeCanvas(): void {
    const canvas = this.canvas;
    const container = this.canvasContainerRef.nativeElement;
    const rect = container.getBoundingClientRect();
    const width = Math.max(200, Math.floor(rect.width));
    const height = Math.max(200, Math.floor(rect.height));
    if (canvas.width !== width || canvas.height !== height) {
      canvas.width = width;
      canvas.height = height;
    }
  }

  private loadImage(blob: Blob): Promise<ImageBitmap | HTMLImageElement> {
    if (typeof window !== 'undefined' && 'createImageBitmap' in window) {
      return createImageBitmap(blob).catch(async error => {
        console.warn('createImageBitmap failed, falling back to HTMLImageElement', error);
        return await this.loadImageFallback(blob);
      });
    }
    return this.loadImageFallback(blob);
  }

  private loadImageFallback(blob: Blob): Promise<HTMLImageElement> {
    return new Promise<HTMLImageElement>((resolve, reject) => {
      const url = URL.createObjectURL(blob);
      const img = new Image();
      img.onload = () => {
        URL.revokeObjectURL(url);
        resolve(img);
      };
      img.onerror = () => {
        URL.revokeObjectURL(url);
        reject(new Error('Не удалось загрузить изображение'));
      };
      img.src = url;
    });
  }

  private scheduleRender(): void {
    if (this.rafHandle !== null) {
      cancelAnimationFrame(this.rafHandle);
    }
    this.rafHandle = requestAnimationFrame(() => {
      this.rafHandle = null;
      this.render();
    });
  }

  private render(): void {
    if (!this.image) {
      return;
    }

    const canvas = this.canvas;
    const ctx = canvas.getContext('2d');
    if (!ctx) {
      return;
    }

    ctx.save();
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.restore();

    const matrix = this.getTransformMatrix();
    ctx.save();
    ctx.setTransform(matrix.a, matrix.b, matrix.c, matrix.d, matrix.e, matrix.f);
    ctx.drawImage(this.image, 0, 0);
    ctx.restore();

    const crop = this.cropRect();
    if (crop) {
      this.drawCropOverlay(ctx, crop);
    }
  }

  private drawCropOverlay(ctx: CanvasRenderingContext2D, crop: CropRect): void {
    const canvas = this.canvas;
    const corners = this.getCropCorners(crop).map(point => this.transformPoint(point));
    if (corners.length !== 4) {
      return;
    }

    ctx.save();
    ctx.fillStyle = 'rgba(0, 0, 0, 0.4)';
    ctx.beginPath();
    ctx.rect(0, 0, canvas.width, canvas.height);
    ctx.moveTo(corners[0].x, corners[0].y);
    for (let i = 1; i < corners.length; i++) {
      ctx.lineTo(corners[i].x, corners[i].y);
    }
    ctx.closePath();
    ctx.fill('evenodd');

    ctx.strokeStyle = '#ffffff';
    ctx.lineWidth = 2;
    ctx.setLineDash([6, 4]);
    ctx.beginPath();
    ctx.moveTo(corners[0].x, corners[0].y);
    for (let i = 1; i < corners.length; i++) {
      ctx.lineTo(corners[i].x, corners[i].y);
    }
    ctx.closePath();
    ctx.stroke();
    ctx.restore();
  }

  private getCropCorners(crop: CropRect): Array<{ x: number; y: number }> {
    return [
      { x: crop.x, y: crop.y },
      { x: crop.x + crop.width, y: crop.y },
      { x: crop.x + crop.width, y: crop.y + crop.height },
      { x: crop.x, y: crop.y + crop.height },
    ];
  }

  private onPointerDown = (event: PointerEvent): void => {
    if (!this.image) {
      return;
    }
    event.preventDefault();
    this.canvas.setPointerCapture?.(event.pointerId);
    this.pointerActive = true;
    this.activePointerId = event.pointerId;
    this.lastPointerPosition = { x: event.clientX, y: event.clientY };

    if (this.isCropping()) {
      const point = this.screenToImage(event);
      if (point) {
        this.cropStart = point;
        this.cropRect.set({ x: point.x, y: point.y, width: 0, height: 0 });
      }
    }
  };

  private onPointerMove = (event: PointerEvent): void => {
    if (!this.pointerActive || this.activePointerId !== event.pointerId) {
      return;
    }
    event.preventDefault();

    if (this.isCropping()) {
      this.updateCrop(event);
      return;
    }

    if (!this.lastPointerPosition) {
      return;
    }

    const deltaX = event.clientX - this.lastPointerPosition.x;
    const deltaY = event.clientY - this.lastPointerPosition.y;
    this.pan.x += deltaX;
    this.pan.y += deltaY;
    this.lastPointerPosition = { x: event.clientX, y: event.clientY };
    this.scheduleRender();
  };

  private onPointerUp = (event: PointerEvent): void => {
    if (this.activePointerId !== event.pointerId) {
      return;
    }
    this.canvas.releasePointerCapture?.(event.pointerId);
    this.pointerActive = false;
    this.activePointerId = null;
    this.lastPointerPosition = null;

    if (this.isCropping()) {
      this.finalizeCrop();
    }
  };

  private updateCrop(event: PointerEvent): void {
    if (!this.cropStart) {
      return;
    }
    const current = this.screenToImage(event);
    if (!current) {
      return;
    }
    const x = Math.min(this.cropStart.x, current.x);
    const y = Math.min(this.cropStart.y, current.y);
    const width = Math.abs(current.x - this.cropStart.x);
    const height = Math.abs(current.y - this.cropStart.y);
    const normalized = this.normalizeCrop({ x, y, width, height });
    this.cropRect.set(normalized);
    this.scheduleRender();
  }

  private finalizeCrop(): void {
    const crop = this.cropRect();
    if (!crop) {
      return;
    }
    if (crop.width < 2 || crop.height < 2) {
      this.cropRect.set(null);
    }
    this.cropStart = null;
    this.scheduleRender();
  }

  private normalizeCrop(crop: CropRect): CropRect {
    const width = this.imageWidth;
    const height = this.imageHeight;
    const x = Math.max(0, Math.min(crop.x, width));
    const y = Math.max(0, Math.min(crop.y, height));
    const maxWidth = width - x;
    const maxHeight = height - y;
    return {
      x,
      y,
      width: Math.min(crop.width, maxWidth),
      height: Math.min(crop.height, maxHeight),
    };
  }

  private screenToImage(event: PointerEvent): { x: number; y: number } | null {
    const rect = this.canvas.getBoundingClientRect();
    const x = event.clientX - rect.left;
    const y = event.clientY - rect.top;
    const matrix = this.getTransformMatrix();
    const inverse = matrix.inverse();
    const point = inverse.transformPoint(new DOMPoint(x, y));
    if (Number.isFinite(point.x) && Number.isFinite(point.y)) {
      return { x: point.x, y: point.y };
    }
    return null;
  }

  private transformPoint(point: { x: number; y: number }): { x: number; y: number } {
    const matrix = this.getTransformMatrix();
    const result = matrix.transformPoint(new DOMPoint(point.x, point.y));
    return { x: result.x, y: result.y };
  }

  private getTransformMatrix(): DOMMatrix {
    const canvas = this.canvas;
    const zoom = this.zoom();
    const rotation = this.rotation();
    const flipH = this.flipHorizontal() ? -1 : 1;
    const flipV = this.flipVertical() ? -1 : 1;
    const cx = canvas.width / 2 + this.pan.x;
    const cy = canvas.height / 2 + this.pan.y;

    let matrix = new DOMMatrix();
    matrix = matrix.translate(cx, cy);
    matrix = matrix.rotate(rotation);
    matrix = matrix.scale(zoom * flipH, zoom * flipV);
    matrix = matrix.translate(-this.imageWidth / 2, -this.imageHeight / 2);
    return matrix;
  }

  private async exportTransformedImage(): Promise<{ blob: Blob; width: number; height: number }> {
    if (!this.image) {
      throw new Error('Изображение не загружено');
    }

    const crop = this.cropRect();
    const rotation = this.rotation();
    const flipH = this.flipHorizontal();
    const flipV = this.flipVertical();

    const sourceRect: CropRect = crop ?? { x: 0, y: 0, width: this.imageWidth, height: this.imageHeight };
    const angle = ((rotation % 360) + 360) % 360;
    const radians = (angle * Math.PI) / 180;

    const needsSwap = angle === 90 || angle === 270;
    const outputWidth = needsSwap ? sourceRect.height : sourceRect.width;
    const outputHeight = needsSwap ? sourceRect.width : sourceRect.height;

    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(outputWidth));
    canvas.height = Math.max(1, Math.round(outputHeight));
    const ctx = canvas.getContext('2d');
    if (!ctx) {
      throw new Error('Canvas недоступен');
    }

    ctx.save();
    ctx.translate(canvas.width / 2, canvas.height / 2);
    ctx.rotate(radians);
    ctx.scale(flipH ? -1 : 1, flipV ? -1 : 1);
    ctx.translate(-sourceRect.width / 2, -sourceRect.height / 2);
    ctx.drawImage(
      this.image,
      sourceRect.x,
      sourceRect.y,
      sourceRect.width,
      sourceRect.height,
      0,
      0,
      sourceRect.width,
      sourceRect.height,
    );
    ctx.restore();

    const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/png'));
    if (!blob) {
      throw new Error('Не удалось получить PNG изображение');
    }

    return { blob, width: canvas.width, height: canvas.height };
  }
}
