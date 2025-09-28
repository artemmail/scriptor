import { CommonModule } from '@angular/common';
import { Component, EventEmitter } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { OpenAiTranscriptionService, OpenAiTranscriptionTaskDto } from '../services/openai-transcription.service';

interface UploadDialogResult {
  task: OpenAiTranscriptionTaskDto;
}

@Component({
  selector: 'app-openai-transcription-upload-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    MatTabsModule,
  ],
  templateUrl: './openai-transcription-upload-dialog.component.html',
  styleUrls: ['./openai-transcription-upload-dialog.component.css'],
})
export class OpenAiTranscriptionUploadDialogComponent {
  readonly uploadingChange = new EventEmitter<boolean>();

  selectedTab = 0;
  selectedFile: File | null = null;
  fileUrl = '';
  clarification = '';
  uploading = false;
  uploadError: string | null = null;

  constructor(
    private readonly dialogRef: MatDialogRef<OpenAiTranscriptionUploadDialogComponent, UploadDialogResult>,
    private readonly transcriptionService: OpenAiTranscriptionService
  ) {}

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files && input.files.length > 0 ? input.files[0] : null;
    if (this.selectedFile) {
      this.fileUrl = '';
    }
  }

  onCancel(): void {
    if (this.uploading) {
      return;
    }
    this.dialogRef.close();
  }

  onSubmit(): void {
    if (this.uploading || !this.canSubmit()) {
      return;
    }

    if (this.selectedTab === 0) {
      this.submitFile();
    } else {
      this.submitLink();
    }
  }

  canSubmit(): boolean {
    if (this.selectedTab === 0) {
      return !!this.selectedFile;
    }

    return !!this.fileUrl && this.fileUrl.trim().length > 0;
  }

  private submitFile(): void {
    if (!this.selectedFile) {
      return;
    }

    this.beginUpload();
    this.transcriptionService.upload(this.selectedFile, this.clarification).subscribe({
      next: (task) => this.handleSuccess(task),
      error: (error) => this.handleError(error, 'Не удалось загрузить файл.'),
    });
  }

  private submitLink(): void {
    const trimmed = this.fileUrl.trim();
    if (!trimmed) {
      return;
    }

    this.beginUpload();
    this.transcriptionService.uploadFromUrl(trimmed, this.clarification).subscribe({
      next: (task) => this.handleSuccess(task),
      error: (error) => this.handleError(error, 'Не удалось загрузить файл по ссылке.'),
    });
  }

  private beginUpload(): void {
    this.uploading = true;
    this.uploadError = null;
    this.uploadingChange.emit(true);
  }

  private handleSuccess(task: OpenAiTranscriptionTaskDto): void {
    this.uploading = false;
    this.uploadingChange.emit(false);
    this.dialogRef.close({ task });
  }

  private handleError(error: unknown, fallback: string): void {
    this.uploading = false;
    this.uploadingChange.emit(false);
    this.uploadError = this.extractError(error) ?? fallback;
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
