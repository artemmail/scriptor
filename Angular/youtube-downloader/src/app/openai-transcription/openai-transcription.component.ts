import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Subscription, timer } from 'rxjs';
import { exhaustMap } from 'rxjs/operators';
import { RouterModule } from '@angular/router';
import { LMarkdownEditorModule } from 'ngx-markdown-editor';
import {
  OpenAiTranscriptionService,
  OpenAiTranscriptionStatus,
  OpenAiTranscriptionTaskDetailsDto,
  OpenAiTranscriptionTaskDto,
  OpenAiTranscriptionStepDto,
  OpenAiTranscriptionStepStatus,
} from '../services/openai-transcription.service';
import { LocalTimePipe } from '../pipe/local-time.pipe';

@Component({
  selector: 'app-openai-transcriptions',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatListModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    LMarkdownEditorModule,
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
  uploading = false;
  uploadError: string | null = null;
  listError: string | null = null;
  detailsError: string | null = null;
  detailsLoading = false;
  continueError: string | null = null;
  continueInProgress = false;

  markdownEditing = false;
  markdownDraft = '';
  markdownSaving = false;
  markdownError: string | null = null;
  deleteInProgress = false;
  markdownEditorOptions = {
    placeholder: 'Отредактируйте итоговый Markdown…',
    theme: 'github',
    lineNumbers: true,
    dragDrop: true,
    showPreviewPanel: true,
    hideIcons: [] as string[],
  };

  readonly OpenAiTranscriptionStatus = OpenAiTranscriptionStatus;
  readonly OpenAiTranscriptionStepStatus = OpenAiTranscriptionStepStatus;

  private pollSubscription?: Subscription;

  constructor(
    private readonly transcriptionService: OpenAiTranscriptionService,
    private readonly snackBar: MatSnackBar,
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
  }

  upload(): void {
    if (!this.selectedFile || this.uploading) {
      return;
    }

    this.uploading = true;
    this.uploadError = null;

    this.transcriptionService.upload(this.selectedFile).subscribe({
      next: (task) => {
        this.uploading = false;
        this.selectedFile = null;
        this.tasks = [task, ...this.tasks.filter((t) => t.id !== task.id)];
        this.selectTaskById(task.id);
      },
      error: (error) => {
        this.uploading = false;
        this.uploadError = this.extractError(error) ?? 'Не удалось загрузить файл.';
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
          this.resetMarkdownState();
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
    this.resetMarkdownState();
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

  private applyTaskUpdate(task: OpenAiTranscriptionTaskDetailsDto): void {
    this.selectedTask = task;

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
          }
        : existing
    );
  }

  private resetMarkdownState(): void {
    this.markdownEditing = false;
    this.markdownDraft = '';
    this.markdownSaving = false;
    this.markdownError = null;
    this.deleteInProgress = false;
  }

  get canEditMarkdown(): boolean {
    return !!this.selectedTask && this.selectedTask.done && !this.markdownSaving && !this.deleteInProgress;
  }

  get canSaveMarkdown(): boolean {
    return this.markdownEditing && !this.markdownSaving && this.markdownDraft.trim().length > 0;
  }

  get canDownloadMarkdown(): boolean {
    const content = this.markdownEditing ? this.markdownDraft : this.selectedTask?.markdownText;
    return !!content && content.trim().length > 0;
  }

  get canDeleteTask(): boolean {
    return !!this.selectedTaskId && !this.deleteInProgress && !this.markdownSaving;
  }

  startMarkdownEditing(): void {
    if (!this.canEditMarkdown) {
      return;
    }

    this.markdownDraft = this.selectedTask?.markdownText ?? '';
    this.markdownError = null;
    this.markdownEditing = true;
  }

  cancelMarkdownEditing(): void {
    if (this.markdownSaving) {
      return;
    }

    this.markdownEditing = false;
    this.markdownDraft = '';
    this.markdownError = null;
  }

  saveMarkdown(): void {
    if (!this.canSaveMarkdown || !this.selectedTaskId) {
      return;
    }

    this.markdownSaving = true;
    this.markdownError = null;

    const markdownText = this.markdownDraft;
    const recognizedText = this.selectedTask?.recognizedText ?? null;

    this.transcriptionService
      .updateRecognizedText(this.selectedTaskId, recognizedText, markdownText)
      .subscribe({
        next: () => {
          this.markdownSaving = false;
          this.markdownEditing = false;
          const updatedAt = new Date().toISOString();
          if (this.selectedTask) {
            this.selectedTask = {
              ...this.selectedTask,
              markdownText,
              modifiedAt: updatedAt,
            };
          }
          this.tasks = this.tasks.map((existing) =>
            existing.id === this.selectedTaskId
              ? { ...existing, modifiedAt: updatedAt }
              : existing
          );
          this.markdownDraft = '';
          this.snackBar.open('Markdown сохранён', '', { duration: 2000 });
        },
        error: (error) => {
          this.markdownSaving = false;
          const message = this.extractError(error) ?? 'Не удалось сохранить Markdown.';
          this.markdownError = message;
          this.snackBar.open(message, 'OK', { duration: 3000 });
        },
      });
  }

  downloadMarkdown(): void {
    const content = this.markdownEditing
      ? this.markdownDraft
      : this.selectedTask?.markdownText ?? '';

    if (!content.trim()) {
      return;
    }

    const blob = new Blob([content], { type: 'text/markdown;charset=utf-8' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    const fileName = this.selectedTask?.displayName || this.selectedTaskId || 'transcription';
    a.download = `${fileName}.md`;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  deleteTask(): void {
    if (!this.selectedTaskId || this.deleteInProgress) {
      return;
    }

    const displayName = this.selectedTask?.displayName;
    const confirmed = confirm(
      displayName ? `Удалить расшифровку «${displayName}»?` : 'Удалить расшифровку?'
    );

    if (!confirmed) {
      return;
    }

    this.deleteInProgress = true;
    this.markdownError = null;

    const taskId = this.selectedTaskId;

    this.transcriptionService.deleteTask(taskId).subscribe({
      next: () => {
        this.deleteInProgress = false;
        this.snackBar.open('Расшифровка удалена', '', { duration: 2000 });
        this.stopPolling();
        this.tasks = this.tasks.filter((task) => task.id !== taskId);
        this.selectedTaskId = null;
        this.selectedTask = null;
        this.resetMarkdownState();
        this.detailsError = null;
        this.detailsLoading = false;
        this.loadTasks(true);
      },
      error: (error) => {
        this.deleteInProgress = false;
        const message = this.extractError(error) ?? 'Не удалось удалить расшифровку.';
        this.markdownError = message;
        this.snackBar.open(message, 'OK', { duration: 3000 });
      },
    });
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
