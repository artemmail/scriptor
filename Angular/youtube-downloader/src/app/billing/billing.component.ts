import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Subject, takeUntil } from 'rxjs';
import { Title } from '@angular/platform-browser';
import {
  PaymentsService,
  SubscriptionPlan,
  SubscriptionBillingPeriod,
  WalletBalance
} from '../services/payments.service';

@Component({
  selector: 'app-billing',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    ReactiveFormsModule,
    MatCardModule,
    MatIconModule,
    MatListModule,
    MatSnackBarModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './billing.component.html',
  styleUrls: ['./billing.component.css']
})
export class BillingComponent implements OnInit, OnDestroy {
  private readonly destroy$ = new Subject<void>();

  readonly depositControl = new FormControl<number | null>(500, {
    nonNullable: false,
    validators: [Validators.required, Validators.min(100), Validators.max(100000)]
  });

  plans: SubscriptionPlan[] = [];
  wallet?: WalletBalance;
  loadingPlans = false;
  loadingWallet = false;
  submitting = false;
  infoMessage = '';
  readonly heroHighlights = [
    'Пополнение кошелька без комиссий и с мгновенным зачислением',
    'Гибкие подписки под регулярные задачи команды',
    'Прозрачная аналитика по каждому списанию'
  ];

  readonly advantages = [
    {
      icon: 'payments',
      title: 'Единый кошелёк',
      description: 'Оплачивайте распознавание из общего баланса и не беспокойтесь о множестве счетов и договоров.'
    },
    {
      icon: 'schedule',
      title: 'Планирование бюджетов',
      description: 'Проверяйте остаток перед крупными проектами и пополняйте кошелёк в пару кликов.'
    },
    {
      icon: 'support_agent',
      title: 'Поддержка 24/7',
      description: 'Команда YouScriptor поможет с выставлением счетов, актами и подбором тарифа.'
    }
  ];

  constructor(
    private readonly paymentsService: PaymentsService,
    private readonly snackBar: MatSnackBar,
    private readonly route: ActivatedRoute,
    private readonly titleService: Title
  ) {
    this.titleService.setTitle('Подписки и баланс — YouScriptor');
  }

  ngOnInit(): void {
    this.loadPlans();
    this.loadWallet();

    this.route.queryParamMap
      .pipe(takeUntil(this.destroy$))
      .subscribe(params => {
        const status = params.get('status');
        const operation = params.get('operation');
        if (status === 'success' || status === null && operation) {
          this.infoMessage = 'Платёж обработан. Обновите страницу через несколько секунд, если изменения ещё не применились.';
        } else if (status === 'failed') {
          this.infoMessage = 'Платёж не был завершён. Попробуйте снова.';
        } else {
          this.infoMessage = '';
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadPlans(): void {
    this.loadingPlans = true;
    this.paymentsService.getPlans().subscribe({
      next: plans => {
        this.plans = plans;
        this.loadingPlans = false;
      },
      error: () => {
        this.loadingPlans = false;
        this.snackBar.open('Не удалось загрузить тарифы. Попробуйте позже.', 'Закрыть', { duration: 4000 });
      }
    });
  }

  loadWallet(): void {
    this.loadingWallet = true;
    this.paymentsService.getWallet().subscribe({
      next: wallet => {
        this.wallet = wallet;
        this.loadingWallet = false;
      },
      error: () => {
        this.loadingWallet = false;
        this.snackBar.open('Не удалось получить баланс кошелька.', 'Закрыть', { duration: 4000 });
      }
    });
  }

  openPayment(url: string): void {
    window.open(url, '_blank');
    this.snackBar.open('Окно оплаты открыто. После успешного платежа вернитесь на эту страницу.', 'Понятно', { duration: 5000 });
  }

  buyPlan(plan: SubscriptionPlan): void {
    if (this.submitting) {
      return;
    }

    this.submitting = true;
    this.paymentsService.createSubscription(plan.code).subscribe({
      next: response => {
        this.submitting = false;
        this.openPayment(response.paymentUrl);
      },
      error: () => {
        this.submitting = false;
        this.snackBar.open('Не удалось создать платёж. Попробуйте позже.', 'Закрыть', { duration: 4000 });
      }
    });
  }

  deposit(): void {
    if (this.submitting) {
      return;
    }

    if (this.depositControl.invalid) {
      this.depositControl.markAsTouched();
      return;
    }

    const amount = this.depositControl.value;
    if (amount == null) {
      return;
    }

    this.submitting = true;
    this.paymentsService.createWalletDeposit(amount).subscribe({
      next: response => {
        this.submitting = false;
        this.openPayment(response.paymentUrl);
      },
      error: () => {
        this.submitting = false;
        this.snackBar.open('Не удалось создать пополнение. Попробуйте позже.', 'Закрыть', { duration: 4000 });
      }
    });
  }

  formatPrice(plan: SubscriptionPlan): string {
    return new Intl.NumberFormat('ru-RU', { style: 'currency', currency: plan.currency }).format(plan.price);
  }

  getPlanDuration(plan: SubscriptionPlan): string {
    if (plan.isLifetime) {
      return 'Бессрочно';
    }

    switch (this.normalizeBillingPeriod(plan.billingPeriod)) {
      case 'ThreeDays':
        return '3 дня';
      case 'Monthly':
        return '1 месяц';
      case 'Yearly':
        return '1 год';
      default:
        return 'Разово';
    }
  }

  private normalizeBillingPeriod(period: SubscriptionBillingPeriod): string {
    if (typeof period === 'string') {
      return period;
    }

    switch (period) {
      case 0:
        return 'OneTime';
      case 1:
        return 'Monthly';
      case 2:
        return 'Yearly';
      case 3:
        return 'Lifetime';
      case 4:
        return 'ThreeDays';
      default:
        return `${period}`;
    }
  }
}
