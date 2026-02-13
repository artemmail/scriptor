import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Title } from '@angular/platform-browser';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import {
  AdminSubscriptionPlan,
  AdminSubscriptionPlansService
} from '../services/admin-subscription-plans.service';

interface EditableAdminSubscriptionPlan extends AdminSubscriptionPlan {
  dirty?: boolean;
}

@Component({
  selector: 'app-admin-billing-plans',
  standalone: true,
  templateUrl: './admin-billing-plans.component.html',
  styleUrls: ['./admin-billing-plans.component.css'],
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatButtonModule,
    MatProgressSpinnerModule
  ]
})
export class AdminBillingPlansComponent implements OnInit {
  plans: EditableAdminSubscriptionPlan[] = [];
  loadingPlans = false;
  plansError: string | null = null;
  plansSuccess: string | null = null;
  private readonly savingPlanIds = new Set<string>();

  constructor(
    private readonly adminSubscriptionPlansService: AdminSubscriptionPlansService,
    private readonly titleService: Title
  ) {}

  ngOnInit(): void {
    this.titleService.setTitle('Админка — тарифы billing');
    this.loadPlans();
  }

  loadPlans(): void {
    this.loadingPlans = true;
    this.plansError = null;
    this.plansSuccess = null;

    this.adminSubscriptionPlansService.getPlans().subscribe({
      next: plans => {
        this.loadingPlans = false;
        this.plans = (plans ?? []).map(plan => ({ ...plan, dirty: false }));
      },
      error: err => {
        this.loadingPlans = false;
        this.plans = [];
        this.plansError = this.resolveErrorMessage(err, 'Не удалось загрузить тарифы.');
      }
    });
  }

  markPlanDirty(plan: EditableAdminSubscriptionPlan): void {
    plan.dirty = true;
    this.plansSuccess = null;
    this.plansError = null;
  }

  isSavingPlan(planId: string): boolean {
    return this.savingPlanIds.has(planId);
  }

  savePlan(plan: EditableAdminSubscriptionPlan): void {
    if (!plan || !plan.id || this.isSavingPlan(plan.id)) {
      return;
    }

    this.savingPlanIds.add(plan.id);
    this.plansError = null;
    this.plansSuccess = null;

    this.adminSubscriptionPlansService
      .savePlan(plan.id, {
        code: (plan.code || '').trim(),
        name: (plan.name || '').trim(),
        description: plan.description?.trim() || null,
        price: Number.isFinite(plan.price) ? plan.price : 0,
        currency: (plan.currency || 'RUB').trim().toUpperCase(),
        includedTranscriptionMinutes: Math.max(0, Math.round(plan.includedTranscriptionMinutes ?? 0)),
        includedVideos: Math.max(0, Math.round(plan.includedVideos ?? 0)),
        isActive: !!plan.isActive,
        priority: Math.round(plan.priority ?? 0)
      })
      .subscribe({
        next: updated => {
          this.savingPlanIds.delete(plan.id);
          const index = this.plans.findIndex(item => item.id === updated.id);
          if (index >= 0) {
            this.plans[index] = { ...updated, dirty: false };
          }
          this.plansSuccess = `Тариф "${updated.name}" сохранён.`;
        },
        error: err => {
          this.savingPlanIds.delete(plan.id);
          this.plansError = this.resolveErrorMessage(err, 'Не удалось сохранить тариф.');
        }
      });
  }

  trackByPlanId(_: number, plan: EditableAdminSubscriptionPlan): string {
    return plan.id;
  }

  formatHourlyRate(plan: EditableAdminSubscriptionPlan): string {
    const minutes = Math.max(0, Math.round(plan.includedTranscriptionMinutes ?? 0));
    if (minutes <= 0) {
      return '—';
    }

    const rate = (Number(plan.price) * 60) / minutes;
    return `${this.formatCurrency(rate, plan.currency)} / час`;
  }

  private formatCurrency(amount: number, currency: string | null | undefined): string {
    const normalizedCurrency = (currency || 'RUB').trim().toUpperCase() || 'RUB';
    try {
      return new Intl.NumberFormat('ru-RU', {
        style: 'currency',
        currency: normalizedCurrency,
        maximumFractionDigits: 2
      }).format(amount);
    } catch {
      return `${amount.toFixed(2)} ${normalizedCurrency}`;
    }
  }

  private resolveErrorMessage(error: unknown, fallback: string): string {
    if (!error) {
      return fallback;
    }

    if (typeof error === 'string') {
      return error;
    }

    if (error instanceof HttpErrorResponse) {
      const message =
        (typeof error.error === 'string' && error.error) ||
        (error.error && typeof error.error.message === 'string' && error.error.message) ||
        error.statusText;
      return message || fallback;
    }

    if (typeof (error as { message?: string }).message === 'string') {
      return (error as { message?: string }).message as string;
    }

    return fallback;
  }
}

