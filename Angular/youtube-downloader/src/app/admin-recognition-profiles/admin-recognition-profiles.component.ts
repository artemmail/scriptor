import { CommonModule } from '@angular/common';
import { Component, OnInit, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AdminWorkspaceComponent } from '../shared/admin-workspace.component';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { finalize } from 'rxjs/operators';
import { RecognitionProfile, RecognitionProfileInput } from '../models/recognition-profile.model';
import { RecognitionProfilesService } from '../services/recognition-profiles.service';
import { Title } from '@angular/platform-browser';

@Component({
  selector: 'app-admin-recognition-profiles',
  standalone: true,
  templateUrl: './admin-recognition-profiles.component.html',
  styleUrls: ['../shared/admin-workspace.css'],
  imports: [
    CommonModule,
    AdminWorkspaceComponent,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSnackBarModule,
    MatProgressSpinnerModule
  ]
})
export class AdminRecognitionProfilesComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(RecognitionProfilesService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly titleService = inject(Title);
  private readonly destroyRef = inject(DestroyRef);

  profiles: RecognitionProfile[] = [];
  loading = false;
  saving = false;
  deletingId: number | null = null;
  error: string | null = null;
  saveError: string | null = null;

  selectedProfile: RecognitionProfile | null = null;
  isCreating = false;

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    displayedName: ['', [Validators.required, Validators.maxLength(200)]],
    hint: ['', [Validators.maxLength(400)]],
    request: ['', [Validators.required, Validators.maxLength(4000)]],
    clarificationTemplate: [''],
    openAiModel: ['', [Validators.required, Validators.maxLength(200)]],
    segmentBlockSize: [600, [Validators.required, Validators.min(1)]]
  });

  ngOnInit(): void {
    this.titleService.setTitle('Админка — профили распознавания');
    this.loadProfiles();
  }

  loadProfiles(): void {
    if (this.loading || this.saving || this.deletingId !== null || this.form.dirty) return;
    this.loading = true;
    this.error = null;

    this.service
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => (this.loading = false)))
      .subscribe({
        next: profiles => {
          this.profiles = profiles;
          if (this.selectedProfile) {
            const updated = profiles.find(p => p.id === this.selectedProfile?.id);
            if (updated) {
              this.selectProfile(updated);
            }
          }
        },
        error: err => {
          console.error('Failed to load recognition profiles', err);
          this.error = 'Не удалось загрузить профили распознавания';
        }
      });
  }

  selectProfile(profile: RecognitionProfile, force = false): void {
    if (!force && (this.saving || this.deletingId !== null)) return;
    if (!force && this.form.dirty && !confirm('Перейти к другому профилю? Несохранённые изменения будут потеряны.')) return;
    this.saveError = null;
    this.selectedProfile = profile;
    this.isCreating = false;
    this.form.setValue({
      name: profile.name,
      displayedName: profile.displayedName,
      hint: profile.hint ?? '',
      request: profile.request,
      clarificationTemplate: profile.clarificationTemplate ?? '',
      openAiModel: profile.openAiModel,
      segmentBlockSize: profile.segmentBlockSize
    });
    this.form.markAsPristine();
    this.form.markAsUntouched();
  }

  startCreate(): void {
    if (this.saving || this.deletingId !== null) return;
    if (this.form.dirty && !confirm('Создать новый профиль? Несохранённые изменения будут потеряны.')) return;
    this.saveError = null;
    this.selectedProfile = null;
    this.isCreating = true;
    this.form.reset({
      name: '',
      displayedName: '',
      hint: '',
      request: '',
      clarificationTemplate: '',
      openAiModel: '',
      segmentBlockSize: 600
    });
  }

  save(): void {
    if (this.saving || this.deletingId !== null) return;
    this.saveError = null;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const payload: RecognitionProfileInput = {
      name: this.form.value.name?.trim() ?? '',
      displayedName: this.form.value.displayedName?.trim() ?? '',
      hint: this.form.value.hint?.trim() || null,
      request: this.form.value.request?.trim() ?? '',
      clarificationTemplate: this.form.value.clarificationTemplate?.trim() || null,
      openAiModel: this.form.value.openAiModel?.trim() ?? '',
      segmentBlockSize: Number(this.form.value.segmentBlockSize)
    };

    if (!payload.name || !payload.displayedName || !payload.request || !payload.openAiModel ||
        !Number.isInteger(payload.segmentBlockSize) || payload.segmentBlockSize < 1) {
      this.saveError = 'Заполните обязательные поля и укажите целый размер блока больше нуля.';
      return;
    }
    this.saving = true;
    const request$ = this.isCreating || !this.selectedProfile
      ? this.service.create(payload)
      : this.service.update(this.selectedProfile.id, payload);

    request$
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => (this.saving = false)))
      .subscribe({
        next: profile => {
          this.snackBar.open('Профиль сохранён', 'OK', { duration: 2500 });
          if (this.isCreating || !this.selectedProfile) {
            this.profiles = [...this.profiles, profile];
          } else {
            this.profiles = this.profiles.map(p => (p.id === profile.id ? profile : p));
          }
          this.selectProfile(profile, true);
        },
        error: err => {
          console.error('Failed to save recognition profile', err);
          const message = err?.error?.message || 'Не удалось сохранить профиль';
          this.saveError = message;
          this.snackBar.open(message, 'Закрыть', { duration: 4000 });
        }
      });
  }

  delete(profile: RecognitionProfile): void {
    if (this.saving || this.deletingId !== null) return;
    if (!confirm(`Удалить профиль «${profile.displayedName}»? Это действие нельзя отменить.`)) {
      return;
    }

    this.saveError = null;
    this.deletingId = profile.id;
    this.service
      .delete(profile.id)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => (this.deletingId = null)))
      .subscribe({
        next: () => {
          this.snackBar.open('Профиль удалён', 'OK', { duration: 2500 });
          this.profiles = this.profiles.filter(p => p.id !== profile.id);
          if (this.selectedProfile?.id === profile.id) {
            this.selectedProfile = null;
            this.isCreating = false;
            this.form.reset({
              name: '',
              displayedName: '',
              hint: '',
              request: '',
              clarificationTemplate: '',
              openAiModel: '',
              segmentBlockSize: 600
            });
          }
        },
        error: err => {
          console.error('Failed to delete recognition profile', err);
          const message = err?.error?.message || 'Не удалось удалить профиль';
          this.saveError = message;
          this.snackBar.open(message, 'Закрыть', { duration: 4000 });
        }
      });
  }

  trackById(_index: number, item: RecognitionProfile): number {
    return item.id;
  }
}
