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

type CropHandleType =
  | 'top-left'
  | 'top-right'
  | 'bottom-left'
  | 'bottom-right'
  | 'top'
  | 'right'
  | 'bottom'
  | 'left'
  | 'move';

interface ActiveCropHandle {
  type: CropHandleType;
  startRect: CropRect;
  startPointer: { x: number; y: number };
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

  private static readonly MAX_ZOOM = 8;
  private static readonly ZOOM_EPSILON = 1e-3;
  private static readonly PAN_EPSILON = 1e-2;
  private static readonly MIN_CROP_SIZE = 5;

  private readonly baseZoomOptions: number[] = [1, 2, 4, ImageEditorDialogComponent.MAX_ZOOM];

  readonly zoom: WritableSignal<number> = signal(1);
  readonly zoomOptions: Signal<readonly number[]> = computed(() => {
    const options = [...this.baseZoomOptions];
    const current = this.zoom();
    if (!options.some(option => this.areZoomValuesEqual(option, current))) {
      options.push(current);
      options.sort((a, b) => a - b);
    }
    return options;
  });
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

  protected readonly trackZoomOption = (_: number, option: number): number => option;

  private image: ImageBitmap | HTMLImageElement | null = null;
  private resizeObserver?: ResizeObserver;
  private pointerActive = false;
  private activePointerId: number | null = null;
  private lastPointerPosition: { x: number; y: number } | null = null;
  private cropStart: { x: number; y: number } | null = null;
  private activeCropHandle: ActiveCropHandle | null = null;
  private pan: { x: number; y: number } = { x: 0, y: 0 };
  private rafHandle: number | null = null;
  private readonly minZoom: WritableSignal<number> = signal(1);

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
    canvas.removeEventListener('wheel', this.onWheel);

    if (this.image && 'close' in this.image) {
      try {
        (this.image as ImageBitmap).close();
      } catch {
        /* noop */
      }
    }
  }

  toggleCrop(): void {
    const shouldEnable = !this.isCropping();
    this.isCropping.set(shouldEnable);
    this.pointerActive = false;
    this.activePointerId = null;
    this.lastPointerPosition = null;
    if (!shouldEnable) {
      this.cropStart = null;
      this.activeCropHandle = null;
    }
    this.scheduleRender();
  }

  applyCropSelection(): void {
    const crop = this.cropRect();
    this.isCropping.set(false);
    this.cropStart = null;
    this.activeCropHandle = null;
    if (!crop) {
      this.scheduleRender();
      return;
    }
    this.focusOnCropArea(crop);
  }

  onZoomChange(event: MatButtonToggleChange): void {
    const value = event.value as number;
    if (typeof value !== 'number') {
      return;
    }
    this.updateZoom(value, { resetPan: true });
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
    this.updateZoom(1, { resetPan: true, schedule: false });
    this.rotation.set(0);
    this.flipHorizontal.set(false);
    this.flipVertical.set(false);
    this.cropRect.set(null);
    this.isCropping.set(false);
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
      this.updateZoomLimits();
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
    canvas.addEventListener('wheel', this.onWheel, { passive: false });
    canvas.style.touchAction = 'none';
  }

  private observeResize(): void {
    this.resizeObserver = new ResizeObserver(() => {
      this.resizeCanvas();
      const zoomAdjusted = this.updateZoomLimits();
      if (zoomAdjusted) {
        this.scheduleRender();
        return;
      }
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
    this.updateZoomLimits();
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

    this.drawCropHandles(ctx, corners);
    ctx.restore();
  }

  private drawCropHandles(
    ctx: CanvasRenderingContext2D,
    corners: Array<{ x: number; y: number }>,
  ): void {
    const [topLeft, topRight, bottomRight, bottomLeft] = corners;
    const midTop = { x: (topLeft.x + topRight.x) / 2, y: (topLeft.y + topRight.y) / 2 };
    const midRight = {
      x: (topRight.x + bottomRight.x) / 2,
      y: (topRight.y + bottomRight.y) / 2,
    };
    const midBottom = {
      x: (bottomLeft.x + bottomRight.x) / 2,
      y: (bottomLeft.y + bottomRight.y) / 2,
    };
    const midLeft = { x: (topLeft.x + bottomLeft.x) / 2, y: (topLeft.y + bottomLeft.y) / 2 };

    const handles = [topLeft, topRight, bottomRight, bottomLeft, midTop, midRight, midBottom, midLeft];
    const size = 12;

    ctx.fillStyle = '#ffffff';
    ctx.strokeStyle = '#3f51b5';
    ctx.setLineDash([]);
    handles.forEach(point => {
      ctx.beginPath();
      ctx.rect(point.x - size / 2, point.y - size / 2, size, size);
      ctx.fill();
      ctx.stroke();
    });
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
      if (!point) {
        return;
      }

      const crop = this.cropRect();
      if (crop) {
        const handleType = this.getCropHandleType(point, crop);
        if (handleType) {
          this.cropStart = null;
          this.activeCropHandle = {
            type: handleType,
            startRect: { ...crop },
            startPointer: point,
          };
          return;
        }
      }

      this.activeCropHandle = null;
      this.cropStart = point;
      this.cropRect.set({ x: point.x, y: point.y, width: 0, height: 0 });
      this.scheduleRender();
    }
  };

  private onPointerMove = (event: PointerEvent): void => {
    if (!this.pointerActive || this.activePointerId !== event.pointerId) {
      return;
    }
    event.preventDefault();

    if (this.isCropping()) {
      if (this.activeCropHandle) {
        this.resizeCrop(event);
        return;
      }
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

  private onWheel = (event: WheelEvent): void => {
    if (!this.image) {
      return;
    }

    event.preventDefault();

    const currentZoom = this.zoom();
    const factor = Math.exp(-event.deltaY / 300);
    const nextZoom = this.clampZoom(currentZoom * factor);

    if (!this.areZoomValuesEqual(nextZoom, currentZoom)) {
      this.updateZoom(nextZoom);
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

  formatZoomLabel(option: number): string {
    const format = (value: number): string => {
      if (this.areZoomValuesEqual(value, Math.round(value))) {
        return Math.round(value).toString();
      }
      return value >= 10 ? value.toFixed(0) : value.toFixed(2);
    };

    if (option >= 1) {
      return `1:${format(option)}`;
    }
    return `${format(1 / option)}:1`;
  }

  private updateZoom(value: number, options: { resetPan?: boolean; schedule?: boolean } = {}): boolean {
    const { resetPan = false, schedule = true } = options;
    const clamped = this.clampZoom(value);

    let panChanged = false;
    if (resetPan && (this.pan.x !== 0 || this.pan.y !== 0)) {
      this.pan = { x: 0, y: 0 };
      panChanged = true;
    } else if (resetPan) {
      this.pan = { x: 0, y: 0 };
    }

    const zoomChanged = !this.areZoomValuesEqual(this.zoom(), clamped);
    if (zoomChanged) {
      this.zoom.set(clamped);
    }

    if ((zoomChanged || panChanged) && schedule) {
      this.scheduleRender();
    }

    return zoomChanged || panChanged;
  }

  private updateZoomLimits(): boolean {
    const minZoom = this.calculateMinZoom();
    if (!Number.isFinite(minZoom) || minZoom <= 0) {
      return false;
    }

    if (!this.areZoomValuesEqual(this.minZoom(), minZoom)) {
      this.minZoom.set(minZoom);
    }

    return this.updateZoom(this.zoom(), { schedule: false });
  }

  private calculateMinZoom(): number {
    if (!this.image) {
      return 1;
    }
    const canvas = this.canvas;
    if (canvas.width === 0 || canvas.height === 0) {
      return 1;
    }

    const widthRatio = canvas.width / this.imageWidth;
    const heightRatio = canvas.height / this.imageHeight;
    const fitScale = Math.min(widthRatio, heightRatio);
    const minZoom = Math.min(1, fitScale);
    return Math.max(minZoom, 0.01);
  }

  private clampZoom(value: number): number {
    if (!Number.isFinite(value)) {
      return this.minZoom();
    }
    const min = this.minZoom();
    const max = ImageEditorDialogComponent.MAX_ZOOM;
    const clamped = Math.min(max, Math.max(min, value));
    return this.snapToBaseZoom(clamped);
  }

  private snapToBaseZoom(value: number): number {
    for (const option of this.baseZoomOptions) {
      if (this.areZoomValuesEqual(option, value)) {
        return option;
      }
    }
    return value;
  }

  private areZoomValuesEqual(a: number, b: number): boolean {
    return Math.abs(a - b) <= ImageEditorDialogComponent.ZOOM_EPSILON;
  }

  private resizeCrop(event: PointerEvent): void {
    if (!this.activeCropHandle) {
      return;
    }
    const point = this.screenToImage(event);
    if (!point) {
      return;
    }

    const { startRect, startPointer, type } = this.activeCropHandle;
    const dx = point.x - startPointer.x;
    const dy = point.y - startPointer.y;
    let rect: CropRect = { ...startRect };

    switch (type) {
      case 'move':
        rect = this.translateCrop(startRect, dx, dy);
        break;
      case 'top-left':
        rect = { x: startRect.x + dx, y: startRect.y + dy, width: startRect.width - dx, height: startRect.height - dy };
        break;
      case 'top-right':
        rect = { x: startRect.x, y: startRect.y + dy, width: startRect.width + dx, height: startRect.height - dy };
        break;
      case 'bottom-left':
        rect = { x: startRect.x + dx, y: startRect.y, width: startRect.width - dx, height: startRect.height + dy };
        break;
      case 'bottom-right':
        rect = { x: startRect.x, y: startRect.y, width: startRect.width + dx, height: startRect.height + dy };
        break;
      case 'top':
        rect = { x: startRect.x, y: startRect.y + dy, width: startRect.width, height: startRect.height - dy };
        break;
      case 'bottom':
        rect = { x: startRect.x, y: startRect.y, width: startRect.width, height: startRect.height + dy };
        break;
      case 'left':
        rect = { x: startRect.x + dx, y: startRect.y, width: startRect.width - dx, height: startRect.height };
        break;
      case 'right':
        rect = { x: startRect.x, y: startRect.y, width: startRect.width + dx, height: startRect.height };
        break;
    }

    rect = this.enforceMinimumCropSize(rect, type, startRect);
    this.cropRect.set(this.normalizeCrop(rect));
    this.scheduleRender();
  }

  private enforceMinimumCropSize(rect: CropRect, type: CropHandleType, startRect: CropRect): CropRect {
    if (type === 'move') {
      return rect;
    }

    const min = ImageEditorDialogComponent.MIN_CROP_SIZE;
    const anchorLeft = startRect.x;
    const anchorRight = startRect.x + startRect.width;
    const anchorTop = startRect.y;
    const anchorBottom = startRect.y + startRect.height;

    let left = rect.x;
    let top = rect.y;
    let right = rect.x + rect.width;
    let bottom = rect.y + rect.height;

    switch (type) {
      case 'top-left':
        right = anchorRight;
        bottom = anchorBottom;
        left = Math.min(left, right - min);
        top = Math.min(top, bottom - min);
        break;
      case 'top-right':
        left = anchorLeft;
        bottom = anchorBottom;
        right = Math.max(right, left + min);
        top = Math.min(top, bottom - min);
        break;
      case 'bottom-left':
        right = anchorRight;
        top = anchorTop;
        left = Math.min(left, right - min);
        bottom = Math.max(bottom, top + min);
        break;
      case 'bottom-right':
        left = anchorLeft;
        top = anchorTop;
        right = Math.max(right, left + min);
        bottom = Math.max(bottom, top + min);
        break;
      case 'top':
        left = anchorLeft;
        right = anchorRight;
        bottom = anchorBottom;
        top = Math.min(top, bottom - min);
        break;
      case 'bottom':
        left = anchorLeft;
        right = anchorRight;
        top = anchorTop;
        bottom = Math.max(bottom, top + min);
        break;
      case 'left':
        right = anchorRight;
        top = anchorTop;
        bottom = anchorBottom;
        left = Math.min(left, right - min);
        break;
      case 'right':
        left = anchorLeft;
        top = anchorTop;
        bottom = anchorBottom;
        right = Math.max(right, left + min);
        break;
    }

    return { x: left, y: top, width: right - left, height: bottom - top };
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
    this.activeCropHandle = null;
    this.scheduleRender();
  }

  private focusOnCropArea(crop: CropRect): void {
    if (!this.image) {
      this.scheduleRender();
      return;
    }

    const zoomForCrop = this.calculateZoomForCrop(crop);
    this.updateZoom(zoomForCrop, { schedule: false });
    const appliedZoom = this.zoom();
    const targetPan = this.calculatePanForCrop(crop, appliedZoom);
    const safePan = {
      x: Number.isFinite(targetPan.x) ? targetPan.x : this.pan.x,
      y: Number.isFinite(targetPan.y) ? targetPan.y : this.pan.y,
    };
    const panChanged = !this.arePanValuesEqual(this.pan, safePan);
    if (panChanged) {
      this.pan = safePan;
    }

    this.scheduleRender();
  }

  private calculateZoomForCrop(crop: CropRect): number {
    const canvas = this.canvas;
    if (canvas.width === 0 || canvas.height === 0) {
      return this.zoom();
    }

    const angle = ((this.rotation() % 360) + 360) % 360;
    const radians = (angle * Math.PI) / 180;
    const cropWidth = crop.width;
    const cropHeight = crop.height;

    const rotatedWidth = Math.abs(cropWidth * Math.cos(radians)) + Math.abs(cropHeight * Math.sin(radians));
    const rotatedHeight = Math.abs(cropWidth * Math.sin(radians)) + Math.abs(cropHeight * Math.cos(radians));

    if (rotatedWidth <= 0 || rotatedHeight <= 0) {
      return this.zoom();
    }

    const widthRatio = canvas.width / rotatedWidth;
    const heightRatio = canvas.height / rotatedHeight;
    const zoomToFit = Math.min(widthRatio, heightRatio);

    if (!Number.isFinite(zoomToFit) || zoomToFit <= 0) {
      return this.zoom();
    }

    return zoomToFit;
  }

  private calculatePanForCrop(crop: CropRect, zoom: number): { x: number; y: number } {
    const matrix = this.buildTransformMatrix(zoom, { x: 0, y: 0 });
    const centerPoint = new DOMPoint(crop.x + crop.width / 2, crop.y + crop.height / 2);
    const transformed = matrix.transformPoint(centerPoint);
    const canvas = this.canvas;
    return {
      x: canvas.width / 2 - transformed.x,
      y: canvas.height / 2 - transformed.y,
    };
  }

  private arePanValuesEqual(a: { x: number; y: number }, b: { x: number; y: number }): boolean {
    return (
      Math.abs(a.x - b.x) <= ImageEditorDialogComponent.PAN_EPSILON &&
      Math.abs(a.y - b.y) <= ImageEditorDialogComponent.PAN_EPSILON
    );
  }

  private normalizeCrop(crop: CropRect): CropRect {
    let { x, y, width, height } = crop;

    if (width < 0) {
      x += width;
      width = Math.abs(width);
    }
    if (height < 0) {
      y += height;
      height = Math.abs(height);
    }

    const maxWidth = this.imageWidth;
    const maxHeight = this.imageHeight;

    width = Math.min(width, maxWidth);
    height = Math.min(height, maxHeight);

    x = Math.min(Math.max(0, x), Math.max(0, maxWidth - width));
    y = Math.min(Math.max(0, y), Math.max(0, maxHeight - height));

    if (x + width > maxWidth) {
      x = Math.max(0, maxWidth - width);
    }
    if (y + height > maxHeight) {
      y = Math.max(0, maxHeight - height);
    }

    return { x, y, width, height };
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

  private getCropHandleType(pointImage: { x: number; y: number }, crop: CropRect): CropHandleType | null {
    const matrix = this.getTransformMatrix();
    const pScreen = matrix.transformPoint(new DOMPoint(pointImage.x, pointImage.y));

    const cornersImg = this.getCropCorners(crop);
    const [tl, tr, br, bl] = cornersImg.map(pt => {
      const s = matrix.transformPoint(new DOMPoint(pt.x, pt.y));
      return { x: s.x, y: s.y };
    });

    const mid = (a: { x: number; y: number }, b: { x: number; y: number }) => ({
      x: (a.x + b.x) / 2,
      y: (a.y + b.y) / 2,
    });
    const midTop = mid(tl, tr);
    const midRight = mid(tr, br);
    const midBottom = mid(bl, br);
    const midLeft = mid(tl, bl);

    const HANDLE_HALF = 6;

    const hitBox = (h: { x: number; y: number }) =>
      Math.abs(pScreen.x - h.x) <= HANDLE_HALF && Math.abs(pScreen.y - h.y) <= HANDLE_HALF;

    if (hitBox(tl)) {
      return 'top-left';
    }
    if (hitBox(tr)) {
      return 'top-right';
    }
    if (hitBox(br)) {
      return 'bottom-right';
    }
    if (hitBox(bl)) {
      return 'bottom-left';
    }

    const EDGE_HALF = 8;

    const hitEdge = (a: { x: number; y: number }, b: { x: number; y: number }) => {
      const vx = b.x - a.x;
      const vy = b.y - a.y;
      const wx = pScreen.x - a.x;
      const wy = pScreen.y - a.y;
      const len2 = vx * vx + vy * vy || 1;
      const t = Math.max(0, Math.min(1, (wx * vx + wy * vy) / len2));
      const px = a.x + t * vx;
      const py = a.y + t * vy;
      const dx = pScreen.x - px;
      const dy = pScreen.y - py;
      return Math.hypot(dx, dy) <= EDGE_HALF;
    };

    if (hitEdge(tl, tr)) {
      return 'top';
    }
    if (hitEdge(tr, br)) {
      return 'right';
    }
    if (hitEdge(bl, br)) {
      return 'bottom';
    }
    if (hitEdge(tl, bl)) {
      return 'left';
    }

    const left = crop.x;
    const right = crop.x + crop.width;
    const top = crop.y;
    const bottom = crop.y + crop.height;
    if (
      pointImage.x > left &&
      pointImage.x < right &&
      pointImage.y > top &&
      pointImage.y < bottom
    ) {
      return 'move';
    }
    return null;
  }

  private translateCrop(startRect: CropRect, dx: number, dy: number): CropRect {
    const maxWidth = this.imageWidth;
    const maxHeight = this.imageHeight;
    const newX = Math.min(Math.max(0, startRect.x + dx), Math.max(0, maxWidth - startRect.width));
    const newY = Math.min(Math.max(0, startRect.y + dy), Math.max(0, maxHeight - startRect.height));
    return { x: newX, y: newY, width: startRect.width, height: startRect.height };
  }

  private getTransformMatrix(): DOMMatrix {
    return this.buildTransformMatrix(this.zoom(), this.pan);
  }

  private buildTransformMatrix(zoom: number, pan: { x: number; y: number }): DOMMatrix {
    const canvas = this.canvas;
    const rotation = this.rotation();
    const flipH = this.flipHorizontal() ? -1 : 1;
    const flipV = this.flipVertical() ? -1 : 1;
    const cx = canvas.width / 2 + pan.x;
    const cy = canvas.height / 2 + pan.y;

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
