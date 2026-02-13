import { Component, OnInit, inject } from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';
import { PLATFORM_ID } from '@angular/core';

import { SubtitleService } from '../services/subtitle.service';
import { PaymentsService, SubscriptionSummary } from '../services/payments.service';
import { UsageLimitResponse, extractUsageLimitResponse } from '../models/usage-limit-response';
import { YandexAdComponent } from '../ydx-ad/yandex-ad.component';

@Component({
  selector: 'app-recognition-control',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterModule,
    YandexAdComponent
  ],
  templateUrl: './recognition-control.component.html',
  styleUrls: ['./recognition-control.component.css']
})
export class RecognitionControlComponent implements OnInit {
  inputValue: string = '';
  title = 'YouScriptor — Расшифровка YouTube и встреч';
  isStarting = false;
  startError: string | null = null;
  limitResponse: UsageLimitResponse | null = null;
  remainingVideos: number | null = null;
  summary: SubscriptionSummary | null = null;
  summaryLoading = false;
  summaryError: string | null = null;
  private readonly platformId = inject(PLATFORM_ID);
  private readonly isBrowser = isPlatformBrowser(this.platformId);

  constructor(
    private recognitionService: SubtitleService,
    private titleService: Title,
    private router: Router,
    private paymentsService: PaymentsService
  ) {
    this.titleService.setTitle(this.title);
  }

  ngOnInit(): void {
    if (this.isBrowser) {
      this.loadSubscriptionSummary();
    }
  }

  onStart(): void {
    if (!this.inputValue.trim()) {
      return;
    }

    if (this.isStarting) {
      return;
    }

    this.isStarting = true;
    this.startError = null;
    this.limitResponse = null;

    this.recognitionService
      .startSubtitleRecognition(this.inputValue, 'user')
      .subscribe({
        next: (response) => {
          this.isStarting = false;
          this.remainingVideos = response.remainingVideos ?? response.remainingQuota ?? null;
          if (response?.taskId) {
            this.router.navigate(['/recognized', response.taskId]);
          }
        },
        error: (err: HttpErrorResponse) => {
          this.isStarting = false;
          if (err.status === 401) {
            // при 401 перенаправляем на /login
            this.router.navigate(['/login']);
          } else {
            const limit = extractUsageLimitResponse(err);
            if (limit) {
              this.limitResponse = limit;
              this.remainingVideos = limit.remainingVideos ?? limit.remainingQuota ?? null;
            } else {
              console.error('Error starting task:', err);
              this.startError = 'Не удалось запустить задачу. Попробуйте позже.';
            }
          }
        },
      });
  }

  loadSubscriptionSummary(): void {
    if (!this.isBrowser) {
      return;
    }

    this.summaryLoading = true;
    this.summaryError = null;
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

  get subscriptionStatusMessage(): string {
    if (!this.summary) {
      return '';
    }

    if (this.summary.hasLifetimeAccess || this.summary.isLifetime) {
      return 'Безлимитный доступ активен';
    }

    const planName = this.summary.planName || (this.summary.hasActiveSubscription ? 'Пакет активен' : 'Стартовый пакет');
    if (this.summary.endsAt) {
      const ends = new Date(this.summary.endsAt).toLocaleDateString('ru-RU');
      return `${planName} до ${ends}`;
    }

    return `${planName}: ${this.formatMinutes(this.summary.remainingTranscriptionMinutes)} и ${this.summary.remainingVideos} видео`;
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

  get billingUrl(): string {
    if (this.limitResponse?.paymentUrl) {
      return this.limitResponse.paymentUrl;
    }

    if (this.summary?.billingUrl) {
      return this.summary.billingUrl;
    }

    return '/billing';
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

  formatMinutes(minutes: number | null | undefined): string {
    if (minutes == null) {
      return '0 мин';
    }

    if (minutes >= 2147483647) {
      return 'безлимит минут';
    }

    if (minutes < 60) {
      return `${minutes} мин`;
    }

    const hours = (minutes / 60).toFixed(1).replace('.', ',');
    return `${hours} ч`;
  }
}
