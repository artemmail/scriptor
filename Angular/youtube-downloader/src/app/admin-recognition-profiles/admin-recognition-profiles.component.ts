import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
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
  styleUrls: ['./admin-recognition-profiles.component.css'],
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatTableModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatSnackBarModule,
    MatProgressSpinnerModule
  ]
})
export class AdminRecognitionProfilesComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(RecognitionProfilesService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly titleService = inject(Title);

  profiles: RecognitionProfile[] = [];
  displayedColumns = ['id', 'name', 'displayedName', 'hint', 'openAiModel', 'segmentBlockSize', 'request', 'actions'];
  loading = false;
  saving = false;
  deletingId: number | null = null;
  error: string | null = null;

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
    this.loading = true;
    this.error = null;

    this.service
      .list()
      .pipe(finalize(() => (this.loading = false)))
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

  selectProfile(profile: RecognitionProfile): void {
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
  }

  startCreate(): void {
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

    this.saving = true;
    const request$ = this.isCreating || !this.selectedProfile
      ? this.service.create(payload)
      : this.service.update(this.selectedProfile.id, payload);

    request$
      .pipe(finalize(() => (this.saving = false)))
      .subscribe({
        next: profile => {
          this.snackBar.open('Профиль сохранён', 'OK', { duration: 2500 });
          if (this.isCreating || !this.selectedProfile) {
            this.profiles = [...this.profiles, profile];
          } else {
            this.profiles = this.profiles.map(p => (p.id === profile.id ? profile : p));
          }
          this.selectProfile(profile);
        },
        error: err => {
          console.error('Failed to save recognition profile', err);
          const message = err?.error?.message || 'Не удалось сохранить профиль';
          this.snackBar.open(message, 'Закрыть', { duration: 4000 });
        }
      });
  }

  delete(profile: RecognitionProfile): void {
    if (!confirm(`Удалить профиль #${profile.id}?`)) {
      return;
    }

    this.deletingId = profile.id;
    this.service
      .delete(profile.id)
      .pipe(finalize(() => (this.deletingId = null)))
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
          this.snackBar.open(message, 'Закрыть', { duration: 4000 });
        }
      });
  }

  trackById(_index: number, item: RecognitionProfile): number {
    return item.id;
  }
}
