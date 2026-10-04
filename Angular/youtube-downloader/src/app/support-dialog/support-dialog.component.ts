import { CommonModule } from '@angular/common';
import { Component, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AuthService, UserInfo } from '../services/AuthService.service';

@Component({
  selector: 'app-support-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatIconModule, MatProgressBarModule],
  templateUrl: './support-dialog.component.html',
  styleUrls: ['./support-dialog.component.css']
})
export class SupportDialogComponent {
  readonly messageTypes = [
    'Вопрос по работе сервиса',
    'Проблема с распознаванием аудио',
    'Проблема с YouTube или субтитрами',
    'Проблема со скачиванием или экспортом',
    'Вопрос по оплате или подписке',
    'Предложение по улучшению',
    'Другое'
  ];
  readonly maxFileSize = 10 * 1024 * 1024;
  user: UserInfo | null = null;
  messageType = this.messageTypes[0];
  header = '';
  text = '';
  uploadedFile: File | null = null;
  sending = false;
  sent = false;
  error = '';
  fileError = '';

  constructor(
    private readonly http: HttpClient,
    private readonly dialogRef: MatDialogRef<SupportDialogComponent>,
    private readonly router: Router,
    auth: AuthService,
    destroyRef: DestroyRef
  ) {
    auth.user$.pipe(takeUntilDestroyed(destroyRef)).subscribe(user => this.user = user);
  }

  get canSubmit(): boolean {
    return !!this.user && !this.sending && !this.sent && !this.fileError
      && this.messageTypes.includes(this.messageType)
      && !!this.header.trim() && this.header.length <= 200
      && !!this.text.trim() && this.text.length <= 10000;
  }

  onFileChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.selectFile(file);
    input.value = '';
  }

  onPaste(event: ClipboardEvent): void {
    const image = Array.from(event.clipboardData?.files ?? []).find(file => file.type.startsWith('image/'));
    if (image && !this.sending) {
      event.preventDefault();
      this.selectFile(image);
    }
  }

  removeFile(): void {
    this.uploadedFile = null;
    this.fileError = '';
  }

  private selectFile(file: File): void {
    this.uploadedFile = null;
    this.fileError = file.size > this.maxFileSize ? 'Размер вложения не должен превышать 10 МБ.' : '';
    if (!this.fileError) this.uploadedFile = file;
  }

  submit(): void {
    if (!this.canSubmit) return;
    const data = new FormData();
    data.append('MessageType', this.messageType);
    data.append('Header', this.header.trim());
    data.append('Text', this.text.trim());
    if (this.uploadedFile) data.append('UploadedFile', this.uploadedFile, this.uploadedFile.name);

    this.error = '';
    this.sending = true;
    this.dialogRef.disableClose = true;
    this.http.post('/api/support', data).subscribe({
      next: () => {
        this.sending = false;
        this.sent = true;
        this.dialogRef.disableClose = false;
      },
      error: (error: HttpErrorResponse) => {
        this.sending = false;
        this.dialogRef.disableClose = false;
        this.error = error.status === 413
          ? 'Вложение слишком большое. Максимальный размер — 10 МБ.'
          : error.status === 401 || error.status === 419
            ? 'Войдите в аккаунт, чтобы отправить сообщение.'
            : error.error?.message || 'Не удалось отправить сообщение. Текст сохранён в форме — попробуйте ещё раз.';
      }
    });
  }

  login(): void {
    this.dialogRef.close();
    this.router.navigate(['/login']);
  }
}
