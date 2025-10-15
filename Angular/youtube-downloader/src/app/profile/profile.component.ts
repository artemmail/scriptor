import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatListModule } from '@angular/material/list';
import { Router } from '@angular/router';

import { AccountService } from '../services/account.service';
import { AuthService } from '../services/AuthService.service';
import { PaymentsService, SubscriptionSummary } from '../services/payments.service';

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
    MatListModule
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
  summaryLoading = false;
  summaryError = '';
  summary: SubscriptionSummary | null = null;

  constructor(
    private readonly accountService: AccountService,
    private readonly authService: AuthService,
    private readonly paymentsService: PaymentsService,
    private readonly router: Router
  ) {}

  ngOnInit(): void {
    this.fetchProfile();
    this.fetchSummary();
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

  fetchSummary(): void {
    if (this.summaryLoading) {
      return;
    }

    this.summaryLoading = true;
    this.summaryError = '';
    this.paymentsService.getSubscriptionSummary().subscribe({
      next: (summary) => {
        this.summaryLoading = false;
        this.summary = summary;
      },
      error: () => {
        this.summaryLoading = false;
        this.summaryError = 'Не удалось загрузить сведения о подписке.';
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

  get subscriptionChipClass(): string {
    if (!this.summary) {
      return 'status-neutral';
    }

    if (this.summary.hasLifetimeAccess || this.summary.isLifetime) {
      return 'status-success';
    }

    if (this.summary.hasActiveSubscription) {
      return 'status-active';
    }

    return 'status-trial';
  }

  get subscriptionStatusMessage(): string {
    if (!this.summary) {
      return '';
    }

    if (this.summary.hasLifetimeAccess || this.summary.isLifetime) {
      return 'Безлимитный доступ активен';
    }

    if (this.summary.hasActiveSubscription) {
      const planName = this.summary.planName || 'Подписка активна';
      if (this.summary.endsAt) {
        const ends = new Date(this.summary.endsAt).toLocaleDateString('ru-RU');
        return `${planName} до ${ends}`;
      }
      return planName;
    }

    return `Бесплатный тариф: ${this.summary.freeRecognitionsPerDay} распознавания YouTube в день и ${this.summary.freeTranscriptionsPerMonth} транскрибации в месяц`;
  }

  get billingUrl(): string {
    return this.summary?.billingUrl || '/billing';
  }

  navigateToBilling(): void {
    const url = this.billingUrl;
    if (!url) {
      return;
    }

    if (url.startsWith('http')) {
      window.open(url, '_blank');
      return;
    }

    this.router.navigateByUrl(url);
  }

  formatAmount(amount: number, currency: string): string {
    try {
      return new Intl.NumberFormat('ru-RU', { style: 'currency', currency }).format(amount);
    } catch {
      return `${amount.toFixed(2)} ${currency}`;
    }
  }

  formatInvoiceStatus(status: string | number): string {
    const normalized = typeof status === 'number' ? status : Number(status);
    switch (normalized) {
      case 0:
        return 'Черновик';
      case 1:
        return 'Выставлен';
      case 2:
        return 'Оплачен';
      case 3:
        return 'Отменён';
      case 4:
        return 'Ошибка оплаты';
      default:
        return typeof status === 'string' ? status : 'Неизвестно';
    }
  }

  formatDate(value: string | null | undefined): string {
    if (!value) {
      return '';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }

    return new Intl.DateTimeFormat('ru-RU', {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    }).format(date);
  }
}
