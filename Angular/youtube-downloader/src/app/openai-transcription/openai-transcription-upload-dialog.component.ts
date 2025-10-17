import { CommonModule } from '@angular/common';
import { Component, EventEmitter } from '@angular/core';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { UsageLimitResponse } from '../models/usage-limit-response';
import { OpenAiTranscriptionTaskDto } from '../services/openai-transcription.service';
import { OpenAiTranscriptionUploadFormComponent } from './openai-transcription-upload-form.component';

interface UploadDialogResult {
  task?: OpenAiTranscriptionTaskDto;
  limit?: UsageLimitResponse;
}

@Component({
  selector: 'app-openai-transcription-upload-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, OpenAiTranscriptionUploadFormComponent],
  templateUrl: './openai-transcription-upload-dialog.component.html',
  styleUrls: ['./openai-transcription-upload-dialog.component.css'],
})
export class OpenAiTranscriptionUploadDialogComponent {
  readonly uploadingChange = new EventEmitter<boolean>();

  constructor(
    private readonly dialogRef: MatDialogRef<OpenAiTranscriptionUploadDialogComponent, UploadDialogResult>
  ) {}

  handleCancel(): void {
    this.dialogRef.close();
  }

  handleSuccess(task: OpenAiTranscriptionTaskDto): void {
    this.dialogRef.close({ task });
  }

  handleLimit(limit: UsageLimitResponse): void {
    this.dialogRef.close({ limit });
  }
}
