import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';

import { AccountService } from '../services/account.service';
import { AuthService } from '../services/AuthService.service';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    RouterLink
  ],
  templateUrl: './profile.component.html',
  styleUrls: ['./profile.component.css']
})
export class ProfileComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  readonly form = this.fb.nonNullable.group({
    displayName: ['', [Validators.required, Validators.maxLength(100)]]
  });

  loading = false;
  saving = false;
  loadError = '';
  saveError = '';
  saveSuccess = false;

  constructor(
    private readonly accountService: AccountService,
    private readonly authService: AuthService
  ) {}

  ngOnInit(): void {
    this.fetchProfile();
  }

  get displayNameValue(): string {
    return this.form.controls.displayName.value.trim();
  }

  fetchProfile(): void {
    if (this.loading) {
      return;
    }

    this.loading = true;
    this.loadError = '';
    this.accountService.getProfile().subscribe({
      next: profile => {
        this.form.setValue({ displayName: profile.displayName });
        this.loading = false;
      },
      error: () => {
        this.loadError = 'Не удалось загрузить профиль. Попробуйте позже.';
        this.loading = false;
      }
    });
  }

  submit(): void {
    if (this.form.invalid || this.saving) {
      this.form.markAllAsTouched();
      return;
    }

    const displayName = this.form.controls.displayName.value.trim();
    if (!displayName) {
      this.form.controls.displayName.setErrors({ required: true });
      return;
    }

    this.saving = true;
    this.saveError = '';
    this.saveSuccess = false;

    this.accountService.updateProfile({ displayName }).subscribe({
      next: profile => {
        this.authService.updateDisplayName(profile.displayName);
        this.saveSuccess = true;
        this.saving = false;
      },
      error: () => {
        this.saveError = 'Не удалось сохранить изменения. Попробуйте позже.';
        this.saving = false;
      }
    });
  }
}
