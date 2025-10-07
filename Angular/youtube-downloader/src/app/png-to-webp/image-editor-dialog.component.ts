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
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';

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
  // для ручек — координата САМОЙ ручки (угла/середины ребра), а не точки клика
  startPointer: { x: number; y: number };
  // смещение клика от центра ручки, чтобы не было «скачка»
  pointerOffset?: { ox: number; oy: number };
}

@Component({
  selector: 'app-image-editor-dialog',
  standalone: true,
  imports: [
    CommonModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatSelectModule,
    MatTooltipModule,
  ],
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
  private static readonly NEW_CROP_DRAG_THRESHOLD = 3; // px, чтобы новый кроп не ставился по одиночному клику
  private static readonly FULLSCREEN_PANEL_CLASS = 'image-editor-dialog-fullscreen';

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
    if (!crop) return 'Полный размер';
    return `${Math.round(crop.width)} × ${Math.round(crop.height)} px`;
  });

  readonly isCropping: WritableSignal<boolean> = signal(false);
  readonly isFullscreen: WritableSignal<boolean> = signal(false);
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

  // новый кроп
  private newCropStartImage: { x: number; y: number } | null = null;
  private pointerDownScreen: { x: number; y: number } | null = null;
  private dragStarted = false;

  // подсветка/курсор
  private hoverHandle: CropHandleType | null = null;

  // HiDPI support
  private devicePixelRatio = 1;
  private canvasCssWidth = 0;
  private canvasCssHeight = 0;

  // DEBUG: отрисовывать центры ручек и рамки их хит-боксов
  private readonly DEBUG_HIT = true;

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
    if (this.isFullscreen()) {
      this.dialogRef.removePanelClass(ImageEditorDialogComponent.FULLSCREEN_PANEL_CLASS);
      this.dialogRef.updateSize();
      this.dialogRef.updatePosition();
    }

    if (this.rafHandle !== null) {
      cancelAnimationFrame(this.rafHandle);
      this.rafHandle = null;
    }

    this.resizeObserver?.disconnect();

    const canvas = this.canvas;
    canvas.removeEventListener('pointerdown', this.onPointerDown);
    canvas.removeEventListener('pointerleave', this.onPointerLeave);
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

  toggleFullscreen(): void {
    const next = !this.isFullscreen();
    this.isFullscreen.set(next);

    if (next) {
      this.dialogRef.addPanelClass(ImageEditorDialogComponent.FULLSCREEN_PANEL_CLASS);
      this.dialogRef.updateSize('100vw', '100vh');
      this.dialogRef.updatePosition({ top: '0', left: '0' });
    } else {
      this.dialogRef.removePanelClass(ImageEditorDialogComponent.FULLSCREEN_PANEL_CLASS);
      this.dialogRef.updateSize();
      this.dialogRef.updatePosition();
    }

    // Размеры контейнера меняются вместе с диалогом — обновим холст
    requestAnimationFrame(() => {
      this.resizeCanvas();
      this.scheduleRender();
    });
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
      this.newCropStartImage = null;
      this.pointerDownScreen = null;
      this.dragStarted = false;
      this.hoverHandle = null;
      this.updateCursorForHandle(null);
    } else {
      this.updateCursorForHandle('move');
    }
    this.scheduleRender();
  }

  applyCropSelection(): void {
    const crop = this.cropRect();
    this.isCropping.set(false);
    this.cropStart = null;
    this.activeCropHandle = null;
    this.newCropStartImage = null;
    this.pointerDownScreen = null;
    this.dragStarted = false;
    this.hoverHandle = null;
    this.updateCursorForHandle(null);

    if (!crop) {
      this.scheduleRender();
      return;
    }
    this.focusOnCropArea(crop);
  }

  onZoomSelectionChange(value: number): void {
    if (typeof value !== 'number') return;
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
    this.newCropStartImage = null;
    this.pointerDownScreen = null;
    this.dragStarted = false;
    this.hoverHandle = null;
    this.updateCursorForHandle(null);
    this.scheduleRender();
  }

  async apply(): Promise<void> {
    if (!this.image) return;

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
    canvas.addEventListener('pointerleave', this.onPointerLeave);
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

    // размеры области без учета рамок, чтобы холст не обрезался справа/снизу
    let width = Math.floor(container.clientWidth);
    let height = Math.floor(container.clientHeight);

    if (!width || !height) {
      const rect = container.getBoundingClientRect();
      width = Math.floor(rect.width);
      height = Math.floor(rect.height);
    }

    // CSS размер
    this.canvasCssWidth = Math.max(200, width);
    this.canvasCssHeight = Math.max(200, height);

    // HiDPI backing store
    this.devicePixelRatio = window.devicePixelRatio || 1;
    canvas.width = Math.max(1, Math.round(this.canvasCssWidth * this.devicePixelRatio));
    canvas.height = Math.max(1, Math.round(this.canvasCssHeight * this.devicePixelRatio));
    canvas.style.width = `${this.canvasCssWidth}px`;
    canvas.style.height = `${this.canvasCssHeight}px`;

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
    if (!this.image) return;

    const canvas = this.canvas;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    // очистка в device px
    ctx.save();
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.restore();

    // масштаб до CSS пикселей (вся дальнейшая математика — в CSS px)
    ctx.save();
    ctx.setTransform(this.devicePixelRatio, 0, 0, this.devicePixelRatio, 0, 0);

    const matrix = this.getTransformMatrix(); // уже в CSS px
    ctx.setTransform(
      matrix.a * this.devicePixelRatio,
      matrix.b * this.devicePixelRatio,
      matrix.c * this.devicePixelRatio,
      matrix.d * this.devicePixelRatio,
      matrix.e * this.devicePixelRatio,
      matrix.f * this.devicePixelRatio,
    );

    ctx.drawImage(this.image, 0, 0);
    ctx.restore();

    const crop = this.cropRect();
    if (crop) {
      // оверлеи рисуем в CSS px
      const overlayCtx = canvas.getContext('2d')!;
      overlayCtx.save();
      overlayCtx.setTransform(this.devicePixelRatio, 0, 0, this.devicePixelRatio, 0, 0);
      this.drawCropOverlay(overlayCtx, crop);
      overlayCtx.restore();
    }
  }

  private drawCropOverlay(ctx: CanvasRenderingContext2D, crop: CropRect): void {
    const corners = this.getCropCorners(crop).map(point => this.transformPoint(point)); // экранные (CSS px)

    ctx.save();
    // затемнение фона
    ctx.fillStyle = 'rgba(0, 0, 0, 0.4)';
    ctx.beginPath();
    ctx.rect(0, 0, this.canvasCssWidth, this.canvasCssHeight);
    ctx.moveTo(corners[0].x, corners[0].y);
    for (let i = 1; i < corners.length; i++) ctx.lineTo(corners[i].x, corners[i].y);
    ctx.closePath();
    ctx.fill('evenodd');

    // штриховая рамка кропа
    ctx.strokeStyle = '#ffffff';
    ctx.lineWidth = 2;
    ctx.setLineDash([6, 4]);
    ctx.beginPath();
    ctx.moveTo(corners[0].x, corners[0].y);
    for (let i = 1; i < corners.length; i++) ctx.lineTo(corners[i].x, corners[i].y);
    ctx.closePath();
    ctx.stroke();

    this.drawCropHandles(ctx, corners);
    ctx.restore();

    // DEBUG: хит-боксы поверх
    if (this.DEBUG_HIT) {
      const boxes = this.getHandleHitBoxesScreen(crop);
      ctx.save();
      ctx.setLineDash([3, 3]);
      ctx.lineWidth = 1;
      for (const b of boxes) {
        ctx.strokeStyle = this.hoverHandle === b.type ? '#ff3d71' : 'rgba(255,255,255,0.5)';
        ctx.strokeRect(b.x - b.hw, b.y - b.hh, b.hw * 2, b.hh * 2);
      }
      ctx.restore();
    }
  }

  private drawCropHandles(
    ctx: CanvasRenderingContext2D,
    corners: Array<{ x: number; y: number }>,
  ): void {
    const [topLeft, topRight, bottomRight, bottomLeft] = corners;
    const midTop = { x: (topLeft.x + topRight.x) / 2, y: (topLeft.y + topRight.y) / 2 };
    const midRight = { x: (topRight.x + bottomRight.x) / 2, y: (topRight.y + bottomRight.y) / 2 };
    const midBottom = { x: (bottomLeft.x + bottomRight.x) / 2, y: (bottomLeft.y + bottomRight.y) / 2 };
    const midLeft = { x: (topLeft.x + bottomLeft.x) / 2, y: (topLeft.y + bottomLeft.y) / 2 };

    const typedHandles: Array<{ x: number; y: number; type: CropHandleType }> = [
      { ...topLeft, type: 'top-left' },
      { ...topRight, type: 'top-right' },
      { ...bottomRight, type: 'bottom-right' },
      { ...bottomLeft, type: 'bottom-left' },
      { ...midTop, type: 'top' },
      { ...midRight, type: 'right' },
      { ...midBottom, type: 'bottom' },
      { ...midLeft, type: 'left' },
    ];

    const size = 16;
    ctx.setLineDash([]);

    for (const h of typedHandles) {
      const isActive = this.activeCropHandle?.type === h.type;
      const isHover = this.hoverHandle === h.type;

      let fill = '#ffffff';
      let stroke = '#3f51b5';
      let lineW = 2;

      if (isActive) {
        fill = '#e8f0ff';
        stroke = '#2962ff';
        lineW = 3;
      } else if (isHover) {
        fill = '#fff3f8';
        stroke = '#ff3d71';
        lineW = 3;
      }

      ctx.save();
      if (isHover) {
        ctx.shadowColor = '#ff3d71';
        ctx.shadowBlur = 12;
      }
      ctx.lineWidth = lineW;
      ctx.strokeStyle = stroke;
      ctx.fillStyle = fill;
      ctx.beginPath();
      ctx.rect(h.x - size / 2, h.y - size / 2, size, size);
      ctx.fill();
      ctx.stroke();
      ctx.restore();

      if (this.DEBUG_HIT) {
        // центр ручки
        ctx.save();
        ctx.beginPath();
        ctx.arc(h.x, h.y, 2.5, 0, Math.PI * 2);
        ctx.fillStyle = '#ff3d71';
        ctx.fill();
        ctx.restore();
      }
    }
  }

  private getCropCorners(crop: CropRect): Array<{ x: number; y: number }> {
    return [
      { x: crop.x, y: crop.y },
      { x: crop.x + crop.width, y: crop.y },
      { x: crop.x + crop.width, y: crop.y + crop.height },
      { x: crop.x, y: crop.y + crop.height },
    ];
  }

  // ——— События указателя ———

  private onPointerDown = (event: PointerEvent): void => {
    if (!this.image) return;

    event.preventDefault();
    this.canvas.setPointerCapture?.(event.pointerId);
    this.pointerActive = true;
    this.activePointerId = event.pointerId;
    this.lastPointerPosition = { x: event.clientX, y: event.clientY };

    if (!this.isCropping()) return;

    const pointImg = this.screenToImage(event);
    if (!pointImg) return;

    const crop = this.cropRect();
    if (crop) {
      // Хит-тест строго в экранных координатах
      const screenPt = this.getScreenPoint(event);
      const handleType = this.getCropHandleTypeScreen(screenPt, crop);
      if (handleType) {
        const corners = this.getCropCorners(crop);
        const [tl, tr, br, bl] = corners;
        const mid = (a: { x: number; y: number }, b: { x: number; y: number }) => ({
          x: (a.x + b.x) / 2,
          y: (a.y + b.y) / 2,
        });

        // точная координата ручки (в координатах изображения)
        const handlePointImage =
          handleType === 'top-left' ? tl :
          handleType === 'top-right' ? tr :
          handleType === 'bottom-right' ? br :
          handleType === 'bottom-left' ? bl :
          handleType === 'top' ? mid(tl, tr) :
          handleType === 'right' ? mid(tr, br) :
          handleType === 'bottom' ? mid(bl, br) :
          /* left */           mid(tl, bl);

        const pointerOffset = { ox: pointImg.x - handlePointImage.x, oy: pointImg.y - handlePointImage.y };

        this.cropStart = null;
        this.activeCropHandle = {
          type: handleType,
          startRect: { ...crop },
          startPointer: handlePointImage,
          pointerOffset,
        };
        this.hoverHandle = handleType;
        this.updateCursorForHandle(handleType);
        return;
      }

      // внутри прямоугольника — перенос
      if (pointImg.x > crop.x && pointImg.x < crop.x + crop.width && pointImg.y > crop.y && pointImg.y < crop.y + crop.height) {
        this.cropStart = null;
        this.activeCropHandle = { type: 'move', startRect: { ...crop }, startPointer: pointImg };
        this.hoverHandle = 'move';
        this.updateCursorForHandle('move');
        return;
      }
    }

    // вне области — новый прямоугольник
    this.activeCropHandle = null;
    this.cropStart = null;
    this.newCropStartImage = pointImg;
    this.pointerDownScreen = this.getScreenPoint(event);
    this.dragStarted = false;
    this.hoverHandle = null;
    this.updateCursorForHandle(null);
  };

  private onPointerMove = (event: PointerEvent): void => {
    event.preventDefault();

    // ХОВЕР — всегда (даже без нажатия)
    if (this.isCropping()) {
      const crop = this.cropRect();
      if (crop) {
        const sp = this.getScreenPoint(event);
        const ht = this.getCropHandleTypeScreen(sp, crop);
        if (ht !== this.hoverHandle) {
          this.hoverHandle = ht;
          this.updateCursorForHandle(ht);
          this.scheduleRender();
        }
      }
    }

    // drag / панорамирование
    if (!this.pointerActive || this.activePointerId !== event.pointerId) return;

    if (this.isCropping()) {
      if (this.activeCropHandle) {
        this.updateCursorForHandle(this.activeCropHandle.type);
        this.resizeCrop(event);
        return;
      }

      if (this.newCropStartImage && this.pointerDownScreen) {
        const p = this.getScreenPoint(event);
        const moved = Math.hypot(p.x - this.pointerDownScreen.x, p.y - this.pointerDownScreen.y);
        if (moved >= ImageEditorDialogComponent.NEW_CROP_DRAG_THRESHOLD) {
          this.dragStarted = true;
          this.cropStart = this.newCropStartImage;
          this.newCropStartImage = null;
          this.pointerDownScreen = null;
          this.cropRect.set({ x: this.cropStart.x, y: this.cropStart.y, width: 0, height: 0 });
          this.updateCrop(event);
        }
        return;
      }

      if (this.cropStart) {
        this.updateCrop(event);
      }
      return;
    }

    // панорамирование изображения, когда кроп выключен
    if (!this.lastPointerPosition) return;
    const deltaX = event.clientX - this.lastPointerPosition.x;
    const deltaY = event.clientY - this.lastPointerPosition.y;
    this.pan.x += deltaX;
    this.pan.y += deltaY;
    this.lastPointerPosition = { x: event.clientX, y: event.clientY };
    this.scheduleRender();
  };

  private onPointerLeave = (): void => {
    if (!this.isCropping()) return;
    if (this.activeCropHandle) return; // во время драга не трогаем
    this.hoverHandle = null;
    this.updateCursorForHandle(null);
    this.scheduleRender();
  };

  private onPointerUp = (event: PointerEvent): void => {
    if (this.activePointerId !== event.pointerId) return;

    this.canvas.releasePointerCapture?.(event.pointerId);
    this.pointerActive = false;
    this.activePointerId = null;
    this.lastPointerPosition = null;

    if (this.isCropping()) {
      if (this.activeCropHandle) {
        this.finalizeCrop();
        return;
      }

      if (!this.dragStarted && this.newCropStartImage) {
        this.newCropStartImage = null;
        this.pointerDownScreen = null;
        this.hoverHandle = null;
        this.updateCursorForHandle(null);
        this.scheduleRender();
        return;
      }

      if (this.cropStart) {
        this.finalizeCrop();
      }

      if (!this.activeCropHandle) {
        this.hoverHandle = null;
        this.updateCursorForHandle(null);
        this.scheduleRender();
      }
    }
  };

  private onWheel = (event: WheelEvent): void => {
    if (!this.image) return;
    event.preventDefault();

    const currentZoom = this.zoom();
    const factor = Math.exp(-event.deltaY / 300);
    const nextZoom = this.clampZoom(currentZoom * factor);

    if (!this.areZoomValuesEqual(nextZoom, currentZoom)) {
      this.updateZoom(nextZoom);
    }
  };

  // ——— Обновление кропа во время рисования нового прямоугольника ———
  private updateCrop(event: PointerEvent): void {
    if (!this.cropStart) return;
    const current = this.screenToImage(event);
    if (!current) return;

    const x = Math.min(this.cropStart.x, current.x);
    const y = Math.min(this.cropStart.y, current.y);
    const width = Math.abs(current.x - this.cropStart.x);
    const height = Math.abs(current.y - this.cropStart.y);
    const normalized = this.normalizeCrop({ x, y, width, height });
    this.cropRect.set(normalized);
    this.scheduleRender();
  }

  // ——— Zoom helpers ———

  formatZoomLabel(option: number): string {
    const format = (value: number): string => {
      if (this.areZoomValuesEqual(value, Math.round(value))) return Math.round(value).toString();
      return value >= 10 ? value.toFixed(0) : value.toFixed(2);
    };
    if (option >= 1) return `1:${format(option)}`;
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
    if (zoomChanged) this.zoom.set(clamped);

    if ((zoomChanged || panChanged) && schedule) this.scheduleRender();

    return zoomChanged || panChanged;
  }

  private updateZoomLimits(): boolean {
    const minZoom = this.calculateMinZoom();
    if (!Number.isFinite(minZoom) || minZoom <= 0) return false;

    if (!this.areZoomValuesEqual(this.minZoom(), minZoom)) {
      this.minZoom.set(minZoom);
    }
    return this.updateZoom(this.zoom(), { schedule: false });
  }

  private calculateMinZoom(): number {
    if (!this.image) return 1;
    if (this.canvasCssWidth === 0 || this.canvasCssHeight === 0) return 1;

    const widthRatio = this.canvasCssWidth / this.imageWidth;
    const heightRatio = this.canvasCssHeight / this.imageHeight;
    const fitScale = Math.min(widthRatio, heightRatio);
    const minZoom = Math.min(1, fitScale);
    return Math.max(minZoom, 0.01);
  }

  private clampZoom(value: number): number {
    if (!Number.isFinite(value)) return this.minZoom();
    const min = this.minZoom();
    const max = ImageEditorDialogComponent.MAX_ZOOM;
    const clamped = Math.min(max, Math.max(min, value));
    return this.snapToBaseZoom(clamped);
  }

  private snapToBaseZoom(value: number): number {
    for (const option of this.baseZoomOptions) {
      if (this.areZoomValuesEqual(option, value)) return option;
    }
    return value;
  }

  private areZoomValuesEqual(a: number, b: number): boolean {
    return Math.abs(a - b) <= ImageEditorDialogComponent.ZOOM_EPSILON;
  }

  // ——— Resize/move ———

  private resizeCrop(event: PointerEvent): void {
    if (!this.activeCropHandle) return;

    const point = this.screenToImage(event);
    if (!point) return;

    const { startRect, startPointer, type, pointerOffset } = this.activeCropHandle;

    // эффективная позиция ручки с учётом offset
    const effX = point.x - (pointerOffset?.ox ?? 0);
    const effY = point.y - (pointerOffset?.oy ?? 0);

    const left0   = startRect.x;
    const top0    = startRect.y;
    const right0  = startRect.x + startRect.width;
    const bottom0 = startRect.y + startRect.height;

    let left = left0, top = top0, right = right0, bottom = bottom0;

    switch (type) {
      case 'move': {
        // dx/dy от позиции клика (offset учтён)
        const refX = startPointer.x + (pointerOffset?.ox ?? 0);
        const refY = startPointer.y + (pointerOffset?.oy ?? 0);
        const dx = point.x - refX;
        const dy = point.y - refY;
        const translated = this.translateCrop(startRect, dx, dy);
        this.cropRect.set(this.normalizeCrop(translated));
        this.scheduleRender();
        return;
      }
      // УГЛЫ
      case 'top-left':     left = effX; top = effY; break;
      case 'top-right':    right = effX; top = effY; break;
      case 'bottom-left':  left = effX; bottom = effY; break;
      case 'bottom-right': right = effX; bottom = effY; break;
      // РЁБРА
      case 'top':    top = effY; break;
      case 'bottom': bottom = effY; break;
      case 'left':   left = effX; break;
      case 'right':  right = effX; break;
    }

    let rect = this.normalizeCrop({ x: left, y: top, width: right - left, height: bottom - top });
    const normalizedStart = this.normalizeCrop(startRect);
    rect = this.enforceMinimumCropSizeNormalized(rect, type, normalizedStart);
    this.cropRect.set(rect);
    this.scheduleRender();
  }

  private enforceMinimumCropSizeNormalized(
    rect: CropRect,
    type: CropHandleType,
    startRect: CropRect,
  ): CropRect {
    if (type === 'move') return rect;

    const min = ImageEditorDialogComponent.MIN_CROP_SIZE;

    const anchor = {
      left: startRect.x,
      right: startRect.x + startRect.width,
      top: startRect.y,
      bottom: startRect.y + startRect.height,
    };

    let left = rect.x;
    let top = rect.y;
    let right = rect.x + rect.width;
    let bottom = rect.y + rect.height;

    switch (type) {
      case 'top-left':
        right = anchor.right;
        bottom = anchor.bottom;
        left = Math.min(left, right - min);
        top = Math.min(top, bottom - min);
        break;
      case 'top-right':
        left = anchor.left;
        bottom = anchor.bottom;
        right = Math.max(right, left + min);
        top = Math.min(top, bottom - min);
        break;
      case 'bottom-left':
        right = anchor.right;
        top = anchor.top;
        left = Math.min(left, right - min);
        bottom = Math.max(bottom, top + min);
        break;
      case 'bottom-right':
        left = anchor.left;
        top = anchor.top;
        right = Math.max(right, left + min);
        bottom = Math.max(bottom, top + min);
        break;
      case 'top':
        left = anchor.left;
        right = anchor.right;
        top = Math.min(top, bottom - min);
        break;
      case 'bottom':
        left = anchor.left;
        right = anchor.right;
        bottom = Math.max(bottom, top + min);
        break;
      case 'left':
        top = anchor.top;
        bottom = anchor.bottom;
        left = Math.min(left, right - min);
        break;
      case 'right':
        top = anchor.top;
        bottom = anchor.bottom;
        right = Math.max(right, left + min);
        break;
    }

    return this.normalizeCrop({ x: left, y: top, width: right - left, height: bottom - top });
  }

  private finalizeCrop(): void {
    const crop = this.cropRect();
    if (!crop) {
      this.cropStart = null;
      this.activeCropHandle = null;
      this.newCropStartImage = null;
      this.pointerDownScreen = null;
      this.dragStarted = false;
      this.hoverHandle = null;
      this.updateCursorForHandle(null);
      this.scheduleRender();
      return;
    }
    if (crop.width < 2 || crop.height < 2) {
      this.cropRect.set(null);
    }
    this.cropStart = null;
    this.activeCropHandle = null;
    this.newCropStartImage = null;
    this.pointerDownScreen = null;
    this.dragStarted = false;
    this.hoverHandle = null;
    this.updateCursorForHandle(null);
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
    if (panChanged) this.pan = safePan;

    this.scheduleRender();
  }

  private calculateZoomForCrop(crop: CropRect): number {
    if (this.canvasCssWidth === 0 || this.canvasCssHeight === 0) return this.zoom();

    const angle = ((this.rotation() % 360) + 360) % 360;
    const radians = (angle * Math.PI) / 180;
    const cropWidth = crop.width;
    const cropHeight = crop.height;

    const rotatedWidth = Math.abs(cropWidth * Math.cos(radians)) + Math.abs(cropHeight * Math.sin(radians));
    const rotatedHeight = Math.abs(cropWidth * Math.sin(radians)) + Math.abs(cropHeight * Math.cos(radians));

    if (rotatedWidth <= 0 || rotatedHeight <= 0) return this.zoom();

    const widthRatio = this.canvasCssWidth / rotatedWidth;
    const heightRatio = this.canvasCssHeight / rotatedHeight;
    const zoomToFit = Math.min(widthRatio, heightRatio);

    if (!Number.isFinite(zoomToFit) || zoomToFit <= 0) return this.zoom();
    return zoomToFit;
  }

  private calculatePanForCrop(crop: CropRect, zoom: number): { x: number; y: number } {
    const matrix = this.buildTransformMatrix(zoom, { x: 0, y: 0 });
    const centerPoint = new DOMPoint(crop.x + crop.width / 2, crop.y + crop.height / 2);
    const transformed = matrix.transformPoint(centerPoint);
    return {
      x: this.canvasCssWidth / 2 - transformed.x,
      y: this.canvasCssHeight / 2 - transformed.y,
    };
  }

  private arePanValuesEqual(a: { x: number; y: number }, b: { x: number; y: number }): boolean {
    return (
      Math.abs(a.x - b.x) <= ImageEditorDialogComponent.PAN_EPSILON &&
      Math.abs(a.y - b.y) <= ImageEditorDialogComponent.PAN_EPSILON
    );
  }

  // ——— Геометрия/матрицы ———

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

    if (x + width > maxWidth) x = Math.max(0, maxWidth - width);
    if (y + height > maxHeight) y = Math.max(0, maxHeight - height);

    return { x, y, width, height };
  }

  private getScreenPoint(event: PointerEvent) {
    const rect = this.canvas.getBoundingClientRect(); // CSS px
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
  }

  private screenPointToImage(pt: { x: number; y: number }): { x: number; y: number } | null {
    const M = this.getTransformMatrix(); // CSS px
    const inv = M.inverse();
    const p = inv.transformPoint(new DOMPoint(pt.x, pt.y));
    if (Number.isFinite(p.x) && Number.isFinite(p.y)) return { x: p.x, y: p.y };
    return null;
  }

  private screenToImage(event: PointerEvent): { x: number; y: number } | null {
    return this.screenPointToImage(this.getScreenPoint(event));
  }

  private transformPoint(point: { x: number; y: number }): { x: number; y: number } {
    const matrix = this.getTransformMatrix();
    const result = matrix.transformPoint(new DOMPoint(point.x, point.y));
    return { x: result.x, y: result.y };
  }

  private getTransformMatrix(): DOMMatrix {
    return this.buildTransformMatrix(this.zoom(), this.pan); // все в CSS px
  }

  private buildTransformMatrix(zoom: number, pan: { x: number; y: number }): DOMMatrix {
    const rotation = this.rotation();
    const flipH = this.flipHorizontal() ? -1 : 1;
    const flipV = this.flipVertical() ? -1 : 1;

    const cx = this.canvasCssWidth / 2 + pan.x;
    const cy = this.canvasCssHeight / 2 + pan.y;

    let matrix = new DOMMatrix();
    matrix = matrix.translate(cx, cy);
    matrix = matrix.rotate(rotation);
    matrix = matrix.scale(zoom * flipH, zoom * flipV);
    matrix = matrix.translate(-this.imageWidth / 2, -this.imageHeight / 2);
    return matrix;
  }

  // ——— Хит-тест в экранных координатах ———

  private getHandleCentersScreen(crop: CropRect): Array<{ x: number; y: number; type: CropHandleType }> {
    const M = this.getTransformMatrix();
    const [p1, p2, p3, p4] = this.getCropCorners(crop).map(pt => {
      const s = M.transformPoint(new DOMPoint(pt.x, pt.y));
      return { x: s.x, y: s.y };
    });
    const mid = (a: { x: number; y: number }, b: { x: number; y: number }) => ({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 });
    return [
      { ...p1, type: 'top-left' },
      { ...p2, type: 'top-right' },
      { ...p3, type: 'bottom-right' },
      { ...p4, type: 'bottom-left' },
      { ...mid(p1, p2), type: 'top' },
      { ...mid(p2, p3), type: 'right' },
      { ...mid(p4, p3), type: 'bottom' },
      { ...mid(p1, p4), type: 'left' },
    ];
  }

  private getHandleHitBoxesScreen(crop: CropRect): Array<{ x: number; y: number; hw: number; hh: number; type: CropHandleType }> {
    const centers = this.getHandleCentersScreen(crop);
    const HANDLE_HALF = 10; // 20×20 px
    return centers.map(c => ({ x: c.x, y: c.y, hw: HANDLE_HALF, hh: HANDLE_HALF, type: c.type }));
  }

  private getCropHandleTypeScreen(Ps: { x: number; y: number }, crop: CropRect): CropHandleType | null {
    // 1) квадраты — углы, затем середины
    const boxes = this.getHandleHitBoxesScreen(crop);
    const order: CropHandleType[] = ['top-left', 'top-right', 'bottom-right', 'bottom-left', 'top', 'right', 'bottom', 'left'];
    const byType = new Map(boxes.map(b => [b.type, b]));
    for (const t of order) {
      const b = byType.get(t)!;
      if (Math.abs(Ps.x - b.x) <= b.hw && Math.abs(Ps.y - b.y) <= b.hh) return t;
    }

    // 2) рёбра как полоса шириной 10 px
    const EDGE_HALF = 10;
    const M = this.getTransformMatrix();
    const [p1, p2, p3, p4] = this.getCropCorners(crop).map(pt => {
      const s = M.transformPoint(new DOMPoint(pt.x, pt.y));
      return { x: s.x, y: s.y };
    });
    const hitEdge = (a: { x: number; y: number }, b: { x: number; y: number }) => {
      const vx = b.x - a.x, vy = b.y - a.y;
      const wx = Ps.x - a.x, wy = Ps.y - a.y;
      const len2 = vx * vx + vy * vy || 1;
      const t = Math.max(0, Math.min(1, (wx * vx + wy * vy) / len2));
      const px = a.x + t * vx, py = a.y + t * vy;
      return Math.hypot(Ps.x - px, Ps.y - py) <= EDGE_HALF;
    };
    if (hitEdge(p1, p2)) return 'top';
    if (hitEdge(p2, p3)) return 'right';
    if (hitEdge(p4, p3)) return 'bottom';
    if (hitEdge(p1, p4)) return 'left';

    // 3) внутри прямоугольника — move (нужно проверить в координатах изображения)
    const ptImg = this.screenPointToImage(Ps);
    if (ptImg) {
      const left = crop.x, right = crop.x + crop.width, top = crop.y, bottom = crop.y + crop.height;
      if (ptImg.x > left && ptImg.x < right && ptImg.y > top && ptImg.y < bottom) return 'move';
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

  private getTransformMatrixForExport(rotation: number, flipH: boolean, flipV: boolean, sourceRect: CropRect): { width: number; height: number; radians: number } {
    const angle = ((rotation % 360) + 360) % 360;
    const radians = (angle * Math.PI) / 180;
    const needsSwap = angle === 90 || angle === 270;
    const outputWidth = needsSwap ? sourceRect.height : sourceRect.width;
    const outputHeight = needsSwap ? sourceRect.width : sourceRect.height;
    return { width: Math.max(1, Math.round(outputWidth)), height: Math.max(1, Math.round(outputHeight)), radians };
  }

  private async exportTransformedImage(): Promise<{ blob: Blob; width: number; height: number }> {
    if (!this.image) throw new Error('Изображение не загружено');

    const crop = this.cropRect();
    const rotation = this.rotation();
    const flipH = this.flipHorizontal();
    const flipV = this.flipVertical();

    const sourceRect: CropRect = crop ?? { x: 0, y: 0, width: this.imageWidth, height: this.imageHeight };
    const { width, height, radians } = this.getTransformMatrixForExport(rotation, flipH, flipV, sourceRect);

    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('Canvas недоступен');

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
    if (!blob) throw new Error('Не удалось получить PNG изображение');

    return { blob, width: canvas.width, height: canvas.height };
  }

  // курсоры
  private updateCursorForHandle(type: CropHandleType | null): void {
    const el = this.canvas;
    let cursor = 'default';
    switch (type) {
      case 'move': cursor = 'move'; break;
      case 'top':
      case 'bottom': cursor = 'ns-resize'; break;
      case 'left':
      case 'right': cursor = 'ew-resize'; break;
      case 'top-left':
      case 'bottom-right': cursor = 'nwse-resize'; break;
      case 'top-right':
      case 'bottom-left': cursor = 'nesw-resize'; break;
      default: cursor = this.isCropping() ? 'crosshair' : 'default';
    }
    el.style.cursor = cursor;
  }
}
