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
  selector: 'app-openai-transcription-editor',
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
  templateUrl: './openai-transcription-editor.component.html',
  styleUrls: ['./openai-transcription-editor.component.css'],
})
export class OpenAiTranscriptionEditorComponent implements OnInit, OnDestroy {
  taskId!: string;
  task: OpenAiTranscriptionTaskDetailsDto | null = null;
  recognizedContent = '';

  loading = true;
  saving = false;
  reloading = false;
  errorMessage: string | null = null;

  editorOptions = {
    placeholder: 'Отредактируйте текст расшифровки…',
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
    return !this.saving && !!this.taskId && this.recognizedContent.trim().length > 0;
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
        this.recognizedContent = task.recognizedText ?? '';
        this.loading = false;
        this.reloading = false;
        this.title.setTitle(`Редактирование расшифровки: ${task.displayName}`);
      },
      error: (error) => {
        this.loading = false;
        this.reloading = false;
        this.errorMessage = this.extractError(error) ?? 'Не удалось загрузить данные о задаче.';
        this.title.setTitle('Ошибка загрузки расшифровки');
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

  download(): void {
    if (!this.recognizedContent) {
      return;
    }

    const blob = new Blob([this.recognizedContent], { type: 'text/plain;charset=utf-8' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    const fileName = this.task?.displayName || this.taskId || 'transcription';
    a.download = `${fileName}.txt`;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  save(): void {
    if (!this.canSave) {
      return;
    }

    this.saving = true;

    this.transcriptionService
      .updateRecognizedText(this.taskId, this.recognizedContent)
      .subscribe({
        next: () => {
          this.saving = false;
          this.snackBar.open('Расшифровка сохранена', '', { duration: 2000 });
          if (this.task) {
            this.task = {
              ...this.task,
              recognizedText: this.recognizedContent,
              modifiedAt: new Date().toISOString(),
            };
          }
        },
        error: (error) => {
          this.saving = false;
          const message = this.extractError(error) ?? 'Не удалось сохранить изменения.';
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
