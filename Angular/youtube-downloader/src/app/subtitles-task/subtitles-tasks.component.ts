import { Component, DestroyRef, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom, forkJoin, interval, Subject, Subscription } from 'rxjs';
import { debounceTime, finalize, map } from 'rxjs/operators';
import { LocalTimePipe } from '../pipe/local-time.pipe';
import { SubtitleService, YoutubeCaptionTaskDto2, RecognizeStatus, YoutubeCaptionVisibility } from '../services/subtitle.service';
import type { VideoDialogData } from '../video-dialog/video-dialog.component';
import { YandexAdComponent } from '../ydx-ad/yandex-ad.component';
import { AuthService } from '../services/AuthService.service';
import { extractUsageLimitResponse } from '../models/usage-limit-response';

type LibrarySort = 'newest' | 'oldest' | 'title' | 'channel' | 'status';

@Component({
  selector: 'app-subtitles-tasks',
  standalone: true,
  templateUrl: './subtitles-tasks.component.html',
  styleUrls: ['../shared/account-page.css', './subtitles-tasks.component.css'],
  imports: [CommonModule, RouterModule, FormsModule, MatIconModule, MatProgressBarModule,
    MatDialogModule, LocalTimePipe, YandexAdComponent],
})
export class SubtitlesTasksComponent implements OnInit {
  readonly RecognizeStatus = RecognizeStatus;
  readonly Visibility = YoutubeCaptionVisibility;
  readonly pageSize = 15;
  readonly skeletonCards = [1, 2, 3, 4, 5, 6];
  tasks: YoutubeCaptionTaskDto2[] = [];
  totalItems = 0;
  pageIndex = 0;
  sortValue: LibrarySort = 'newest';
  viewMode: 'cards' | 'list' = 'cards';
  filterValue = '';
  userIdFilter: string | null = null;
  currentUserId: string | null = null;
  showOnlyMine = false;
  isAuthenticated = false;
  isAdmin = false;
  canManageVisibility = false;
  canUseBatch = false;
  loading = false;
  loadingMore = false;
  listError: string | null = null;
  actionMessage: string | null = null;
  recognitionInput = '';
  recognitionStarting = false;
  recognitionBatchStarting = false;
  recognitionError: string | null = null;
  recognitionNeedsPayment = false;
  batchDialogOpening = false;
  expandedTasks = new Set<string>();
  visibilityErrors = new Map<string, string>();
  videoErrors = new Map<string, string>();
  private readonly visibilityUpdating = new Set<string>();
  private readonly videoOpening = new Set<string>();
  private readonly searchChanges = new Subject<void>();
  private listRequest?: Subscription;
  private refreshRequest?: Subscription;
  private requestVersion = 0;
  private initialized = false;
  private isDestroyed = false;

  constructor(
    private readonly subtitleService: SubtitleService,
    private readonly titleService: Title,
    private readonly dialog: MatDialog,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly authService: AuthService,
    private readonly destroyRef: DestroyRef,
  ) {
    this.titleService.setTitle('Скрипторий — библиотека расшифровок YouTube');
    this.authService.user$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(user => {
      const oldScope = this.currentUserId + ':' + this.isAdmin + ':' + this.canManageVisibility;
      this.isAuthenticated = !!user;
      this.currentUserId = user?.id ?? null;
      this.isAdmin = !!user?.roles?.some(role => role.toLowerCase() === 'admin');
      this.canManageVisibility = this.isAdmin || !!user?.canHideCaptions;
      this.canUseBatch = !!user?.canHideCaptions;
      this.showOnlyMine = !!this.currentUserId && this.userIdFilter === this.currentUserId;
      if (this.initialized && oldScope !== this.currentUserId + ':' + this.isAdmin + ':' + this.canManageVisibility) {
        this.reloadTasks();
      }
    });
    this.destroyRef.onDestroy(() => {
      this.isDestroyed = true;
      this.listRequest?.unsubscribe();
      this.refreshRequest?.unsubscribe();
    });
  }

  ngOnInit(): void {
    this.initialized = true;
    this.searchChanges.pipe(debounceTime(350), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.reloadTasks());
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      this.userIdFilter = params.get('userId');
      this.showOnlyMine = !!this.currentUserId && this.userIdFilter === this.currentUserId;
      this.reloadTasks();
    });
    interval(10000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.refreshActiveTasks());
  }

  get sort(): { field: string; order: string } {
    switch (this.sortValue) {
      case 'oldest': return { field: 'createdAt', order: 'asc' };
      case 'title': return { field: 'title', order: 'asc' };
      case 'channel': return { field: 'channelName', order: 'asc' };
      case 'status': return { field: 'status', order: 'asc' };
      default: return { field: 'createdAt', order: 'desc' };
    }
  }
  get includeHidden(): boolean {
    return this.isAdmin || (this.canManageVisibility && !!this.currentUserId && this.userIdFilter === this.currentUserId);
  }
  get canManageCurrentScope(): boolean { return this.isAdmin || (this.canManageVisibility && this.showOnlyMine); }
  get completedTasksCount(): number { return this.tasks.filter(task => this.isCompleted(task)).length; }
  get inProgressTasksCount(): number { return this.tasks.filter(task => this.isTaskInProgress(task)).length; }
  get failedTasksCount(): number { return this.tasks.filter(task => task.status === RecognizeStatus.Error).length; }
  get hasMore(): boolean { return this.tasks.length < this.totalItems; }
  get recognitionBusy(): boolean { return this.recognitionStarting || this.recognitionBatchStarting || this.batchDialogOpening; }

  isCompleted(task: YoutubeCaptionTaskDto2): boolean {
    return task.status !== RecognizeStatus.Error && (task.done || task.status === RecognizeStatus.Done);
  }
  isTaskInProgress(task: YoutubeCaptionTaskDto2): boolean {
    return !this.isCompleted(task) && task.status !== RecognizeStatus.Error;
  }
  trackTask(_: number, task: YoutubeCaptionTaskDto2): string { return task.id; }
  taskLink(task: YoutubeCaptionTaskDto2): string { return task.slug || task.id; }

  onSearchChange(): void {
    // Cancel immediately, before the debounce, so an older query cannot replace the new results.
    this.cancelRequests();
    this.loading = true;
    this.loadingMore = false;
    this.listError = null;
    this.searchChanges.next();
  }
  clearFilter(input?: HTMLInputElement): void {
    this.filterValue = '';
    this.onSearchChange();
    input?.focus();
  }
  searchChannel(channel: string): void {
    this.filterValue = channel;
    this.onSearchChange();
  }
  setShowOnlyMine(show: boolean): void {
    if (show && !this.currentUserId) return;
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { userId: show ? this.currentUserId : null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  private cancelRequests(): void {
    this.requestVersion++;
    this.listRequest?.unsubscribe();
    this.refreshRequest?.unsubscribe();
  }

  reloadTasks(): void {
    this.loadPage(0, false);
  }
  loadMore(): void {
    if (this.loading || this.loadingMore || !this.hasMore) return;
    this.loadPage(this.pageIndex + 1, true);
  }
  retryList(): void {
    if (this.tasks.length && this.hasMore) this.loadMore();
    else this.reloadTasks();
  }

  private loadPage(pageIndex: number, append: boolean): void {
    this.cancelRequests();
    const version = this.requestVersion;
    this.loading = !append;
    this.loadingMore = append;
    this.listError = null;
    if (!append) {
      this.tasks = [];
      this.totalItems = 0;
      this.pageIndex = 0;
      this.expandedTasks.clear();
      this.visibilityErrors.clear();
      this.videoErrors.clear();
    }
    this.listRequest = this.subtitleService.getTasks(pageIndex + 1, this.pageSize,
      this.sort.field, this.sort.order, this.filterValue.trim(), this.userIdFilter, this.includeHidden)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: response => {
          if (version !== this.requestVersion) return;
          const items = append ? [...this.tasks, ...response.items] : response.items;
          this.tasks = Array.from(new Map(items.map(task => [task.id, task])).values());
          this.totalItems = response.totalCount;
          this.pageIndex = pageIndex;
          this.loading = false;
          this.loadingMore = false;
        },
        error: () => {
          if (version !== this.requestVersion) return;
          this.loading = false;
          this.loadingMore = false;
          this.listError = append
            ? 'Не удалось загрузить следующие материалы. Уже открытые записи остались на странице.'
            : 'Не удалось загрузить библиотеку. Попробуйте ещё раз.';
        },
      });
  }

  private refreshActiveTasks(): void {
    if (this.loading || this.loadingMore || (this.refreshRequest && !this.refreshRequest.closed)
      || this.visibilityUpdating.size || !this.inProgressTasksCount) return;
    const version = this.requestVersion;
    const pages = Array.from(new Set(this.tasks.flatMap((task, i) =>
      this.isTaskInProgress(task) ? [Math.floor(i / this.pageSize)] : [])));
    this.refreshRequest = forkJoin(pages.map(page => this.subtitleService.getTasks(page + 1, this.pageSize,
      this.sort.field, this.sort.order, this.filterValue.trim(), this.userIdFilter, this.includeHidden)
      .pipe(map(response => response.items))))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: groups => {
          if (version !== this.requestVersion) return;
          const updates = new Map(groups.flat().map(task => [task.id, task]));
          // Keep cards in place during background updates.
          this.tasks = this.tasks.map(task => updates.get(task.id) ?? task);
        },
        error: () => { /* A transient refresh failure is retried on the next interval. */ },
      });
  }

  toggleExpand(id: string): void {
    this.expandedTasks.has(id) ? this.expandedTasks.delete(id) : this.expandedTasks.add(id);
  }
  taskProgress(task: YoutubeCaptionTaskDto2): number {
    return task.segmentsTotal > 0 ? Math.min(100, Math.max(0, task.segmentsProcessed / task.segmentsTotal * 100)) : 0;
  }
  getStatusText(status: RecognizeStatus | null | undefined): string {
    switch (status) {
      case RecognizeStatus.Created: return 'В очереди';
      case RecognizeStatus.Converting: return 'Подготавливаем аудио';
      case RecognizeStatus.Uploading: return 'Загружаем запись';
      case RecognizeStatus.Recognizing: return 'Распознаём речь';
      case RecognizeStatus.RetrievingResult: return 'Получаем результат';
      case RecognizeStatus.ApplyingPunctuation: return 'Оформляем текст';
      case RecognizeStatus.FetchingSubtitles: return 'Проверяем видео';
      case RecognizeStatus.DownloadingCaptions: return 'Получаем субтитры';
      case RecognizeStatus.SegmentingCaptions: return 'Подготавливаем текст';
      case RecognizeStatus.ApplyingPunctuationSegment: return 'Обрабатываем текст';
      case RecognizeStatus.Done: return 'Готово к чтению';
      case RecognizeStatus.Error: return 'Ошибка обработки';
      default: return 'Ожидаем обновления';
    }
  }

  isVisibilityUpdating(id: string): boolean { return this.visibilityUpdating.has(id); }
  toggleTaskVisibility(task: YoutubeCaptionTaskDto2): void {
    if (!this.canManageCurrentScope || this.visibilityUpdating.has(task.id) || task.visibility === this.Visibility.Deleted) return;
    const nextVisibility = task.visibility === this.Visibility.Hidden ? this.Visibility.Public : this.Visibility.Hidden;
    this.refreshRequest?.unsubscribe();
    this.visibilityUpdating.add(task.id);
    this.visibilityErrors.delete(task.id);
    this.subtitleService.updateTaskVisibility(task.id, nextVisibility)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.visibilityUpdating.delete(task.id)))
      .subscribe({
        next: () => {
          this.tasks = this.tasks.map(item => item.id === task.id ? { ...item, visibility: nextVisibility } : item);
        },
        error: () => this.visibilityErrors.set(task.id, 'Не удалось изменить видимость. Попробуйте снова.'),
      });
  }

  onStartRecognition(): void {
    const query = this.recognitionInput.trim();
    if (!query || this.recognitionBusy) return;
    this.recognitionStarting = true;
    this.recognitionError = null;
    this.recognitionNeedsPayment = false;
    this.subtitleService.startWithTrackChoice(query).pipe(takeUntilDestroyed(this.destroyRef),
      finalize(() => this.recognitionStarting = false)).subscribe({
        next: response => {
          if (response.taskId) this.router.navigate(['/recognized', response.taskId]);
          else this.reloadTasks();
        },
        error: (error: HttpErrorResponse) => this.handleRecognitionError(error, 'Не удалось начать обработку. Проверьте ссылку и попробуйте снова.'),
      });
  }

  async onStartRecognitionBatch(): Promise<void> {
    if (this.recognitionBusy || !this.isAuthenticated || !this.canUseBatch) return;
    this.recognitionError = null;
    this.batchDialogOpening = true;
    try {
      const { BulkVideoDialogComponent } = await import('../bulk-video-dialog/bulk-video-dialog.component');
      if (this.isDestroyed) return;
      const ref = this.dialog.open(BulkVideoDialogComponent, {
        width: '640px', maxWidth: '95vw', maxHeight: '90vh', panelClass: 'library-dialog-panel', data: { value: '' },
      });
      ref.afterClosed().pipe(takeUntilDestroyed(this.destroyRef)).subscribe((input?: string | null) => {
        this.batchDialogOpening = false;
        const items = Array.from(new Set((input ?? '').split(/\r?\n/).map(line => line.trim()).filter(Boolean)));
        if (items.length) this.startRecognitionBatch(items);
      });
    } catch {
      this.batchDialogOpening = false;
      this.recognitionError = 'Не удалось открыть форму. Попробуйте снова.';
    }
  }

  private startRecognitionBatch(items: string[]): void {
    this.recognitionBatchStarting = true;
    this.recognitionNeedsPayment = false;
    this.recognitionError = null;
    this.actionMessage = null;
    this.subtitleService.startBatchWithTrackChoices(items)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.recognitionBatchStarting = false))
      .subscribe({
        next: response => {
          const count = response.taskIds?.length ?? 0;
          this.actionMessage = count ? `Обработка запущена. Задач: ${count}. Их можно найти в разделе «Мои материалы».` : null;
          this.reloadTasks();
          if (response.invalidItems?.length) {
            this.recognitionError = 'Не удалось добавить: ' + response.invalidItems.slice(0, 3).join(', ')
              + (response.invalidItems.length > 3 ? ` и ещё ${response.invalidItems.length - 3}.` : '.');
          }
        },
        error: (error: HttpErrorResponse) => this.handleRecognitionError(error, 'Не удалось запустить обработку списка. Попробуйте снова.'),
      });
  }

  private handleRecognitionError(error: HttpErrorResponse, fallback: string): void {
    if (error.status === 401) {
      this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
      return;
    }
    const limit = extractUsageLimitResponse(error);
    this.recognitionNeedsPayment = !!limit || error.status === 402;
    const message = limit?.message ?? error.error?.message ?? error.error?.title;
    this.recognitionError = typeof message === 'string' ? message : fallback;
  }

  isVideoOpening(id: string): boolean { return this.videoOpening.has(id); }
  async openVideoDialog(task: YoutubeCaptionTaskDto2): Promise<void> {
    if (!this.isAuthenticated || this.videoOpening.has(task.id)) return;
    this.videoOpening.add(task.id);
    this.videoErrors.delete(task.id);
    try {
      // A caption task may have its own ID when the video has several language tracks.
      const details = /^[a-zA-Z0-9_-]{11}$/.test(task.id) ? null
        : await firstValueFrom(this.subtitleService.getStatus(task.id).pipe(takeUntilDestroyed(this.destroyRef)));
      const videoId = details?.videoId || task.id;
      if (!/^[a-zA-Z0-9_-]{11}$/.test(videoId)) throw new Error('Missing video ID');
      const { VideoDialogComponent } = await import('../video-dialog/video-dialog.component');
      if (this.isDestroyed) return;
      const data: VideoDialogData = { videoId, title: task.title, channelName: task.channelName,
        channelId: task.channelId, uploadDate: task.uploadDate };
      this.dialog.open(VideoDialogComponent, {
        width: '900px', maxWidth: '95vw', maxHeight: '90vh', panelClass: 'library-dialog-panel', data,
      });
    } catch {
      if (!this.isDestroyed) this.videoErrors.set(task.id, 'Не удалось открыть видео. Попробуйте ещё раз.');
    } finally {
      this.videoOpening.delete(task.id);
    }
  }
}
