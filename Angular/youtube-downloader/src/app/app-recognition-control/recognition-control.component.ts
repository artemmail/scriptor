import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';

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
  title = 'YouScriptor Транскрибация лекций в документ с разметкой (word/pdf)';
  isStarting = false;
  startError: string | null = null;
  limitResponse: UsageLimitResponse | null = null;
  remainingQuota: number | null = null;
  summary: SubscriptionSummary | null = null;
  summaryLoading = false;
  summaryError: string | null = null;

  constructor(
    private recognitionService: SubtitleService,
    private titleService: Title,
    private router: Router,
    private paymentsService: PaymentsService
  ) {
    this.titleService.setTitle(this.title);
  }

  ngOnInit(): void {
    this.loadSubscriptionSummary();
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
          this.remainingQuota = response.remainingQuota ?? null;
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
            const limit = err.status === 402 ? extractUsageLimitResponse(err) : null;
            if (limit) {
              this.limitResponse = limit;
              this.remainingQuota = limit.remainingQuota ?? null;
            } else {
              console.error('Error starting task:', err);
              this.startError = 'Не удалось запустить задачу. Попробуйте позже.';
            }
          }
        },
      });
  }

  loadSubscriptionSummary(): void {
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

    if (this.summary.hasActiveSubscription) {
      const planName = this.summary.planName || 'Подписка активна';
      if (this.summary.endsAt) {
        const ends = new Date(this.summary.endsAt).toLocaleDateString('ru-RU');
        return `${planName} до ${ends}`;
      }
      return planName;
    }

    return `Бесплатный тариф: ${this.summary.freeRecognitionsPerDay} YouTube-распознавания в день и ${this.summary.freeTranscriptionsPerMonth} транскрибации в месяц`;
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
}
