import { Component, ElementRef, HostListener, Inject, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule } from '@angular/material/menu';
import { MatButtonModule } from '@angular/material/button';
import { Router, RouterModule } from '@angular/router';
import { Subscription, timer } from 'rxjs';
import { exhaustMap } from 'rxjs/operators';
import { DomSanitizer, SafeHtml, Title } from '@angular/platform-browser';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatCheckboxModule } from '@angular/material/checkbox';
import {
  AdminRestartStoppedTasksResponse,
  OpenAiTranscriptionService,
  OpenAiTranscriptionStatus,
  OpenAiTranscriptionTaskDetailsDto,
  OpenAiTranscriptionTaskDto,
  OpenAiTranscriptionStepDto,
  OpenAiTranscriptionStepStatus,
} from '../services/openai-transcription.service';
import { LocalTimePipe } from '../pipe/local-time.pipe';
import { MarkdownRendererService1 } from '../task-result/markdown-renderer.service';
import { MatDialog } from '@angular/material/dialog';
import { OpenAiTranscriptionUploadDialogComponent } from './openai-transcription-upload-dialog.component';
import { OpenAiTranscriptionUploadFormComponent } from './openai-transcription-upload-form.component';
import { ActionMenuPanelDirective } from '../shared/action-menu-panel.directive';
import { TranscriptionHeroComponent } from '../shared/transcription-hero/transcription-hero.component';
import {
  OpenAiTranscriptionAnalyticsDialogComponent,
  OpenAiTranscriptionAnalyticsDialogData,
  OpenAiTranscriptionAnalyticsDialogResult,
} from './openai-transcription-analytics-dialog.component';
import { PaymentsService, SubscriptionSummary } from '../services/payments.service';
import { UsageLimitResponse, extractUsageLimitResponse } from '../models/usage-limit-response';
import { AuthService } from '../services/AuthService.service';
import { PLATFORM_ID } from '@angular/core';

@Component({
  selector: 'app-openai-transcriptions',
  standalone: true,
  imports: [
    CommonModule,
    MatIconModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatMenuModule,
    MatButtonModule,
    MatSnackBarModule,
    MatCheckboxModule,
    LocalTimePipe,
    RouterModule,
    ActionMenuPanelDirective,
    TranscriptionHeroComponent,
    OpenAiTranscriptionUploadFormComponent,
  ],
  templateUrl: './openai-transcription.component.html',
  styleUrls: ['./openai-transcription.component.css'],
})
export class OpenAiTranscriptionComponent implements OnInit, OnDestroy {
  @ViewChild('detailsPanelContainer')
  private detailsPanelContainer?: ElementRef<HTMLElement>;

  tasks: OpenAiTranscriptionTaskDto[] = [];
  selectedTaskId: string | null = null;
  selectedTask: OpenAiTranscriptionTaskDetailsDto | null = null;

  uploading = false;
  listError: string | null = null;
  detailsError: string | null = null;
  detailsLoading = false;
  continueError: string | null = null;
  continueInProgress = false;
  continueFromSegmentError: string | null = null;
  continueFromSegmentInProgress = false;
  continueFromSegmentNumber: number | null = null;
  exportError: string | null = null;
  exportingPdf = false;
  exportingDocx = false;
  downloadingSrt = false;
  copying = false;
  deleteError: string | null = null;
  deleteInProgress = false;
  renderedMarkdown: SafeHtml | null = null;
  private markdownSource = '';
  isResultFullscreen = false;
  private originalBodyOverflow: string | null = null;
  analyticsInProgress = false;
  analyticsError: string | null = null;
  limitResponse: UsageLimitResponse | null = null;
  summary: SubscriptionSummary | null = null;
  summaryLoading = false;
  summaryError: string | null = null;
  adminRestartInProgress = false;
  adminRestartError: string | null = null;

  readonly OpenAiTranscriptionStatus = OpenAiTranscriptionStatus;
  readonly OpenAiTranscriptionStepStatus = OpenAiTranscriptionStepStatus;
  private readonly workflowStepsWithDownload: readonly OpenAiTranscriptionStatus[] = [
    OpenAiTranscriptionStatus.Downloading,
    OpenAiTranscriptionStatus.Converting,
    OpenAiTranscriptionStatus.Transcribing,
    OpenAiTranscriptionStatus.Segmenting,
    OpenAiTranscriptionStatus.ProcessingSegments,
  ];
  private readonly workflowStepsWithoutDownload: readonly OpenAiTranscriptionStatus[] = [
    OpenAiTranscriptionStatus.Converting,
    OpenAiTranscriptionStatus.Transcribing,
    OpenAiTranscriptionStatus.Segmenting,
    OpenAiTranscriptionStatus.ProcessingSegments,
  ];
  readonly heroTitle = 'Преобразовать аудио и видео в текст';
  readonly heroLead =
    'Online-сервис автоматической транскрибации помогает за считанные минуты превратить записи интервью, созвонов и лекций в структурированный текст.';
  readonly heroHighlights: readonly string[] = [
    'Выделение участников разговора',
    'Исправление орфографии, разметка и форматирование',
    'Структурирование в документе в виде таблиц, списков, формул',
    'Формирование готового к печати документа в Word и PDF',
    'Профили распознавания — переговоры, совещание, собеседование, презентация и другие',
  ];

  private pollSubscription?: Subscription;
  private ensurePanelVisibleScheduled = false;
  private userSubscription?: Subscription;
  isAdmin = false;
  showAllTasks = false;
  private readonly isBrowser: boolean;

  get downloadInProgress(): boolean {
    return this.exportingPdf || this.exportingDocx || this.downloadingSrt;
  }

  get canShowAllTasks(): boolean {
    return this.isAdmin;
  }

  get isShowingAllTasks(): boolean {
    return this.isAdmin && this.showAllTasks;
  }

  constructor(
    private readonly transcriptionService: OpenAiTranscriptionService,
    private readonly markdownRenderer: MarkdownRendererService1,
    private readonly sanitizer: DomSanitizer,
    private readonly router: Router,
    private readonly dialog: MatDialog,
    private readonly snackBar: MatSnackBar,
    private readonly paymentsService: PaymentsService,
    private readonly authService: AuthService,
    private readonly titleService: Title,
    @Inject(PLATFORM_ID) platformId: Object,
  ) {
    this.isBrowser = isPlatformBrowser(platformId);
    this.titleService.setTitle('Протокол совещаний — расшифровки YouScriptor');
  }

  ngOnInit(): void {
    this.userSubscription = this.authService.user$.subscribe((user) => {
      const wasAdmin = this.isAdmin;
      this.isAdmin = !!user?.roles?.some((role) => role.toLowerCase() === 'admin');

      if (!this.isAdmin && this.showAllTasks) {
        this.showAllTasks = false;
        this.loadTasks(true);
        return;
      }

      if (wasAdmin !== this.isAdmin) {
        this.loadTasks(!this.selectedTaskId);
      }
    });
    if (this.isBrowser) {
      this.loadSubscriptionSummary();
    }
    this.loadTasks(true);
  }

  loadSubscriptionSummary(): void {
    if (!this.isBrowser) {
      return;
    }

    this.summaryLoading = true;
    this.summaryError = null;
    this.paymentsService.getSubscriptionSummary().subscribe({
      next: (summary) => {
        this.summaryLoading = false;
        this.summary = summary;
      },
      error: () => {
        this.summaryLoading = false;
        this.summaryError = 'Не удалось загрузить сведения о подписке.';
      },
    });
  }

  get subscriptionStatusMessage(): string {
    if (!this.summary) {
      return '';
    }

    const planName = this.summary.planName || (this.summary.hasActiveSubscription ? 'Пакет активен' : 'Стартовый пакет');
    const remaining = `${this.formatRemainingHours(this.summary.remainingTranscriptionMinutes)} / ${this.formatRemainingVideos(this.summary.remainingVideos)}`;

    if (this.summary.endsAt) {
      const ends = new Date(this.summary.endsAt).toLocaleDateString('ru-RU');
      return `${planName} до ${ends}: осталось ${remaining}`;
    }

    return `${planName}: осталось ${remaining}`;
  }

  get subscriptionChipClass(): string {
    if (!this.summary) {
      return 'status-neutral';
    }

    if (this.summary.hasLifetimeAccess || this.summary.isLifetime) {
      return 'status-success';
    }

    if (this.summary.hasActiveSubscription) {
      return 'status-active';
    }

    return 'status-trial';
  }

  get billingUrl(): string {
    if (this.limitResponse?.paymentUrl) {
      return this.limitResponse.paymentUrl;
    }

    if (this.summary?.billingUrl) {
      return this.summary.billingUrl;
    }

    return '/billing';
  }

  navigateToBilling(url?: string): void {
    const target = url ?? this.billingUrl;
    if (!target) {
      return;
    }

    if (target.startsWith('http')) {
      window.open(target, '_blank');
      return;
    }

    this.dialog.closeAll();
    this.router?.navigateByUrl(target);
  }

  handleUsageLimit(limit: UsageLimitResponse): void {
    this.limitResponse = limit;
    const ref = this.snackBar.open(limit.message, 'Оплатить', { duration: 6000 });
    ref.onAction().subscribe(() => this.navigateToBilling(limit.paymentUrl ?? this.billingUrl));
  }

  onUploadStateChange(state: boolean): void {
    this.uploading = state;
  }

  ngOnDestroy(): void {
    this.stopPolling();
    this.resetFullscreenState();
    this.userSubscription?.unsubscribe();
  }

  @HostListener('document:keydown.escape')
  onEscapePressed(): void {
    if (!this.isResultFullscreen) {
      return;
    }

    this.resetFullscreenState();
  }

  openUploadDialog(): void {
    const dialogRef = this.dialog.open(OpenAiTranscriptionUploadDialogComponent, {
      width: '520px',
      autoFocus: false,
      restoreFocus: false,
      disableClose: true,
    });

    const component = dialogRef.componentInstance;
    const subscriptions: Subscription[] = [];

    if (component) {
      subscriptions.push(
        component.uploadingChange.subscribe((state) => {
          this.uploading = state;
        })
      );
    }

    dialogRef.afterClosed().subscribe((result) => {
      subscriptions.forEach((sub) => sub.unsubscribe());
      this.uploading = false;
      if (result?.limit) {
        this.handleUsageLimit(result.limit);
      } else if (result?.task) {
        this.handleUploadSuccess(result.task);
      }
    });
  }

  loadTasks(selectFirstAvailable = false): void {
    this.listError = null;
    const includeAll = this.isAdmin && this.showAllTasks;
    this.transcriptionService.list(includeAll).subscribe({
      next: (tasks) => {
        this.tasks = tasks;

        if (!this.selectedTaskId && selectFirstAvailable && tasks.length > 0) {
          this.selectTask(tasks[0]);
          return;
        }

        if (this.selectedTaskId && !tasks.some((task) => task.id === this.selectedTaskId)) {
          this.stopPolling();
          this.selectedTaskId = null;
          this.selectedTask = null;
          if (selectFirstAvailable && tasks.length > 0) {
            this.selectTask(tasks[0]);
          }
        }
      },
      error: (error) => {
        this.listError = this.extractError(error) ?? 'Не удалось получить список задач.';
      },
    });
  }

  restartStoppedTasksAsAdmin(): void {
    if (!this.isAdmin || this.adminRestartInProgress) {
      return;
    }

    if (this.isBrowser) {
      const confirmed = window.confirm(
        'Очистить очередь распознавания и перезапустить все остановленные задачи?'
      );
      if (!confirmed) {
        return;
      }
    }

    this.adminRestartInProgress = true;
    this.adminRestartError = null;

    this.transcriptionService.restartStoppedTasksAsAdmin().subscribe({
      next: (response: AdminRestartStoppedTasksResponse) => {
        this.adminRestartInProgress = false;

        const message =
          `Очередь очищена: команд ${response.purgedCommandMessages}, ответов ${response.purgedResponseMessages}. ` +
          `Перезапущено задач: ${response.tasksScheduled} из ${response.tasksFound}.`;

        this.snackBar.open(message, 'OK', { duration: 7000 });
        this.loadTasks(!this.selectedTaskId);
        if (this.selectedTaskId) {
          this.startPolling();
        }
      },
      error: (error) => {
        this.adminRestartInProgress = false;
        this.adminRestartError =
          this.extractError(error) ?? 'Не удалось очистить очередь и перезапустить задачи.';
      },
    });
  }

  onShowAllChange(checked: boolean): void {
    if (!this.isAdmin) {
      return;
    }

    this.showAllTasks = checked;
    this.loadTasks(true);
  }

  selectTask(task: OpenAiTranscriptionTaskDto): void {
    this.selectTaskById(task.id);
  }

  private selectTaskById(taskId: string): void {
    if (this.selectedTaskId === taskId && this.selectedTask) {
      return;
    }

    this.selectedTaskId = taskId;
    this.selectedTask = null;
    this.detailsError = null;
    this.analyticsInProgress = false;
    this.analyticsError = null;
    this.deleteError = null;
    this.deleteInProgress = false;
    this.continueError = null;
    this.continueFromSegmentError = null;
    this.continueFromSegmentInProgress = false;
    this.continueFromSegmentNumber = null;
    this.updateRenderedMarkdown(null);
    this.resetFullscreenState();
    this.startPolling();
    this.scheduleEnsureDetailsPanelVisible();
  }

  private startPolling(): void {
    if (!this.selectedTaskId) {
      return;
    }

    this.stopPolling();
    this.detailsLoading = !this.selectedTask;

    this.pollSubscription = timer(0, 5000)
      .pipe(exhaustMap(() => this.transcriptionService.getTask(this.selectedTaskId!)))
      .subscribe({
        next: (task) => {
          this.detailsLoading = false;
          this.detailsError = null;
          this.applyTaskUpdate(task);
          if (task.done || task.status === OpenAiTranscriptionStatus.Error) {
            this.stopPolling();
          }
        },
        error: (error) => {
          this.detailsLoading = false;
          this.detailsError = this.extractError(error) ?? 'Не удалось получить информацию о задаче.';
          this.stopPolling();
        },
      });
  }

  private stopPolling(): void {
    if (this.pollSubscription) {
      this.pollSubscription.unsubscribe();
      this.pollSubscription = undefined;
    }
  }

  handleUploadSuccess(task: OpenAiTranscriptionTaskDto): void {
    this.limitResponse = null;
    this.loadSubscriptionSummary();
    this.tasks = [task, ...this.tasks.filter((t) => t.id !== task.id)];
    this.selectTaskById(task.id);
  }

  private applyTaskUpdate(task: OpenAiTranscriptionTaskDetailsDto): void {
    this.selectedTask = task;
    this.syncContinueFromSegmentNumber(task);
    this.updateRenderedMarkdown(task);
    this.scheduleEnsureDetailsPanelVisible();

    this.tasks = this.tasks.map((existing) =>
      existing.id === task.id
        ? {
            ...existing,
            status: task.status,
            done: task.done,
            error: task.error,
            modifiedAt: task.modifiedAt,
            segmentsProcessed: task.segmentsProcessed,
            segmentsTotal: task.segmentsTotal,
            requiresDownload: task.requiresDownload,
            clarification: task.clarification,
            createdByEmail: task.createdByEmail ?? existing.createdByEmail,
          }
        : existing
    );
  }

  private scheduleEnsureDetailsPanelVisible(): void {
    if (typeof window === 'undefined') {
      return;
    }

    if (this.ensurePanelVisibleScheduled) {
      return;
    }

    this.ensurePanelVisibleScheduled = true;

    const callback = () => {
      this.ensurePanelVisibleScheduled = false;
      this.ensureDetailsPanelVisible();
    };

    if (typeof window.requestAnimationFrame === 'function') {
      window.requestAnimationFrame(callback);
    } else {
      setTimeout(callback, 0);
    }
  }

  private ensureDetailsPanelVisible(): void {
    const container = this.detailsPanelContainer?.nativeElement;

    if (!container) {
      return;
    }

    const rect = container.getBoundingClientRect();
    const viewportHeight = window.innerHeight || document.documentElement.clientHeight || 0;
    const offset = 24;

    if (rect.top < offset) {
      window.scrollBy({ top: rect.top - offset, behavior: 'smooth' });
      return;
    }

    if (rect.bottom > viewportHeight) {
      window.scrollBy({ top: rect.bottom - viewportHeight + offset, behavior: 'smooth' });
    }
  }

  private updateRenderedMarkdown(task: OpenAiTranscriptionTaskDetailsDto | null): void {
    if (!task) {
      this.renderedMarkdown = null;
      this.markdownSource = '';
      return;
    }

    const markdown = this.getMarkdownContent(task);
    this.markdownSource = markdown ?? '';

    if (markdown) {
      const rendered = this.markdownRenderer.renderMath(markdown);
      this.renderedMarkdown = this.sanitizer.bypassSecurityTrustHtml(rendered);
    } else {
      this.renderedMarkdown = null;
    }
  }

  private getMarkdownContent(task: OpenAiTranscriptionTaskDetailsDto | null): string | null {
    if (!task) {
      return null;
    }

    return task.markdownText || task.processedText || task.recognizedText || null;
  }

  isTaskCompleted(task: OpenAiTranscriptionTaskDetailsDto | null): boolean {
    return !!task && task.status === OpenAiTranscriptionStatus.Done;
  }

  hasMarkdown(): boolean {
    return !!this.markdownSource && this.markdownSource.trim().length > 0;
  }

  get markdownContent(): string {
    return this.markdownSource;
  }

  canDownloadSrt(): boolean {
    return !!this.selectedTask?.hasSegments;
  }

  hasAnyDownloadOption(): boolean {
    return (
      this.isDownloadFormatAvailable('md') ||
      this.isDownloadFormatAvailable('pdf') ||
      this.isDownloadFormatAvailable('docx') ||
      this.isDownloadFormatAvailable('srt')
    );
  }

  isDownloadFormatAvailable(format: 'md' | 'pdf' | 'docx' | 'srt'): boolean {
    switch (format) {
      case 'md':
        return this.hasMarkdown();
      case 'pdf':
      case 'docx':
        return !!this.selectedTaskId && this.hasMarkdown();
      case 'srt':
        return this.canDownloadSrt();
      default:
        return false;
    }
  }

  onDownload(format: 'md' | 'pdf' | 'docx' | 'srt'): void {
    if (this.downloadInProgress || !this.isDownloadFormatAvailable(format)) {
      return;
    }

    this.exportError = null;

    switch (format) {
      case 'md':
        this.downloadMarkdown();
        return;
      case 'pdf':
        this.exportPdf();
        return;
      case 'docx':
        this.exportDocx();
        return;
      case 'srt':
        this.downloadSrt();
        return;
      default:
        return;
    }
  }

  downloadMarkdown(): void {
    if (!this.selectedTask || !this.hasMarkdown()) {
      return;
    }

    this.exportError = null;
    const blob = new Blob([this.markdownSource], { type: 'text/markdown' });
    this.saveBlob(blob, this.buildFileName('md'));
  }

  downloadSrt(): void {
    if (!this.selectedTaskId || !this.selectedTask?.hasSegments || this.downloadingSrt) {
      return;
    }

    this.exportError = null;
    this.downloadingSrt = true;

    this.transcriptionService.exportSrt(this.selectedTaskId).subscribe({
      next: (blob) => {
        this.downloadingSrt = false;
        this.saveBlob(blob, this.buildFileName('srt'));
      },
      error: (error) => {
        this.downloadingSrt = false;
        this.exportError =
          this.extractError(error) ?? 'Не удалось экспортировать SRT.';
      },
    });
  }

  hasAnyCopyOption(): boolean {
    return (
      this.isCopyFormatAvailable('txt') ||
      this.isCopyFormatAvailable('markdown') ||
      this.isCopyFormatAvailable('html') ||
      this.isCopyFormatAvailable('bbcode') ||
      this.isCopyFormatAvailable('srt')
    );
  }

  isCopyFormatAvailable(format: 'txt' | 'markdown' | 'html' | 'bbcode' | 'srt'): boolean {
    switch (format) {
      case 'txt':
        return !!this.getPlainTextContent();
      case 'markdown':
        return this.hasMarkdown();
      case 'html':
        return !!this.getRenderedHtmlContent();
      case 'bbcode':
        return !!this.selectedTaskId && this.hasMarkdown();
      case 'srt':
        return this.canDownloadSrt();
      default:
        return false;
    }
  }

  onCopy(format: 'txt' | 'markdown' | 'html' | 'bbcode' | 'srt'): void {
    if (this.copying || !this.isCopyFormatAvailable(format)) {
      return;
    }

    this.copying = true;
    const finalize = () => {
      this.copying = false;
    };

    const copy = (text: string, successMessage: string, errorMessage?: string) => {
      this.copyTextToClipboard(text, successMessage, errorMessage, finalize);
    };

    switch (format) {
      case 'txt': {
        const plainText = this.getPlainTextContent();
        if (!plainText) {
          finalize();
          return;
        }
        copy(plainText, 'Текст скопирован в буфер обмена');
        return;
      }
      case 'markdown':
        copy(this.markdownSource, 'Markdown скопирован в буфер обмена');
        return;
      case 'html': {
        const htmlContent = this.getRenderedHtmlContent();
        if (!htmlContent) {
          finalize();
          return;
        }
        copy(htmlContent, 'HTML скопирован в буфер обмена');
        return;
      }
      case 'bbcode': {
        if (!this.selectedTaskId) {
          finalize();
          return;
        }
        this.transcriptionService.exportBbcode(this.selectedTaskId).subscribe({
          next: (text) => copy(text, 'BBCode скопирован в буфер обмена'),
          error: (error) => {
            finalize();
            this.handleActionError(error, 'Не удалось подготовить BBCode.');
          },
        });
        return;
      }
      case 'srt': {
        if (!this.selectedTaskId) {
          finalize();
          return;
        }
        this.transcriptionService.exportSrt(this.selectedTaskId).subscribe({
          next: (blob) => {
            blob
              .text()
              .then((text) => {
                copy(text, 'SRT скопирован в буфер обмена');
              })
              .catch((error) => {
                console.error('Blob read error', error);
                finalize();
                this.snackBar.open('Не удалось подготовить SRT.', 'OK', { duration: 3000 });
              });
          },
          error: (error) => {
            finalize();
            this.handleActionError(error, 'Не удалось подготовить SRT.');
          },
        });
        return;
      }
      default:
        finalize();
        return;
    }
  }

  toggleResultFullscreen(): void {
    this.isResultFullscreen = !this.isResultFullscreen;
    this.updateBodyScrollLock();
  }

  private resetFullscreenState(): void {
    if (this.isResultFullscreen) {
      this.isResultFullscreen = false;
      this.updateBodyScrollLock();
    }
  }

  private updateBodyScrollLock(): void {
    if (typeof document === 'undefined') {
      return;
    }

    if (this.isResultFullscreen) {
      if (this.originalBodyOverflow === null) {
        this.originalBodyOverflow = document.body.style.overflow || '';
      }
      document.body.style.overflow = 'hidden';
    } else {
      if (this.originalBodyOverflow !== null) {
        document.body.style.overflow = this.originalBodyOverflow;
        this.originalBodyOverflow = null;
      } else {
        document.body.style.removeProperty('overflow');
      }
    }
  }

  exportPdf(): void {
    if (!this.selectedTaskId || this.exportingPdf) {
      return;
    }

    this.exportError = null;
    this.exportingPdf = true;

    this.transcriptionService.exportPdf(this.selectedTaskId).subscribe({
      next: (blob) => {
        this.exportingPdf = false;
        this.saveBlob(blob, this.buildFileName('pdf'));
      },
      error: (error) => {
        this.exportingPdf = false;
        this.exportError = this.extractError(error) ?? 'Не удалось экспортировать PDF.';
      },
    });
  }

  exportDocx(): void {
    if (!this.selectedTaskId || this.exportingDocx) {
      return;
    }

    this.exportError = null;
    this.exportingDocx = true;

    this.transcriptionService.exportDocx(this.selectedTaskId).subscribe({
      next: (blob) => {
        this.exportingDocx = false;
        this.saveBlob(blob, this.buildFileName('docx'));
      },
      error: (error) => {
        this.exportingDocx = false;
        this.exportError = this.extractError(error) ?? 'Не удалось экспортировать DOCX.';
      },
    });
  }

  private copyTextToClipboard(
    text: string,
    successMessage: string,
    errorMessage = 'Не удалось скопировать в буфер обмена',
    finalize?: () => void
  ): void {
    if (!navigator.clipboard || !navigator.clipboard.writeText) {
      console.error('Clipboard API is not available');
      this.snackBar.open('Буфер обмена недоступен в этом браузере', 'OK', { duration: 3000 });
      finalize?.();
      return;
    }

    navigator.clipboard
      .writeText(text)
      .then(() => {
        this.snackBar.open(successMessage, '', { duration: 2000 });
        finalize?.();
      })
      .catch((error) => {
        console.error('Clipboard error', error);
        this.snackBar.open(errorMessage, 'OK', { duration: 3000 });
        finalize?.();
      });
  }

  private getPlainTextContent(): string | null {
    const processed = this.selectedTask?.processedText?.trim();
    if (processed) {
      return processed;
    }

    const recognized = this.selectedTask?.recognizedText?.trim();
    if (recognized) {
      return recognized;
    }

    const html = this.getRenderedHtmlContent();
    if (!html) {
      return null;
    }

    const tempDiv = document.createElement('div');
    tempDiv.innerHTML = html;
    const plain = (tempDiv.textContent || tempDiv.innerText || '').trim();
    return plain.length > 0 ? plain : null;
  }

  private getRenderedHtmlContent(): string | null {
    if (!this.hasMarkdown()) {
      return null;
    }

    return this.markdownRenderer.renderMath(this.markdownSource);
  }

  private handleActionError(error: unknown, fallback: string): void {
    const message = this.extractError(error) ?? fallback;
    this.snackBar.open(message, 'OK', { duration: 3000 });
  }

  private saveBlob(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.click();
    URL.revokeObjectURL(url);
  }

  private buildFileName(extension: string): string {
    const base = this.selectedTask?.displayName?.trim() || 'transcription';
    return `${this.sanitizeFileName(base)}.${extension}`;
  }

  private sanitizeFileName(value: string): string {
    return value.replace(/[\\/:*?"<>|]/g, '_').replace(/\s+/g, ' ').trim();
  }

  getStatusText(status: OpenAiTranscriptionStatus | null | undefined): string {
    return this.transcriptionService.getStatusText(status);
  }

  getStepStatusText(status: OpenAiTranscriptionStepStatus): string {
    return this.transcriptionService.getStepStatusText(status);
  }

  getTaskIcon(task: OpenAiTranscriptionTaskDto): string {
    if (task.status === OpenAiTranscriptionStatus.Error) {
      return 'error';
    }

    if (task.status === OpenAiTranscriptionStatus.Downloading) {
      return 'cloud_download';
    }

    if (task.done) {
      return 'check_circle';
    }

    return 'graphic_eq';
  }

  getStepIcon(status: OpenAiTranscriptionStepStatus): string {
    switch (status) {
      case OpenAiTranscriptionStepStatus.Completed:
        return 'check_circle';
      case OpenAiTranscriptionStepStatus.InProgress:
        return 'schedule';
      case OpenAiTranscriptionStepStatus.Error:
        return 'error';
      default:
        return 'radio_button_unchecked';
    }
  }

  getStepClass(status: OpenAiTranscriptionStepStatus): string {
    switch (status) {
      case OpenAiTranscriptionStepStatus.Completed:
        return 'step-completed';
      case OpenAiTranscriptionStepStatus.InProgress:
        return 'step-progress';
      case OpenAiTranscriptionStepStatus.Error:
        return 'step-error';
      default:
        return 'step-pending';
    }
  }

  getStatusClass(status: OpenAiTranscriptionStatus): string {
    switch (status) {
      case OpenAiTranscriptionStatus.Done:
        return 'status-done';
      case OpenAiTranscriptionStatus.Error:
        return 'status-error';
      case OpenAiTranscriptionStatus.Downloading:
      case OpenAiTranscriptionStatus.Converting:
      case OpenAiTranscriptionStatus.Transcribing:
      case OpenAiTranscriptionStatus.Segmenting:
      case OpenAiTranscriptionStatus.ProcessingSegments:
      case OpenAiTranscriptionStatus.Formatting:
        return 'status-progress';
      default:
        return 'status-pending';
    }
  }

  getTaskProgressSummary(task: OpenAiTranscriptionTaskDto): string {
    const totalSteps = this.getWorkflowSteps(task).length;
    const completedSteps = this.getApproximateCompletedSteps(task);
    const currentStep = this.getApproximateCurrentStep(task);
    const currentStepText = currentStep ? this.getStatusText(currentStep) : 'Завершено';

    const parts = [`Шаги: ${completedSteps}/${totalSteps}`, `Сейчас: ${currentStepText}`];

    if (currentStep === OpenAiTranscriptionStatus.ProcessingSegments && task.segmentsTotal > 0) {
      parts.push(`Сегменты: ${task.segmentsProcessed}/${task.segmentsTotal}`);
    }

    return parts.join(' · ');
  }

  getCompletedWorkflowSteps(task: OpenAiTranscriptionTaskDetailsDto | null): number {
    if (!task) {
      return 0;
    }

    const steps = this.getWorkflowSteps(task);
    if (task.status === OpenAiTranscriptionStatus.Done) {
      return steps.length;
    }

    const latestSteps = this.getLatestStepMap(task);
    return steps.reduce((count, stepType) => {
      const step = latestSteps.get(stepType);
      return step?.status === OpenAiTranscriptionStepStatus.Completed ? count + 1 : count;
    }, 0);
  }

  getWorkflowStepCount(task: OpenAiTranscriptionTaskDto | OpenAiTranscriptionTaskDetailsDto | null): number {
    return this.getWorkflowSteps(task).length;
  }

  getCurrentWorkflowStep(task: OpenAiTranscriptionTaskDetailsDto | null): OpenAiTranscriptionStatus | null {
    if (!task || task.status === OpenAiTranscriptionStatus.Done) {
      return null;
    }

    const workflowSteps = this.getWorkflowSteps(task);
    const latestSteps = this.getLatestStepMap(task);

    let inProgressStep: OpenAiTranscriptionStepDto | null = null;
    for (const stepType of workflowSteps) {
      const step = latestSteps.get(stepType);
      if (step?.status !== OpenAiTranscriptionStepStatus.InProgress) {
        continue;
      }

      if (!inProgressStep || this.isStepNewer(step, inProgressStep)) {
        inProgressStep = step;
      }
    }

    if (inProgressStep) {
      return inProgressStep.step;
    }

    if (task.status === OpenAiTranscriptionStatus.Error) {
      let errorStep: OpenAiTranscriptionStepDto | null = null;
      for (const stepType of workflowSteps) {
        const step = latestSteps.get(stepType);
        if (step?.status !== OpenAiTranscriptionStepStatus.Error) {
          continue;
        }

        if (!errorStep || this.isStepNewer(step, errorStep)) {
          errorStep = step;
        }
      }

      if (errorStep) {
        return errorStep.step;
      }
    }

    if (workflowSteps.includes(task.status)) {
      return task.status;
    }

    if (task.status === OpenAiTranscriptionStatus.Created) {
      return workflowSteps[0] ?? null;
    }

    return null;
  }

  getCurrentWorkflowStepText(task: OpenAiTranscriptionTaskDetailsDto | null): string {
    if (!task) {
      return 'Неизвестно';
    }

    if (task.status === OpenAiTranscriptionStatus.Done) {
      return 'Все шаги завершены';
    }

    const step = this.getCurrentWorkflowStep(task);
    if (step) {
      return this.getStatusText(step);
    }

    if (task.status === OpenAiTranscriptionStatus.Error) {
      return 'Ожидает восстановления';
    }

    return 'Ожидает запуска';
  }

  shouldShowSegmentProgress(task: OpenAiTranscriptionTaskDetailsDto | null): boolean {
    if (!task || task.segmentsTotal <= 0) {
      return false;
    }

    if (task.status === OpenAiTranscriptionStatus.Error && task.segmentsProcessed < task.segmentsTotal) {
      return true;
    }

    return this.getCurrentWorkflowStep(task) === OpenAiTranscriptionStatus.ProcessingSegments;
  }

  canContinueFromSegment(task: OpenAiTranscriptionTaskDetailsDto | null): boolean {
    return !!task && task.status === OpenAiTranscriptionStatus.Error && task.segmentsTotal > 0;
  }

  isContinueFromSegmentNumberInvalid(task: OpenAiTranscriptionTaskDetailsDto | null): boolean {
    if (!task || !this.canContinueFromSegment(task) || this.continueFromSegmentNumber == null) {
      return false;
    }

    return this.continueFromSegmentNumber < 1 || this.continueFromSegmentNumber > task.segmentsTotal;
  }

  onContinueFromSegmentNumberInput(value: string): void {
    const parsed = Number.parseInt(value, 10);
    this.continueFromSegmentNumber = Number.isFinite(parsed) ? parsed : null;
  }

  continueTaskFromSegment(): void {
    if (
      !this.selectedTaskId ||
      !this.selectedTask ||
      !this.canContinueFromSegment(this.selectedTask) ||
      this.continueFromSegmentInProgress ||
      this.continueInProgress
    ) {
      return;
    }

    const segmentNumber = this.resolveContinueFromSegmentNumber(this.selectedTask);

    this.continueFromSegmentInProgress = true;
    this.continueFromSegmentError = null;
    this.continueError = null;

    this.transcriptionService.continueTaskFromSegment(this.selectedTaskId, segmentNumber).subscribe({
      next: (task) => {
        this.continueFromSegmentInProgress = false;
        this.detailsError = null;
        this.applyTaskUpdate(task);
        this.startPolling();
      },
      error: (error) => {
        this.continueFromSegmentInProgress = false;
        const limit = extractUsageLimitResponse(error);
        if (limit) {
          this.handleUsageLimit(limit);
          return;
        }

        this.continueFromSegmentError =
          this.extractError(error) ?? 'Не удалось восстановить задачу с выбранного сегмента.';
      },
    });
  }

  continueTask(): void {
    if (!this.selectedTaskId || this.continueInProgress || this.continueFromSegmentInProgress) {
      return;
    }

    this.continueInProgress = true;
    this.continueError = null;
    this.continueFromSegmentError = null;

    this.transcriptionService.continueTask(this.selectedTaskId).subscribe({
      next: (task) => {
        this.continueInProgress = false;
        this.detailsError = null;
        this.applyTaskUpdate(task);
        this.startPolling();
      },
      error: (error) => {
        this.continueInProgress = false;
        const limit = extractUsageLimitResponse(error);
        if (limit) {
          this.handleUsageLimit(limit);
          return;
        }
        this.continueError = this.extractError(error) ?? 'Не удалось продолжить задачу.';
      },
    });
  }

  private canDelete(task: OpenAiTranscriptionTaskDetailsDto | null): boolean {
    return (
      !!task &&
      (task.status === OpenAiTranscriptionStatus.Done || task.status === OpenAiTranscriptionStatus.Error)
    );
  }

  canDeleteSelectedTask(): boolean {
    return this.canDelete(this.selectedTask);
  }

  deleteTask(): void {
    if (!this.selectedTaskId || this.deleteInProgress || !this.canDeleteSelectedTask()) {
      return;
    }

    const taskId = this.selectedTaskId;
    this.deleteInProgress = true;
    this.deleteError = null;
    this.continueError = null;
    this.continueFromSegmentError = null;

    this.transcriptionService.deleteTask(taskId).subscribe({
      next: () => {
        this.deleteInProgress = false;
        this.stopPolling();
        this.selectedTaskId = null;
        this.selectedTask = null;
        this.tasks = this.tasks.filter((task) => task.id !== taskId);
        if (this.tasks.length > 0) {
          this.selectTask(this.tasks[0]);
        }
        this.snackBar.open('Задача удалена.', 'OK', { duration: 3000 });
      },
      error: (error) => {
        this.deleteInProgress = false;
        this.deleteError = this.extractError(error) ?? 'Не удалось удалить задачу.';
      },
    });
  }

  openAnalyticsDialog(): void {
    if (!this.selectedTaskId || !this.selectedTask) {
      return;
    }

    const data: OpenAiTranscriptionAnalyticsDialogData = {
      currentProfileId: this.selectedTask.recognitionProfileId ?? null,
      currentClarification: this.selectedTask.clarification ?? null,
    };

    const dialogRef = this.dialog.open<
      OpenAiTranscriptionAnalyticsDialogComponent,
      OpenAiTranscriptionAnalyticsDialogData,
      OpenAiTranscriptionAnalyticsDialogResult | undefined
    >(OpenAiTranscriptionAnalyticsDialogComponent, {
      width: '520px',
      autoFocus: false,
      restoreFocus: false,
      disableClose: true,
      data,
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result && result.recognitionProfileId) {
        this.createAnalyticsTask(result.recognitionProfileId, result.clarification ?? null);
      }
    });
  }

  private createAnalyticsTask(profileId: number, clarification: string | null): void {
    if (!this.selectedTaskId || this.analyticsInProgress) {
      return;
    }

    this.analyticsInProgress = true;
    this.analyticsError = null;

    this.transcriptionService.cloneForAnalytics(this.selectedTaskId, profileId, clarification).subscribe({
      next: (task) => {
        this.analyticsInProgress = false;
        this.handleUploadSuccess(task);
        this.snackBar.open('Создана новая задача с выбранным профилем.', 'Закрыть', {
          duration: 5000,
        });
      },
      error: (error) => {
        this.analyticsInProgress = false;
        const limit = extractUsageLimitResponse(error);
        if (limit) {
          this.handleUsageLimit(limit);
          return;
        }
        this.analyticsError =
          this.extractError(error) ?? 'Не удалось создать задачу с выбранным профилем.';
      },
    });
  }

  trackTask(_: number, task: OpenAiTranscriptionTaskDto): string {
    return task.id;
  }

  trackStep(_: number, step: OpenAiTranscriptionStepDto): number {
    return step.id;
  }

  private getWorkflowSteps(
    task: OpenAiTranscriptionTaskDto | OpenAiTranscriptionTaskDetailsDto | null | undefined
  ): readonly OpenAiTranscriptionStatus[] {
    return task?.requiresDownload ? this.workflowStepsWithDownload : this.workflowStepsWithoutDownload;
  }

  private getLatestStepMap(
    task: OpenAiTranscriptionTaskDetailsDto
  ): Map<OpenAiTranscriptionStatus, OpenAiTranscriptionStepDto> {
    const map = new Map<OpenAiTranscriptionStatus, OpenAiTranscriptionStepDto>();

    for (const step of task.steps ?? []) {
      const existing = map.get(step.step);
      if (!existing || this.isStepNewer(step, existing)) {
        map.set(step.step, step);
      }
    }

    return map;
  }

  private isStepNewer(
    candidate: OpenAiTranscriptionStepDto,
    reference: OpenAiTranscriptionStepDto
  ): boolean {
    const candidateTime = this.getStepTimestamp(candidate.startedAt);
    const referenceTime = this.getStepTimestamp(reference.startedAt);

    if (candidateTime !== referenceTime) {
      return candidateTime > referenceTime;
    }

    return candidate.id > reference.id;
  }

  private getStepTimestamp(value: string | null): number {
    if (!value) {
      return 0;
    }

    const parsed = Date.parse(value);
    return Number.isNaN(parsed) ? 0 : parsed;
  }

  private getApproximateCompletedSteps(task: OpenAiTranscriptionTaskDto): number {
    const total = this.getWorkflowSteps(task).length;
    if (task.status === OpenAiTranscriptionStatus.Done || task.done) {
      return total;
    }

    const completedBeforeProcessing = task.requiresDownload ? 4 : 3;

    switch (task.status) {
      case OpenAiTranscriptionStatus.Created:
        return 0;
      case OpenAiTranscriptionStatus.Downloading:
        return 0;
      case OpenAiTranscriptionStatus.Converting:
        return task.requiresDownload ? 1 : 0;
      case OpenAiTranscriptionStatus.Transcribing:
        return task.requiresDownload ? 2 : 1;
      case OpenAiTranscriptionStatus.Segmenting:
        return task.requiresDownload ? 3 : 2;
      case OpenAiTranscriptionStatus.ProcessingSegments:
        return completedBeforeProcessing;
      case OpenAiTranscriptionStatus.Error:
        if (task.segmentsTotal > 0) {
          return completedBeforeProcessing;
        }
        return 0;
      default:
        return 0;
    }
  }

  private getApproximateCurrentStep(task: OpenAiTranscriptionTaskDto): OpenAiTranscriptionStatus | null {
    if (task.status === OpenAiTranscriptionStatus.Done || task.done) {
      return null;
    }

    const workflowSteps = this.getWorkflowSteps(task);

    if (workflowSteps.includes(task.status)) {
      return task.status;
    }

    if (task.status === OpenAiTranscriptionStatus.Created) {
      return workflowSteps[0] ?? null;
    }

    if (task.status === OpenAiTranscriptionStatus.Error && task.segmentsTotal > 0) {
      return OpenAiTranscriptionStatus.ProcessingSegments;
    }

    return null;
  }

  private syncContinueFromSegmentNumber(task: OpenAiTranscriptionTaskDetailsDto): void {
    if (!this.canContinueFromSegment(task)) {
      this.continueFromSegmentNumber = null;
      return;
    }

    const max = task.segmentsTotal;
    if (
      this.continueFromSegmentNumber == null ||
      this.continueFromSegmentNumber < 1 ||
      this.continueFromSegmentNumber > max
    ) {
      this.continueFromSegmentNumber = this.getSuggestedContinueFromSegment(task);
    }
  }

  private getSuggestedContinueFromSegment(task: OpenAiTranscriptionTaskDetailsDto): number {
    if (task.segmentsTotal <= 0) {
      return 1;
    }

    const nextSegment = task.segmentsProcessed + 1;
    return Math.min(Math.max(nextSegment, 1), task.segmentsTotal);
  }

  private resolveContinueFromSegmentNumber(task: OpenAiTranscriptionTaskDetailsDto): number {
    const max = Math.max(task.segmentsTotal, 1);
    const requested = this.continueFromSegmentNumber ?? this.getSuggestedContinueFromSegment(task);
    const normalized = Math.min(Math.max(requested, 1), max);
    this.continueFromSegmentNumber = normalized;
    return normalized;
  }

  private extractError(error: unknown): string | null {
    if (!error) {
      return null;
    }

    if (typeof error === 'string') {
      return error;
    }

    if (typeof error === 'object') {
      const anyError = error as { error?: unknown; message?: string };
      if (anyError.error) {
        if (typeof anyError.error === 'string') {
          return anyError.error;
        }
        if (typeof anyError.error === 'object') {
          const nested = anyError.error as { message?: string; title?: string };
          return nested.message || nested.title || null;
        }
      }
      return anyError.message ?? null;
    }

    return null;
  }

  private formatRemainingHours(minutes: number | null | undefined): string {
    if (minutes == null) {
      return '0 ч';
    }

    if (minutes >= 2147483647) {
      return 'безлимит ч';
    }

    const hours = Math.max(0, minutes) / 60;
    const formatted = hours.toFixed(1).replace('.', ',').replace(',0', '');
    return `${formatted} ч`;
  }

  private formatRemainingVideos(videos: number | null | undefined): string {
    if (videos == null) {
      return '0 YouTube';
    }

    if (videos >= 2147483647) {
      return 'безлимит YouTube';
    }

    return `${Math.max(0, videos)} YouTube`;
  }
}
