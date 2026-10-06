import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Title } from '@angular/platform-browser';
import { RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AdminMenuComponent } from '../shared/admin-menu.component';
import { AdminSubscriptionPlan, AdminSubscriptionPlansService } from '../services/admin-subscription-plans.service';

interface EditableAdminSubscriptionPlan extends AdminSubscriptionPlan {
  dirty: boolean;
  error?: string | null;
  saved?: boolean;
}

@Component({
  selector: 'app-admin-billing-plans',
  standalone: true,
  templateUrl: './admin-billing-plans.component.html',
  styleUrls: ['../shared/account-page.css', './admin-billing-plans.component.css'],
  imports: [CommonModule, FormsModule, RouterModule, MatIconModule, MatProgressSpinnerModule, AdminMenuComponent]
})
export class AdminBillingPlansComponent implements OnInit {
  plans: EditableAdminSubscriptionPlan[] = [];
  selectedPlanId: string | null = null;
  search = '';
  loadingPlans = false;
  plansError: string | null = null;
  private readonly originals = new Map<string, AdminSubscriptionPlan>();
  private readonly savingPlanIds = new Set<string>();

  constructor(
    private readonly adminSubscriptionPlansService: AdminSubscriptionPlansService,
    private readonly titleService: Title
  ) {}

  ngOnInit(): void {
    this.titleService.setTitle('Тарифы — управление YouScriptor');
    this.loadPlans();
  }

  get selectedPlan(): EditableAdminSubscriptionPlan | undefined {
    return this.plans.find(plan => plan.id === this.selectedPlanId);
  }

  get filteredPlans(): EditableAdminSubscriptionPlan[] {
    const query = this.search.trim().toLocaleLowerCase('ru');
    return this.plans.filter(plan => !query || [plan.name, plan.code].some(value => value.toLocaleLowerCase('ru').includes(query)));
  }

  get visibleSelectedPlanId(): string | null {
    return this.filteredPlans.some(plan => plan.id === this.selectedPlanId) ? this.selectedPlanId : null;
  }

  get activeCount(): number { return [...this.originals.values()].filter(plan => plan.isActive).length; }
  get dirtyCount(): number { return this.plans.filter(plan => plan.dirty).length; }
  get canReload(): boolean { return !this.loadingPlans && !this.dirtyCount && !this.savingPlanIds.size; }

  loadPlans(): void {
    if (!this.canReload) return;
    this.loadingPlans = true;
    this.plansError = null;
    this.adminSubscriptionPlansService.getPlans().subscribe({
      next: plans => {
        this.plans = (plans ?? []).map(plan => ({ ...plan, dirty: false }));
        this.originals.clear();
        this.plans.forEach(plan => this.originals.set(plan.id, { ...plan }));
        if (!this.selectedPlan) this.selectedPlanId = this.plans[0]?.id ?? null;
        this.loadingPlans = false;
      },
      error: err => {
        this.loadingPlans = false;
        this.plansError = this.resolveErrorMessage(err, 'Не удалось загрузить тарифы.');
      }
    });
  }

  selectPlan(planId: string): void { this.selectedPlanId = planId; }

  markPlanDirty(plan: EditableAdminSubscriptionPlan): void {
    const original = this.originals.get(plan.id);
    const fields: (keyof AdminSubscriptionPlan)[] = [
      'code', 'name', 'description', 'price', 'currency',
      'includedTranscriptionMinutes', 'includedVideos', 'isActive', 'priority'
    ];
    plan.dirty = !original || fields.some(field => plan[field] !== original[field]);
    plan.error = null;
    plan.saved = false;
  }

  isSavingPlan(planId: string): boolean { return this.savingPlanIds.has(planId); }

  revertPlan(plan: EditableAdminSubscriptionPlan): void {
    const original = this.originals.get(plan.id);
    if (!original || this.isSavingPlan(plan.id)) return;
    const index = this.plans.findIndex(item => item.id === plan.id);
    this.plans[index] = { ...original, dirty: false, error: null, saved: false };
  }

  isValidQuota(value: number): boolean {
    return Number.isInteger(value) && value >= 0 && value <= 2147483647;
  }

  isValidPrice(price: number): boolean { return Number.isFinite(price) && price >= 0; }

  isValidPriority(priority: number): boolean {
    return Number.isInteger(priority) && priority >= -2147483648 && priority <= 2147483647;
  }

  isPlanValid(plan: EditableAdminSubscriptionPlan): boolean {
    return !!plan.name?.trim() && plan.name.length <= 128
      && !!plan.code?.trim() && plan.code.length <= 64
      && !!plan.currency?.trim() && plan.currency.length <= 8
      && (plan.description?.length ?? 0) <= 1024
      && this.isValidPrice(plan.price)
      && this.isValidQuota(plan.includedTranscriptionMinutes)
      && this.isValidQuota(plan.includedVideos)
      && this.isValidPriority(plan.priority);
  }

  savePlan(plan: EditableAdminSubscriptionPlan): void {
    if (!plan.id || !plan.dirty || this.isSavingPlan(plan.id) || this.loadingPlans) return;
    if (!this.isPlanValid(plan)) {
      plan.error = 'Проверьте обязательные поля, стоимость и лимиты тарифа.';
      return;
    }
    this.savingPlanIds.add(plan.id);
    plan.error = null;
    plan.saved = false;
    this.adminSubscriptionPlansService.savePlan(plan.id, {
      code: plan.code.trim(),
      name: plan.name.trim(),
      description: plan.description?.trim() || null,
      price: plan.price,
      currency: plan.currency.trim().toUpperCase(),
      includedTranscriptionMinutes: plan.includedTranscriptionMinutes,
      includedVideos: plan.includedVideos,
      isActive: !!plan.isActive,
      priority: plan.priority
    }).subscribe({
      next: updated => {
        this.savingPlanIds.delete(plan.id);
        this.originals.set(updated.id, { ...updated });
        const index = this.plans.findIndex(item => item.id === updated.id);
        if (index >= 0) this.plans[index] = { ...updated, dirty: false, saved: true };
      },
      error: err => {
        this.savingPlanIds.delete(plan.id);
        plan.error = this.resolveErrorMessage(err, 'Не удалось сохранить тариф. Ваши изменения остаются в редакторе.');
      }
    });
  }

  trackByPlanId(_: number, plan: AdminSubscriptionPlan): string { return plan.id; }

  formatPrice(plan: AdminSubscriptionPlan): string {
    return this.isValidPrice(plan.price) ? this.formatCurrency(plan.price, plan.currency) : '—';
  }

  formatHourlyRate(plan: AdminSubscriptionPlan): string {
    if (!this.isValidPrice(plan.price) || !this.isValidQuota(plan.includedTranscriptionMinutes) || plan.includedTranscriptionMinutes === 0) return '—';
    return this.formatCurrency(plan.price * 60 / plan.includedTranscriptionMinutes, plan.currency) + ' / час';
  }

  private formatCurrency(amount: number, currency: string | null | undefined): string {
    const code = currency?.trim().toUpperCase();
    if (!code) return amount.toLocaleString('ru-RU', { maximumFractionDigits: 2 });
    try {
      return new Intl.NumberFormat('ru-RU', { style: 'currency', currency: code, maximumFractionDigits: 2 }).format(amount);
    } catch {
      return amount.toLocaleString('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' ' + code;
    }
  }

  private resolveErrorMessage(error: unknown, fallback: string): string {
    if (typeof error === 'string') return error;
    if (error instanceof HttpErrorResponse) {
      if (typeof error.error === 'string' && error.error) return error.error;
      if (typeof error.error?.message === 'string') return error.error.message;
      return fallback;
    }
    return fallback;
  }
}
