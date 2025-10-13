import { CommonModule } from '@angular/common';
import { Component, Inject, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import {
  OpenAiRecognitionProfileOptionDto,
  OpenAiTranscriptionService,
} from '../services/openai-transcription.service';

export interface OpenAiTranscriptionAnalyticsDialogData {
  currentProfileId: number | null;
  currentClarification: string | null;
}

export interface OpenAiTranscriptionAnalyticsDialogResult {
  recognitionProfileId: number;
  clarification: string | null;
}

@Component({
  selector: 'app-openai-transcription-analytics-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressBarModule,
  ],
  templateUrl: './openai-transcription-analytics-dialog.component.html',
  styleUrls: ['./openai-transcription-analytics-dialog.component.css'],
})
export class OpenAiTranscriptionAnalyticsDialogComponent implements OnInit {
  readonly defaultClarificationPlaceholder =
    'Например: выделить тезисы, отметить спикеров или уточнить формат отчёта';

  profiles: OpenAiRecognitionProfileOptionDto[] = [];
  profilesLoading = false;
  profilesError: string | null = null;
  selectedProfileId: number | null = null;
  clarification = '';
  clarificationPlaceholder = this.defaultClarificationPlaceholder;
  clarificationHint: string | null = null;

  constructor(
    private readonly dialogRef: MatDialogRef<
      OpenAiTranscriptionAnalyticsDialogComponent,
      OpenAiTranscriptionAnalyticsDialogResult | undefined
    >,
    private readonly transcriptionService: OpenAiTranscriptionService,
    @Inject(MAT_DIALOG_DATA) private readonly data: OpenAiTranscriptionAnalyticsDialogData | null
  ) {}

  ngOnInit(): void {
    this.clarification = this.data?.currentClarification ?? '';
    this.loadProfiles();
  }

  onCancel(): void {
    if (this.profilesLoading) {
      return;
    }
    this.dialogRef.close();
  }

  onConfirm(): void {
    if (!this.canSubmit()) {
      return;
    }

    const clarification = this.clarification?.trim();
    this.dialogRef.close({
      recognitionProfileId: this.selectedProfileId!,
      clarification: clarification ? clarification : null,
    });
  }

  canSubmit(): boolean {
    return !this.profilesLoading && this.selectedProfileId != null;
  }

  onProfileSelectionChanged(rawValue: unknown): void {
    let profileId: number | null = null;
    if (typeof rawValue === 'number' && Number.isFinite(rawValue)) {
      profileId = rawValue;
    } else if (typeof rawValue === 'string') {
      const parsed = Number(rawValue);
      profileId = Number.isFinite(parsed) ? parsed : null;
    }

    this.selectedProfileId = profileId;
    this.applySelectedProfile();
  }

  private loadProfiles(): void {
    this.profilesLoading = true;
    this.profilesError = null;
    this.transcriptionService.listRecognitionProfiles().subscribe({
      next: (profiles) => {
        this.profilesLoading = false;
        this.profiles = profiles;
        if (profiles.length === 0) {
          this.profilesError = 'Нет доступных профилей распознавания.';
          this.selectedProfileId = null;
        } else {
          const currentId = this.data?.currentProfileId ?? null;
          const alternative = profiles.find((profile) => profile.id !== currentId);
          this.selectedProfileId = (alternative ?? profiles[0]).id;
        }
        this.applySelectedProfile();
      },
      error: (error) => {
        this.profilesLoading = false;
        this.profilesError = this.extractError(error) ?? 'Не удалось загрузить профили распознавания.';
        this.selectedProfileId = null;
        this.applySelectedProfile();
      },
    });
  }

  private applySelectedProfile(): void {
    const profile = this.profiles.find((item) => item.id === this.selectedProfileId);
    if (profile?.clarificationTemplate) {
      this.clarificationHint = profile.clarificationTemplate;
      this.clarificationPlaceholder = profile.clarificationTemplate.replace(
        '{clarification}',
        'ваше уточнение'
      );
    } else {
      this.clarificationHint = null;
      this.clarificationPlaceholder = this.defaultClarificationPlaceholder;
    }
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
