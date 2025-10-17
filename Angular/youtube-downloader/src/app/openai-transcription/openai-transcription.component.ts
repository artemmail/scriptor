import { Component, ElementRef, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule } from '@angular/material/menu';
import { MatButtonModule } from '@angular/material/button';
import { Router, RouterModule } from '@angular/router';
import { Subscription, timer } from 'rxjs';
import { exhaustMap } from 'rxjs/operators';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import {
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
import {
  OpenAiTranscriptionAnalyticsDialogComponent,
  OpenAiTranscriptionAnalyticsDialogData,
  OpenAiTranscriptionAnalyticsDialogResult,
} from './openai-transcription-analytics-dialog.component';
import { PaymentsService, SubscriptionSummary } from '../services/payments.service';
import { UsageLimitResponse, extractUsageLimitResponse } from '../models/usage-limit-response';

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
    LocalTimePipe,
    RouterModule,
    ActionMenuPanelDirective,
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

  readonly OpenAiTranscriptionStatus = OpenAiTranscriptionStatus;
  readonly OpenAiTranscriptionStepStatus = OpenAiTranscriptionStepStatus;
  readonly heroHighlights = [
    'Скорость обработки — 1 час записи за 3 минуты',
    'Поддержка 78 языков и автоматические тайм-коды',
    'Удобный редактор с командной работой и AI-помощником',
  ];

  private pollSubscription?: Subscription;
  private ensurePanelVisibleScheduled = false;

  get downloadInProgress(): boolean {
    return this.exportingPdf || this.exportingDocx || this.downloadingSrt;
  }

  constructor(
    private readonly transcriptionService: OpenAiTranscriptionService,
    private readonly markdownRenderer: MarkdownRendererService1,
    private readonly sanitizer: DomSanitizer,
    private readonly router: Router,
    private readonly dialog: MatDialog,
    private readonly snackBar: MatSnackBar,
    private readonly paymentsService: PaymentsService
  ) {}

  ngOnInit(): void {
    this.loadSubscriptionSummary();
    this.loadTasks(true);
  }

  loadSubscriptionSummary(): void {
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

    if (this.summary.hasLifetimeAccess || this.summary.isLifetime) {
      return 'Безлимитный доступ активен';
    }

    if (this.summary.hasActiveSubscription) {
      const planName = this.summary.planName || 'Подписка активна';
      if (this.summary.endsAt) {
        const ends = new Date(this.summary.endsAt).toLocaleDateString('ru-RU');
        return `${planName} до ${ends}`;
      }
      return planName;
    }

    return `Бесплатный тариф: ${this.summary.freeTranscriptionsPerMonth} транскрибаций в месяц и ${this.summary.freeRecognitionsPerDay} распознавания YouTube в день`;
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
    this.transcriptionService.list().subscribe({
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
            clarification: task.clarification,
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

  continueTask(): void {
    if (!this.selectedTaskId || this.continueInProgress) {
      return;
    }

    this.continueInProgress = true;
    this.continueError = null;

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
}
