import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import {
  OpenAiRecognitionProfileOptionDto,
  OpenAiTranscriptionService,
  OpenAiTranscriptionTaskDto,
} from '../services/openai-transcription.service';
import { UsageLimitResponse, extractUsageLimitResponse } from '../models/usage-limit-response';

type UploadLayout = 'dialog' | 'card';

@Component({
  selector: 'app-openai-transcription-upload-form',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressBarModule,
    MatTabsModule,
  ],
  templateUrl: './openai-transcription-upload-form.component.html',
  styleUrls: ['./openai-transcription-upload-form.component.css'],
})
export class OpenAiTranscriptionUploadFormComponent implements OnInit {
  @Input() layout: UploadLayout = 'dialog';
  @Output() cancel = new EventEmitter<void>();
  @Output() success = new EventEmitter<OpenAiTranscriptionTaskDto>();
  @Output() limit = new EventEmitter<UsageLimitResponse>();
  @Output() uploadingChange = new EventEmitter<boolean>();

  readonly layouts = {
    dialog: 'dialog' as UploadLayout,
    card: 'card' as UploadLayout,
  };

  private readonly defaultClarificationPlaceholder =
    'Например: отметить важных спикеров или уточнить терминологию';

  selectedTab = 0;
  selectedFile: File | null = null;
  fileUrl = '';
  clarification = '';
  clarificationPlaceholder = this.defaultClarificationPlaceholder;
  clarificationHint: string | null = null;
  uploading = false;
  uploadError: string | null = null;
  limitResponse: UsageLimitResponse | null = null;
  profiles: OpenAiRecognitionProfileOptionDto[] = [];
  profilesLoading = false;
  profilesError: string | null = null;
  selectedProfileId: number | null = null;
  dragOver = false;

  get isDialogLayout(): boolean {
    return this.layout === this.layouts.dialog;
  }

  get selectedProfile(): OpenAiRecognitionProfileOptionDto | undefined {
    if (this.selectedProfileId == null) {
      return undefined;
    }
    return this.profiles.find((item) => item.id === this.selectedProfileId);
  }

  constructor(private readonly transcriptionService: OpenAiTranscriptionService) {}

  ngOnInit(): void {
    this.loadRecognitionProfiles();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files && input.files.length > 0 ? input.files[0] : null;
    if (this.selectedFile) {
      this.fileUrl = '';
    }
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    if (this.uploading || this.profilesLoading) {
      return;
    }

    const files = event.dataTransfer?.files;
    if (!files || files.length === 0) {
      this.dragOver = false;
      return;
    }

    this.selectedFile = files[0];
    this.fileUrl = '';
    this.selectedTab = 0;
    this.dragOver = false;
  }

  clearLink(input: HTMLInputElement): void {
    if (this.uploading || this.profilesLoading) {
      return;
    }

    this.fileUrl = '';
    input.focus();
  }

  onDragOver(event: DragEvent): void {
    if (this.uploading || this.profilesLoading) {
      return;
    }
    event.preventDefault();
    this.dragOver = true;
  }

  onDragLeave(event: DragEvent): void {
    if (!event.currentTarget) {
      this.dragOver = false;
      return;
    }

    const target = event.currentTarget as HTMLElement;
    const related = event.relatedTarget as Node | null;
    if (!related || !target.contains(related)) {
      this.dragOver = false;
    }
  }

  onCancel(): void {
    if (this.uploading) {
      return;
    }

    if (this.limitResponse) {
      this.limit.emit(this.limitResponse);
      return;
    }

    this.cancel.emit();
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
    if (this.profilesLoading || this.selectedProfileId == null) {
      return false;
    }

    if (this.selectedTab === 0) {
      return !!this.selectedFile;
    }

    return !!this.fileUrl && this.fileUrl.trim().length > 0;
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

  confirmLimit(): void {
    if (this.limitResponse) {
      this.limit.emit(this.limitResponse);
    }
  }

  private submitFile(): void {
    if (!this.selectedFile) {
      return;
    }

    this.beginUpload();
    const profileId = this.selectedProfileId;
    if (profileId == null) {
      this.handleError('Не выбран профиль распознавания.', 'Не удалось загрузить файл.');
      return;
    }

    this.transcriptionService.upload(this.selectedFile, profileId, this.clarification).subscribe({
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
    const profileId = this.selectedProfileId;
    if (profileId == null) {
      this.handleError('Не выбран профиль распознавания.', 'Не удалось загрузить файл по ссылке.');
      return;
    }

    this.transcriptionService.uploadFromUrl(trimmed, profileId, this.clarification).subscribe({
      next: (task) => this.handleSuccess(task),
      error: (error) => this.handleError(error, 'Не удалось загрузить файл по ссылке.'),
    });
  }

  private beginUpload(): void {
    this.uploading = true;
    this.uploadError = null;
    this.limitResponse = null;
    this.uploadingChange.emit(true);
  }

  private handleSuccess(task: OpenAiTranscriptionTaskDto): void {
    this.uploading = false;
    this.uploadingChange.emit(false);
    this.success.emit(task);
  }

  private handleError(error: unknown, fallback: string): void {
    this.uploading = false;
    this.uploadingChange.emit(false);
    const limit = extractUsageLimitResponse(error);
    if (limit) {
      this.limitResponse = limit;
      this.uploadError = limit.message;
      return;
    }

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

  private loadRecognitionProfiles(): void {
    this.profilesLoading = true;
    this.profilesError = null;
    this.transcriptionService.listRecognitionProfiles().subscribe({
      next: (profiles) => {
        this.profilesLoading = false;
        this.profiles = profiles;
        this.profilesError = profiles.length === 0 ? 'Нет доступных профилей распознавания.' : null;
        const hasCurrent = profiles.some((profile) => profile.id === this.selectedProfileId);
        this.selectedProfileId = hasCurrent ? this.selectedProfileId : null;
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
    const profile = this.selectedProfile;
    if (profile?.clarificationTemplate) {
      this.clarificationHint = profile.clarificationTemplate;
      this.clarificationPlaceholder = profile.clarificationTemplate.replace('{clarification}', 'ваше уточнение');
    } else {
      this.clarificationHint = null;
      this.clarificationPlaceholder = this.defaultClarificationPlaceholder;
    }
  }
}
