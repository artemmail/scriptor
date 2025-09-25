import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';

import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { LMarkdownEditorModule } from 'ngx-markdown-editor';

import {
  OpenAiTranscriptionService,
  OpenAiTranscriptionTaskDetailsDto,
} from '../services/openai-transcription.service';

@Component({
  selector: 'app-openai-transcription-markdown-editor',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    LMarkdownEditorModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './openai-transcription-markdown-editor.component.html',
  styleUrls: ['./openai-transcription-markdown-editor.component.css'],
})
export class OpenAiTranscriptionMarkdownEditorComponent implements OnInit, OnDestroy {
  taskId!: string;
  task: OpenAiTranscriptionTaskDetailsDto | null = null;
  markdownContent = '';

  loading = true;
  saving = false;
  reloading = false;
  deleting = false;
  errorMessage: string | null = null;

  editorOptions = {
    placeholder: 'Отредактируйте итоговый Markdown…',
    theme: 'github',
    lineNumbers: true,
    dragDrop: true,
    showPreviewPanel: true,
    hideIcons: [] as string[],
  };

  private paramSubscription?: Subscription;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly title: Title,
    private readonly transcriptionService: OpenAiTranscriptionService,
    private readonly snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.paramSubscription = this.route.paramMap.subscribe((params) => {
      const id = params.get('id');
      if (!id) {
        this.errorMessage = 'Не указан идентификатор задачи.';
        this.loading = false;
        return;
      }

      if (this.taskId === id) {
        return;
      }

      this.taskId = id;
      this.loadTask();
    });
  }

  ngOnDestroy(): void {
    this.paramSubscription?.unsubscribe();
  }

  get canSave(): boolean {
    return !this.saving && !!this.taskId && this.markdownContent.trim().length > 0;
  }

  get canDownloadMarkdown(): boolean {
    return this.markdownContent.trim().length > 0;
  }

  loadTask(showSpinner = true): void {
    if (!this.taskId) {
      return;
    }

    if (showSpinner) {
      this.loading = true;
    }
    this.errorMessage = null;

    this.transcriptionService.getTask(this.taskId).subscribe({
      next: (task) => {
        this.task = task;
        this.markdownContent = task.markdownText ?? '';
        this.loading = false;
        this.reloading = false;
        this.title.setTitle(`Редактирование Markdown: ${task.displayName}`);
      },
      error: (error) => {
        this.loading = false;
        this.reloading = false;
        this.errorMessage = this.extractError(error) ?? 'Не удалось загрузить данные о задаче.';
        this.title.setTitle('Ошибка загрузки Markdown');
      },
    });
  }

  refresh(): void {
    if (this.loading || !this.taskId) {
      return;
    }

    this.reloading = true;
    this.loadTask(false);
  }

  goBack(): void {
    this.router.navigate(['/transcriptions']);
  }

  downloadMarkdown(): void {
    if (!this.canDownloadMarkdown) {
      return;
    }

    const blob = new Blob([this.markdownContent], { type: 'text/markdown;charset=utf-8' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    const fileName = this.task?.displayName || this.taskId || 'transcription-markdown';
    a.download = `${fileName}.md`;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  downloadRecognized(): void {
    if (!this.task?.recognizedText) {
      return;
    }

    const blob = new Blob([this.task.recognizedText], { type: 'text/plain;charset=utf-8' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    const fileName = this.task.displayName || this.taskId || 'transcription';
    a.download = `${fileName}.txt`;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  save(): void {
    if (!this.canSave) {
      return;
    }

    this.saving = true;

    const recognizedText = this.task?.recognizedText ?? null;

    this.transcriptionService
      .updateRecognizedText(this.taskId, recognizedText, this.markdownContent)
      .subscribe({
        next: () => {
          this.saving = false;
          this.snackBar.open('Markdown сохранён', '', { duration: 2000 });
          if (this.task) {
            this.task = {
              ...this.task,
              markdownText: this.markdownContent,
              modifiedAt: new Date().toISOString(),
            };
          }
        },
        error: (error) => {
          this.saving = false;
          const message = this.extractError(error) ?? 'Не удалось сохранить Markdown.';
          this.snackBar.open(message, 'OK', { duration: 3000 });
        },
      });
  }

  deleteTask(): void {
    if (!this.taskId || this.deleting) {
      return;
    }

    const displayName = this.task?.displayName;
    const confirmed = confirm(
      displayName ? `Удалить расшифровку «${displayName}»?` : 'Удалить расшифровку?'
    );
    if (!confirmed) {
      return;
    }

    this.deleting = true;

    this.transcriptionService.deleteTask(this.taskId).subscribe({
      next: () => {
        this.deleting = false;
        this.snackBar.open('Расшифровка удалена', '', { duration: 2000 });
        this.router.navigate(['/transcriptions']);
      },
      error: (error) => {
        this.deleting = false;
        const message = this.extractError(error) ?? 'Не удалось удалить расшифровку.';
        this.snackBar.open(message, 'OK', { duration: 3000 });
      },
    });
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
