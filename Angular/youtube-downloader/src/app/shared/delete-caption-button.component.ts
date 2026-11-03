import { Component, DestroyRef, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { AuthService } from '../services/AuthService.service';
import { RecognizeStatus, SubtitleService } from '../services/subtitle.service';

@Component({
  selector: 'app-delete-caption-button', standalone: true,
  imports: [CommonModule, MatIconModule],
  template: `
    <ng-container *ngIf="isAdmin && terminal">
      <button class="caption-delete" type="button" [disabled]="busy || disabled" (click)="remove()"
        [class.compact]="compact" [attr.aria-label]="'Удалить задачу: ' + (title || taskId)" title="Удалить задачу">
        <mat-icon aria-hidden="true">{{ busy ? 'hourglass_top' : 'delete_outline' }}</mat-icon>
        <span *ngIf="!compact">{{ busy ? 'Удаляем…' : 'Удалить задачу' }}</span>
      </button>
      <p *ngIf="error" class="caption-delete-error" role="alert">{{ error }}</p>
    </ng-container>`,
  styles: [`
    :host { display: block; }
    .caption-delete { display: inline-flex; align-items: center; gap: 8px; padding: 10px 0; border: 0; background: transparent; color: #a14d43; font: inherit; font-size: 12px; cursor: pointer; }
    .caption-delete:hover:not(:disabled) { color: #79372f; }
    .caption-delete:disabled { opacity: .5; cursor: default; }
    .caption-delete:focus-visible { outline: 2px solid #a14d43; outline-offset: 3px; border-radius: 5px; }
    .caption-delete mat-icon { width: 19px; height: 19px; font-size: 19px; }
    .caption-delete.compact { justify-content: center; padding: 0; width: 36px; height: 36px; border-radius: 8px; }
    .caption-delete.compact:hover:not(:disabled) { background: #faefeb; }
    .caption-delete-error { color: #a14d43; font-size: 12px; line-height: 1.5; margin: 6px 0; }
  `]
})
export class DeleteCaptionButtonComponent {
  @Input() taskId = '';
  @Input() title: string | null | undefined = '';
  @Input() status: RecognizeStatus | null | undefined;
  @Input() done = false;
  @Input() compact = false;
  @Input() disabled = false;
  @Output() deleted = new EventEmitter<void>();
  @Output() busyChange = new EventEmitter<boolean>();
  isAdmin = false;
  busy = false;
  error = '';

  constructor(private subtitles: SubtitleService, auth: AuthService, private destroyRef: DestroyRef) {
    auth.user$.pipe(takeUntilDestroyed(destroyRef)).subscribe(user => {
      this.isAdmin = !!user?.roles?.some(role => role.toLowerCase() === 'admin');
    });
  }

  get terminal(): boolean { return this.done || this.status === RecognizeStatus.Done || this.status === RecognizeStatus.Error; }

  remove(): void {
    if (!this.isAdmin || !this.terminal || !this.taskId || this.busy || this.disabled) return;
    if (!window.confirm(`Удалить задачу «${this.title || this.taskId}» и её расшифровку? Восстановить удалённый текст будет нельзя.`)) return;
    this.error = '';
    this.busy = true;
    this.busyChange.emit(true);
    this.subtitles.deleteTask(this.taskId).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => {
      this.busy = false;
      this.busyChange.emit(false);
    })).subscribe({
      next: () => this.deleted.emit(),
      error: error => {
        if (error.status === 404) { this.deleted.emit(); return; }
        this.error = error.status === 401 || error.status === 403
          ? 'Нет прав на удаление. Войдите в аккаунт администратора.'
          : 'Не удалось удалить задачу. Попробуйте снова.';
      }
    });
  }
}
