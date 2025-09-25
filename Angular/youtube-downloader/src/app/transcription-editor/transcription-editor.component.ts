import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { LMarkdownEditorModule } from 'ngx-markdown-editor';
import {
  OpenAiTranscriptionService,
  OpenAiTranscriptionTaskDetailsDto
} from '../services/openai-transcription.service';
import { MarkdownRendererService1 } from '../task-result/markdown-renderer.service';

@Component({
  selector: 'app-transcription-editor',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    LMarkdownEditorModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './transcription-editor.component.html',
  styleUrls: ['./transcription-editor.component.css']
})
export class TranscriptionEditorComponent implements OnInit {
  taskId!: string;
  task: OpenAiTranscriptionTaskDetailsDto | null = null;
  markdownContent = '';
  loading = true;
  errorMessage: string | null = null;
  saving = false;
  deleting = false;
  exporting = false;

  editorOptions = {
    placeholder: 'Пишите Markdown и LaTeX: $…$ или $$…$$',
    katex: true,
    theme: 'github',
    lineNumbers: true,
    dragDrop: true,
    showPreviewPanel: true,
    hideIcons: []
  };

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly titleService: Title,
    private readonly transcriptionService: OpenAiTranscriptionService,
    private readonly snackBar: MatSnackBar,
    private readonly markdownRenderer: MarkdownRendererService1
  ) {
    this.renderWithMath = this.renderWithMath.bind(this);
  }

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      const id = params.get('id');
      if (id) {
        this.taskId = id;
        this.loadTask();
      }
    });
  }

  renderWithMath(content: string): string {
    return this.markdownRenderer.renderMath(content);
  }

  private loadTask(): void {
    this.loading = true;
    this.errorMessage = null;

    this.transcriptionService.getTask(this.taskId).subscribe({
      next: task => {
        this.task = task;
        this.markdownContent = task.markdownText || task.processedText || task.recognizedText || '';
        this.loading = false;
        this.titleService.setTitle(`Расшифровка: ${task.displayName || this.taskId}`);
      },
      error: error => {
        this.loading = false;
        this.errorMessage = this.extractError(error) ?? 'Не удалось загрузить задачу.';
        this.titleService.setTitle('Ошибка загрузки');
      }
    });
  }

  onDownloadMd(): void {
    if (!this.hasContent() || this.exporting) {
      return;
    }

    const blob = new Blob([this.markdownContent], { type: 'text/markdown' });
    this.triggerDownload(blob, `${this.getFileBaseName()}.md`);
  }

  onDownloadPdf(): void {
    if (!this.hasContent() || this.exporting) {
      return;
    }

    this.exporting = true;
    this.transcriptionService.exportPdf(this.taskId).subscribe({
      next: blob => {
        this.triggerDownload(blob, `${this.getFileBaseName()}.pdf`);
        this.exporting = false;
      },
      error: error => {
        this.exporting = false;
        this.handleActionError(error, 'Не удалось сформировать PDF файл.');
      }
    });
  }

  onDownloadDocx(): void {
    if (!this.hasContent() || this.exporting) {
      return;
    }

    this.exporting = true;
    this.transcriptionService.exportDocx(this.taskId).subscribe({
      next: blob => {
        this.triggerDownload(blob, `${this.getFileBaseName()}.docx`);
        this.exporting = false;
      },
      error: error => {
        this.exporting = false;
        this.handleActionError(error, 'Не удалось сформировать DOCX файл.');
      }
    });
  }

  onCopyBbcode(): void {
    if (!this.hasContent() || this.exporting) {
      return;
    }

    this.exporting = true;
    this.transcriptionService.exportBbcode(this.taskId).subscribe({
      next: text => {
        if (!navigator.clipboard || !navigator.clipboard.writeText) {
          console.error('Clipboard API is not available');
          this.snackBar.open('Буфер обмена недоступен в этом браузере', 'OK', { duration: 3000 });
          this.exporting = false;
          return;
        }

        navigator.clipboard.writeText(text).then(() => {
          this.snackBar.open('BBCode скопирован в буфер обмена', '', { duration: 2000 });
          this.exporting = false;
        }).catch(err => {
          console.error('Clipboard error', err);
          this.snackBar.open('Не удалось скопировать в буфер обмена', 'OK', { duration: 3000 });
          this.exporting = false;
        });
      },
      error: error => {
        this.exporting = false;
        this.handleActionError(error, 'Не удалось подготовить BBCode.');
      }
    });
  }

  onSave(): void {
    if (!this.hasContent() || this.saving) {
      return;
    }

    this.saving = true;
    this.transcriptionService.updateMarkdown(this.taskId, this.markdownContent).subscribe({
      next: () => {
        this.saving = false;
        this.snackBar.open('Расшифровка сохранена', '', { duration: 2000 });
      },
      error: error => {
        this.saving = false;
        this.handleActionError(error, 'Ошибка при сохранении расшифровки.');
      }
    });
  }

  onDelete(): void {
    if (this.deleting) {
      return;
    }

    const confirmed = confirm('Удалить эту расшифровку?');
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
      error: error => {
        this.deleting = false;
        this.handleActionError(error, 'Не удалось удалить расшифровку.');
      }
    });
  }

  hasContent(): boolean {
    return !!this.markdownContent && this.markdownContent.trim().length > 0;
  }

  private triggerDownload(blob: Blob, filename: string): void {
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  private getFileBaseName(): string {
    const candidate = this.task?.displayName?.trim() || this.task?.fileName?.trim() || this.taskId;
    return candidate.replace(/[\\/:*?"<>|]+/g, '_');
  }

  private handleActionError(error: unknown, fallback: string): void {
    const message = this.extractError(error) ?? fallback;
    this.snackBar.open(message, 'OK', { duration: 3000 });
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
