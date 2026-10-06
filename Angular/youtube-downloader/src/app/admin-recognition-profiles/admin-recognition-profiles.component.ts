import { CommonModule } from '@angular/common';
import { Component, OnInit, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormsModule, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { finalize } from 'rxjs/operators';
import { RecognitionProfile, RecognitionProfileInput } from '../models/recognition-profile.model';
import { RecognitionProfilesService } from '../services/recognition-profiles.service';
import { AdminMenuComponent } from '../shared/admin-menu.component';
import { Title } from '@angular/platform-browser';

interface ProfileFormValue {
  name: string;
  displayedName: string;
  hint: string;
  request: string;
  clarificationTemplate: string;
  openAiModel: string;
  segmentBlockSize: number;
}

const trimmedRequired: ValidatorFn = control =>
  typeof control.value === 'string' && control.value.trim() ? null : { required: true };
const positiveInteger: ValidatorFn = control =>
  Number.isInteger(control.value) && control.value >= 1 && control.value <= 2147483647
    ? null : { positiveInteger: true };

@Component({
  selector: 'app-admin-recognition-profiles',
  standalone: true,
  templateUrl: './admin-recognition-profiles.component.html',
  styleUrls: ['../shared/account-page.css', './admin-recognition-profiles.component.css'],
  imports: [CommonModule, FormsModule, ReactiveFormsModule, RouterModule, MatIconModule, MatProgressSpinnerModule, AdminMenuComponent]
})
export class AdminRecognitionProfilesComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(RecognitionProfilesService);
  private readonly titleService = inject(Title);
  private readonly destroyRef = inject(DestroyRef);
  private readonly drafts = new Map<number, ProfileFormValue>();
  private newDraft: ProfileFormValue | null = null;

  readonly clarificationToken = '{clarification}';
  profiles: RecognitionProfile[] = [];
  search = '';
  loading = false;
  saving = false;
  deletingId: number | null = null;
  error: string | null = null;
  saveError: string | null = null;
  success: string | null = null;
  selectedProfile: RecognitionProfile | null = null;
  pendingDelete: RecognitionProfile | null = null;
  isCreating = false;
  clarificationExpanded = false;

  readonly form = this.fb.nonNullable.group({
    name: ['', [trimmedRequired, Validators.maxLength(200)]],
    displayedName: ['', [trimmedRequired, Validators.maxLength(200)]],
    hint: ['', [Validators.maxLength(400)]],
    request: ['', [trimmedRequired, Validators.maxLength(4000)]],
    clarificationTemplate: [''],
    openAiModel: ['', [trimmedRequired, Validators.maxLength(200)]],
    segmentBlockSize: [600, [positiveInteger]]
  });

  get busy(): boolean { return this.loading || this.saving || this.deletingId !== null; }
  get canReload(): boolean { return !this.busy && this.draftCount === 0; }
  get filteredProfiles(): RecognitionProfile[] {
    const query = this.search.trim().toLocaleLowerCase('ru');
    return this.profiles.filter(profile => !query || [profile.displayedName, profile.name, profile.hint, profile.openAiModel]
      .some(value => value?.toLocaleLowerCase('ru').includes(query)));
  }
  get visibleSelectedId(): number | null {
    return this.filteredProfiles.some(profile => profile.id === this.selectedProfile?.id) ? this.selectedProfile!.id : null;
  }
  get modelCount(): number { return new Set(this.profiles.map(profile => profile.openAiModel)).size; }
  get draftCount(): number {
    const ids = new Set<number | 'new'>(this.drafts.keys());
    if (this.newDraft) ids.add('new');
    if (this.form.dirty && (this.selectedProfile || this.isCreating)) ids.add(this.isCreating ? 'new' : this.selectedProfile!.id);
    return ids.size;
  }
  get hasNewDraft(): boolean { return !!this.newDraft || (this.isCreating && this.form.dirty); }

  ngOnInit(): void {
    this.titleService.setTitle('Профили обработки — управление YouScriptor');
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.saveError = null;
      this.success = null;
      this.pendingDelete = null;
      const initial = this.selectedProfile ? this.profileValue(this.selectedProfile) : this.emptyValue();
      if (JSON.stringify(this.form.getRawValue()) === JSON.stringify(initial)) {
        this.form.markAsPristine();
        if (this.isCreating) this.newDraft = null;
        else if (this.selectedProfile) this.drafts.delete(this.selectedProfile.id);
      }
    });
    this.loadProfiles();
  }

  loadProfiles(): void {
    if (!this.canReload) return;
    this.loading = true;
    this.error = null;
    this.service.list().pipe(takeUntilDestroyed(this.destroyRef), finalize(() => (this.loading = false))).subscribe({
      next: profiles => {
        this.profiles = profiles ?? [];
        if (this.isCreating) return;
        const selected = this.profiles.find(profile => profile.id === this.selectedProfile?.id) ?? this.profiles[0];
        if (selected) this.activateProfile(selected);
        else this.activateNew();
      },
      error: () => { this.error = 'Не удалось загрузить профили. Попробуйте обновить список.'; }
    });
  }

  selectProfile(profile: RecognitionProfile): void {
    if (this.busy || profile.id === this.selectedProfile?.id) return;
    this.stashCurrent();
    this.activateProfile(profile);
  }

  selectById(id: number): void {
    const profile = this.profiles.find(item => item.id === id);
    if (profile) this.selectProfile(profile);
  }

  startCreate(): void {
    if (this.busy || this.isCreating) return;
    this.stashCurrent();
    this.activateNew();
  }

  hasDraft(profile: RecognitionProfile): boolean {
    return this.drafts.has(profile.id) || (this.selectedProfile?.id === profile.id && this.form.dirty);
  }

  resetChanges(): void {
    if (this.busy) return;
    if (this.selectedProfile) {
      this.drafts.delete(this.selectedProfile.id);
      this.activateProfile(this.selectedProfile);
    } else {
      this.newDraft = null;
      this.activateNew();
    }
  }

  fieldInvalid(name: keyof ProfileFormValue): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.dirty || control.touched);
  }

  save(): void {
    if (this.busy || (!this.isCreating && (!this.selectedProfile || !this.form.dirty))) return;
    this.pendingDelete = null;
    this.saveError = null;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const payload: RecognitionProfileInput = {
      name: value.name.trim(),
      displayedName: value.displayedName.trim(),
      hint: value.hint.trim() || null,
      request: value.request.trim(),
      clarificationTemplate: value.clarificationTemplate.trim() || null,
      openAiModel: value.openAiModel.trim(),
      segmentBlockSize: value.segmentBlockSize
    };
    const creating = this.isCreating;
    this.saving = true;
    const request$ = creating ? this.service.create(payload) : this.service.update(this.selectedProfile!.id, payload);
    request$.pipe(takeUntilDestroyed(this.destroyRef), finalize(() => (this.saving = false))).subscribe({
      next: profile => {
        this.profiles = creating ? [...this.profiles, profile] : this.profiles.map(item => item.id === profile.id ? profile : item);
        if (creating) this.newDraft = null;
        else this.drafts.delete(profile.id);
        this.activateProfile(profile);
        this.success = creating ? 'Новый профиль создан.' : 'Изменения профиля сохранены.';
      },
      error: err => { this.saveError = this.errorMessage(err, 'Не удалось сохранить профиль. Изменения остаются в редакторе.'); }
    });
  }

  requestDelete(): void {
    if (this.busy || !this.selectedProfile) return;
    this.saveError = null;
    this.success = null;
    this.pendingDelete = this.selectedProfile;
  }

  cancelDelete(): void { if (!this.busy) this.pendingDelete = null; }

  deleteSelected(): void {
    const profile = this.pendingDelete;
    if (this.busy || !profile || profile.id !== this.selectedProfile?.id) return;
    this.deletingId = profile.id;
    this.saveError = null;
    this.service.delete(profile.id).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => (this.deletingId = null))).subscribe({
      next: () => {
        this.profiles = this.profiles.filter(item => item.id !== profile.id);
        this.drafts.delete(profile.id);
        if (this.profiles.length) this.activateProfile(this.profiles[0]);
        else this.activateNew();
        this.success = 'Профиль «' + profile.displayedName + '» удалён.';
      },
      error: err => { this.saveError = this.errorMessage(err, 'Не удалось удалить профиль. Попробуйте ещё раз.'); }
    });
  }

  trackById(_index: number, item: RecognitionProfile): number { return item.id; }

  private stashCurrent(): void {
    if (this.isCreating) this.newDraft = this.form.dirty ? this.form.getRawValue() : null;
    else if (this.selectedProfile) {
      if (this.form.dirty) this.drafts.set(this.selectedProfile.id, this.form.getRawValue());
      else this.drafts.delete(this.selectedProfile.id);
    }
  }

  private activateProfile(profile: RecognitionProfile): void {
    this.selectedProfile = profile;
    this.isCreating = false;
    const draft = this.drafts.get(profile.id);
    this.setForm(draft ?? this.profileValue(profile), !!draft);
  }

  private activateNew(): void {
    this.selectedProfile = null;
    this.isCreating = true;
    this.setForm(this.newDraft ?? this.emptyValue(), !!this.newDraft);
  }

  private setForm(value: ProfileFormValue, dirty: boolean): void {
    this.form.reset(value, { emitEvent: false });
    if (dirty) this.form.markAsDirty();
    this.clarificationExpanded = !!value.clarificationTemplate;
    this.saveError = null;
    this.success = null;
    this.pendingDelete = null;
  }

  private emptyValue(): ProfileFormValue {
    return { name: '', displayedName: '', hint: '', request: '', clarificationTemplate: '', openAiModel: '', segmentBlockSize: 600 };
  }

  private profileValue(profile: RecognitionProfile): ProfileFormValue {
    return { name: profile.name, displayedName: profile.displayedName, hint: profile.hint ?? '', request: profile.request,
      clarificationTemplate: profile.clarificationTemplate ?? '', openAiModel: profile.openAiModel, segmentBlockSize: profile.segmentBlockSize };
  }

  private errorMessage(error: unknown, fallback: string): string {
    const response = error as { error?: { message?: unknown } | string };
    if (typeof response?.error === 'string' && response.error) return response.error;
    if (typeof response?.error === 'object' && typeof response.error?.message === 'string') return response.error.message;
    return fallback;
  }
}
