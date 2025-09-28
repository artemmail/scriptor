import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { Subscription, timer } from 'rxjs';
import { exhaustMap } from 'rxjs/operators';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
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

@Component({
  selector: 'app-openai-transcriptions',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatListModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    FormsModule,
    LocalTimePipe,
    RouterModule,
  ],
  templateUrl: './openai-transcription.component.html',
  styleUrls: ['./openai-transcription.component.css'],
})
export class OpenAiTranscriptionComponent implements OnInit, OnDestroy {
  tasks: OpenAiTranscriptionTaskDto[] = [];
  selectedTaskId: string | null = null;
  selectedTask: OpenAiTranscriptionTaskDetailsDto | null = null;

  selectedFile: File | null = null;
  fileUrl = '';
  clarification = '';
  uploading = false;
  uploadError: string | null = null;
  listError: string | null = null;
  detailsError: string | null = null;
  detailsLoading = false;
  continueError: string | null = null;
  continueInProgress = false;
  exportError: string | null = null;
  exportingPdf = false;
  exportingDocx = false;
  renderedMarkdown: SafeHtml | null = null;
  private markdownSource = '';

  readonly OpenAiTranscriptionStatus = OpenAiTranscriptionStatus;
  readonly OpenAiTranscriptionStepStatus = OpenAiTranscriptionStepStatus;

  private pollSubscription?: Subscription;

  constructor(
    private readonly transcriptionService: OpenAiTranscriptionService,
    private readonly markdownRenderer: MarkdownRendererService1,
    private readonly sanitizer: DomSanitizer
  ) {}

  ngOnInit(): void {
    this.loadTasks(true);
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files && input.files.length > 0 ? input.files[0] : null;
    if (this.selectedFile) {
      this.fileUrl = '';
    }
  }

  upload(): void {
    if (!this.selectedFile || this.uploading) {
      return;
    }

    this.uploading = true;
    this.uploadError = null;

    this.transcriptionService
      .upload(this.selectedFile, this.clarification)
      .subscribe({
        next: (task) => this.handleUploadSuccess(task),
        error: (error) => {
          this.uploading = false;
          this.uploadError = this.extractError(error) ?? 'Не удалось загрузить файл.';
        },
      });
  }

  uploadFromUrl(): void {
    const trimmedUrl = this.fileUrl.trim();
    if (!trimmedUrl || this.uploading) {
      return;
    }

    this.uploading = true;
    this.uploadError = null;
    this.selectedFile = null;

    this.transcriptionService.uploadFromUrl(trimmedUrl, this.clarification).subscribe({
      next: (task) => this.handleUploadSuccess(task),
      error: (error) => {
        this.uploading = false;
        this.uploadError =
          this.extractError(error) ?? 'Не удалось загрузить файл по ссылке.';
      },
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
    this.updateRenderedMarkdown(null);
    this.startPolling();
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

  private handleUploadSuccess(task: OpenAiTranscriptionTaskDto): void {
    this.uploading = false;
    this.uploadError = null;
    this.selectedFile = null;
    this.fileUrl = '';
    this.clarification = '';
    this.tasks = [task, ...this.tasks.filter((t) => t.id !== task.id)];
    this.selectTaskById(task.id);
  }

  private applyTaskUpdate(task: OpenAiTranscriptionTaskDetailsDto): void {
    this.selectedTask = task;
    this.updateRenderedMarkdown(task);

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
    if (!this.selectedTask || !this.selectedTask.segments?.length) {
      return false;
    }

    return this.selectedTask.segments.some(
      (segment) => segment.startSeconds != null && segment.endSeconds != null
    );
  }

  downloadMarkdown(): void {
    if (!this.selectedTask || !this.hasMarkdown()) {
      return;
    }

    const blob = new Blob([this.markdownSource], { type: 'text/markdown' });
    this.saveBlob(blob, this.buildFileName('md'));
  }

  downloadSrt(): void {
    if (!this.selectedTask || !this.canDownloadSrt()) {
      return;
    }

    const segments = this.selectedTask.segments
      .filter((segment) => segment.startSeconds != null && segment.endSeconds != null)
      .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));

    const lines = segments.map((segment, index) => {
      const text = (segment.processedText || segment.text || '').trim();
      const start = this.formatSrtTime(segment.startSeconds!);
      const end = this.formatSrtTime(segment.endSeconds!);

      return `${index + 1}\n${start} --> ${end}\n${text}\n`;
    });

    if (!lines.length) {
      return;
    }

    const blob = new Blob([lines.join('\n')], { type: 'text/plain' });
    this.saveBlob(blob, this.buildFileName('srt'));
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

  private formatSrtTime(totalSeconds: number): string {
    const clamped = Math.max(0, totalSeconds);
    const wholeSeconds = Math.floor(clamped);
    const fractional = clamped - wholeSeconds;
    const hours = Math.floor(wholeSeconds / 3600);
    const minutes = Math.floor((wholeSeconds % 3600) / 60);
    const seconds = wholeSeconds % 60;
    const milliseconds = Math.floor(fractional * 1000);

    const pad = (value: number, length = 2) => value.toString().padStart(length, '0');
    const padMs = (value: number) => value.toString().padStart(3, '0');

    return `${pad(hours)}:${pad(minutes)}:${pad(seconds)},${padMs(milliseconds)}`;
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
        this.continueError = this.extractError(error) ?? 'Не удалось продолжить задачу.';
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
