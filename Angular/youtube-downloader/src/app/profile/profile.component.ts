import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatListModule } from '@angular/material/list';
import { ActivatedRoute, Router } from '@angular/router';
import { PLATFORM_ID } from '@angular/core';

import { take } from 'rxjs/operators';

import { AccountService, GoogleCalendarOperationResponse, GoogleCalendarStatus } from '../services/account.service';
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
  private readonly accountService = inject(AccountService);
  private readonly authService = inject(AuthService);
  private readonly paymentsService = inject(PaymentsService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
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
  readonly user$ = this.authService.user$;
  logoutInProgress = false;
  logoutError = '';
  calendarLoading = false;
  calendarError = '';
  calendarStatus: GoogleCalendarStatus | null = null;
  calendarActionInProgress = false;
  calendarActionError = '';
  calendarActionSuccess = '';
  calendarNotification: { type: 'success' | 'info' | 'error'; text: string } | null = null;
  private readonly platformId = inject(PLATFORM_ID);
  private readonly isBrowser = isPlatformBrowser(this.platformId);

  ngOnInit(): void {
    if (this.isBrowser) {
      this.handleCalendarQueryParams();
      this.fetchProfile();
      this.fetchSummary();
      this.fetchCalendarStatus();
    }
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
    if (!this.isBrowser) {
      return;
    }

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

  fetchCalendarStatus(): void {
    if (!this.isBrowser || this.calendarLoading) {
      return;
    }

    this.calendarLoading = true;
    this.calendarError = '';

    this.accountService.getGoogleCalendarStatus().subscribe({
      next: (status) => {
        this.calendarStatus = status;
        this.calendarLoading = false;
      },
      error: (error) => {
        this.calendarLoading = false;
        this.calendarStatus = null;
        this.calendarError = this.extractCalendarError(error) ??
          'Не удалось получить статус интеграции Google Calendar.';
      }
    });
  }

  private handleCalendarQueryParams(): void {
    if (!this.isBrowser) {
      return;
    }

    this.route.queryParamMap.pipe(take(1)).subscribe(params => {
      const status = params.get('calendarStatus');
      const message = params.get('calendarMessage');
      const error = params.get('calendarError');

      if (!status && !message && !error) {
        return;
      }

      if (status === 'connected') {
        this.calendarNotification = {
          type: 'success',
          text: message || 'Google Calendar подключён.'
        };
      } else if (status === 'unchanged') {
        this.calendarNotification = {
          type: 'info',
          text: message || 'Права доступа уже были предоставлены ранее.'
        };
      } else if (status === 'error') {
        this.calendarNotification = {
          type: 'error',
          text: error || message || 'Не удалось обновить доступ к Google Calendar.'
        };
      } else if (message) {
        this.calendarNotification = { type: 'info', text: message };
      }

      if (error && status !== 'error') {
        this.calendarNotification = { type: 'error', text: error };
      }

      this.clearCalendarQueryParams();
    });
  }

  private clearCalendarQueryParams(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        calendarStatus: null,
        calendarMessage: null,
        calendarError: null
      },
      queryParamsHandling: 'merge',
      replaceUrl: true
    }).catch(() => {
      // ignore navigation errors when cleaning up the url
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

  connectGoogleCalendar(): void {
    if (!this.isBrowser) {
      return;
    }

    this.calendarActionError = '';
    this.calendarActionSuccess = '';
    this.calendarNotification = null;

    const returnUrl = `${window.location.origin}/profile`;
    const url = `/api/account/google-calendar/connect?returnUrl=${encodeURIComponent(returnUrl)}`;
    window.location.href = url;
  }

  disconnectGoogleCalendar(): void {
    if (this.calendarActionInProgress) {
      return;
    }

    this.calendarActionInProgress = true;
    this.calendarActionError = '';
    this.calendarActionSuccess = '';
    this.calendarNotification = null;

    this.accountService.disconnectGoogleCalendar().subscribe({
      next: (response: GoogleCalendarOperationResponse) => {
        this.calendarActionInProgress = false;
        if (response.success) {
          this.calendarActionSuccess = response.message
            || 'Интеграция Google Calendar отключена.';
        } else {
          this.calendarActionError = response.error
            || 'Не удалось отключить интеграцию Google Calendar.';
        }
        this.fetchCalendarStatus();
      },
      error: (error) => {
        this.calendarActionInProgress = false;
        this.calendarActionError = this.extractCalendarError(error)
          || 'Не удалось отключить интеграцию Google Calendar.';
      }
    });
  }

  get calendarChipClass(): string {
    if (this.calendarLoading) {
      return 'status-neutral';
    }

    if (this.calendarError) {
      return 'status-trial';
    }

    const status = this.calendarStatus;
    if (!status) {
      return 'status-neutral';
    }

    if (status.isConnected) {
      return 'status-success';
    }

    if (status.tokensRevokedAt || status.consentDeclinedAt || !status.isGoogleAccount) {
      return 'status-trial';
    }

    return 'status-neutral';
  }

  get calendarStatusMessage(): string {
    if (this.calendarLoading) {
      return 'Проверяем доступ…';
    }

    if (this.calendarError) {
      return this.calendarError;
    }

    const status = this.calendarStatus;
    if (!status) {
      return 'Статус интеграции недоступен.';
    }

    if (status.isConnected) {
      return 'Доступ к Google Calendar разрешён.';
    }

    if (status.tokensRevokedAt) {
      return 'Доступ к Google Calendar отключён.';
    }

    if (status.consentDeclinedAt) {
      return 'Доступ к Google Calendar не подтверждён.';
    }

    return 'Доступ к Google Calendar не подключён.';
  }

  get calendarSecondaryMessage(): string | null {
    const status = this.calendarStatus;
    if (!status) {
      return null;
    }

    if (status.isConnected && status.consentGrantedAt) {
      return `Доступ подтверждён ${this.formatDate(status.consentGrantedAt)}`;
    }

    if (status.tokensRevokedAt) {
      return `Доступ отключён ${this.formatDate(status.tokensRevokedAt)}`;
    }

    if (status.consentDeclinedAt && !status.isConnected) {
      return `Последний отказ: ${this.formatDate(status.consentDeclinedAt)}`;
    }

    return null;
  }

  get showCalendarConnectButton(): boolean {
    return !this.calendarLoading && !!this.calendarStatus && !this.calendarStatus.isConnected;
  }

  get showCalendarDisconnectButton(): boolean {
    return !this.calendarLoading && !!this.calendarStatus?.isConnected;
  }

  get calendarBotWarning(): string | null {
    if (this.calendarStatus && !this.calendarStatus.isGoogleAccount) {
      return 'Управление календарём из бота недоступно для этого аккаунта. Войдите через Google, чтобы активировать интеграцию.';
    }

    return null;
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

  private extractCalendarError(error: unknown): string | null {
    if (!error || typeof error !== 'object') {
      return null;
    }

    const anyError = error as { error?: unknown; message?: string };
    const payload = anyError.error;

    if (typeof payload === 'string') {
      return payload;
    }

    if (payload && typeof payload === 'object') {
      const data = payload as { message?: string; error?: string };
      if (typeof data.error === 'string' && data.error.trim()) {
        return data.error;
      }
      if (typeof data.message === 'string' && data.message.trim()) {
        return data.message;
      }
    }

    if (typeof anyError.message === 'string' && anyError.message.trim()) {
      return anyError.message;
    }

    return null;
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

  logout(): void {
    if (this.logoutInProgress) {
      return;
    }

    this.logoutError = '';
    this.logoutInProgress = true;
    this.authService.logout().subscribe({
      next: () => {
        this.logoutInProgress = false;
        this.router.navigate(['/']).catch(() => {
          this.logoutError = 'Не удалось открыть главную страницу. Обновите страницу вручную.';
        });
      },
      error: () => {
        this.logoutInProgress = false;
        this.logoutError = 'Не удалось выйти из аккаунта. Попробуйте ещё раз.';
      }
    });
  }

  getInitials(value: string | null | undefined): string {
    if (!value) {
      return 'U';
    }

    const parts = value.trim().split(/\s+/).filter(Boolean);
    if (!parts.length) {
      return 'U';
    }

    if (parts.length === 1) {
      return parts[0].substring(0, 2).toUpperCase();
    }

    return (parts[0][0] + parts[1][0]).toUpperCase();
  }
}
