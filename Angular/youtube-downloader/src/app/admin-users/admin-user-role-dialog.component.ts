import { CommonModule } from '@angular/common';
import { Component, Inject, OnInit } from '@angular/core';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTabsModule } from '@angular/material/tabs';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { PaymentsService, SubscriptionPlan } from '../services/payments.service';
import { AdminUsersService } from '../services/admin-users.service';
import {
  AdminManualSubscriptionPaymentRequest,
  AdminUserListItem,
  AdminUserSubscriptionSummary
} from '../models/admin-user.model';

export interface AdminUserRoleDialogData {
  user: AdminUserListItem;
  availableRoles: string[];
}

@Component({
  selector: 'app-admin-user-role-dialog',
  standalone: true,
  templateUrl: './admin-user-role-dialog.component.html',
  styleUrls: ['./admin-user-role-dialog.component.css'],
  imports: [
    CommonModule,
    MatDialogModule,
    MatButtonModule,
    MatCheckboxModule,
    MatTabsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatProgressSpinnerModule,
    ReactiveFormsModule
  ]
})
export class AdminUserRoleDialogComponent implements OnInit {
  selected = new Set<string>();
  readonly ipAddresses: string[];
  subscriptionSummary: AdminUserSubscriptionSummary | null = null;
  subscriptionError: string | null = null;
  loadingSubscription = false;
  manualPaymentForm: FormGroup;
  plans: SubscriptionPlan[] = [];
  plansError: string | null = null;
  loadingManualPayment = false;
  paymentSuccess: string | null = null;
  paymentError: string | null = null;

  constructor(
    @Inject(MAT_DIALOG_DATA) public readonly data: AdminUserRoleDialogData,
    private readonly dialogRef: MatDialogRef<AdminUserRoleDialogComponent>,
    private readonly adminUsersService: AdminUsersService,
    private readonly paymentsService: PaymentsService,
    private readonly formBuilder: FormBuilder
  ) {
    data.user.roles?.forEach(role => this.selected.add(role));
    const dedupedIps = Array.from(
      new Set((data.user.youtubeCaptionIps ?? []).map(ip => ip.trim()).filter(ip => !!ip))
    );
    this.ipAddresses = dedupedIps.sort((a, b) => a.localeCompare(b));
    this.manualPaymentForm = this.formBuilder.group({
      planCode: ['', Validators.required],
      amount: [null],
      currency: [''],
      endDate: [null],
      paidAt: [null],
      reference: [''],
      comment: ['']
    });
  }

  ngOnInit(): void {
    this.loadSubscriptionSummary();
    this.loadPlans();
  }

  toggleRole(role: string, checked: boolean): void {
    if (checked) {
      this.selected.add(role);
    } else {
      this.selected.delete(role);
    }
  }

  isChecked(role: string): boolean {
    return this.selected.has(role);
  }

  close(): void {
    this.dialogRef.close();
  }

  save(): void {
    this.dialogRef.close({ roles: Array.from(this.selected) });
  }

  private loadSubscriptionSummary(): void {
    this.loadingSubscription = true;
    this.subscriptionError = null;

    this.adminUsersService.getUserSubscription(this.data.user.id).subscribe({
      next: summary => {
        this.subscriptionSummary = summary;
        this.loadingSubscription = false;
      },
      error: err => {
        console.error('Failed to load user subscription', err);
        this.subscriptionSummary = null;
        this.subscriptionError = this.resolveErrorMessage(err, 'Не удалось загрузить данные о подписке.');
        this.loadingSubscription = false;
      }
    });
  }

  private loadPlans(): void {
    this.plansError = null;

    this.paymentsService.getPlans().subscribe({
      next: plans => {
        this.plans = plans;
        const currentPlan = this.manualPaymentForm.get('planCode');
        if (currentPlan && !currentPlan.value && this.plans.length > 0) {
          currentPlan.patchValue(this.plans[0].code);
        }
      },
      error: err => {
        console.error('Failed to load subscription plans', err);
        this.plans = [];
        this.plansError = this.resolveErrorMessage(err, 'Не удалось загрузить список тарифов.');
      }
    });
  }

  submitManualPayment(): void {
    if (this.manualPaymentForm.invalid) {
      this.manualPaymentForm.markAllAsTouched();
      return;
    }

    const formValue = this.manualPaymentForm.value;
    const payload: AdminManualSubscriptionPaymentRequest = {
      userId: this.data.user.id,
      planCode: formValue.planCode,
      amount: this.normalizeNumber(formValue.amount),
      currency: this.normalizeString(formValue.currency),
      endDate: this.normalizeDate(formValue.endDate),
      paidAt: this.normalizeDate(formValue.paidAt),
      reference: this.normalizeString(formValue.reference),
      comment: this.normalizeString(formValue.comment)
    };

    this.loadingManualPayment = true;
    this.paymentSuccess = null;
    this.paymentError = null;

    this.adminUsersService.createManualSubscriptionPayment(payload).subscribe({
      next: () => {
        this.paymentSuccess = 'Ручной платеж успешно сохранён.';
        this.loadingManualPayment = false;
        this.resetManualPaymentForm();
        this.loadSubscriptionSummary();
      },
      error: err => {
        console.error('Failed to create manual payment', err);
        this.paymentError = this.resolveErrorMessage(err, 'Не удалось сохранить платёж.');
        this.loadingManualPayment = false;
      }
    });
  }

  getPlanLabel(plan: SubscriptionPlan): string {
    const price = plan.price != null ? `${plan.price} ${plan.currency}` : '';
    return price ? `${plan.name} — ${price}` : plan.name;
  }

  trackPayment(_: number, payment: { invoiceId: string }): string {
    return payment.invoiceId;
  }

  private normalizeNumber(value: unknown): number | null | undefined {
    if (value === null || value === undefined || value === '') {
      return undefined;
    }

    const parsed = typeof value === 'number' ? value : Number(value);
    return Number.isFinite(parsed) ? parsed : undefined;
  }

  private normalizeString(value: unknown): string | null | undefined {
    if (typeof value !== 'string') {
      return undefined;
    }

    const trimmed = value.trim();
    return trimmed ? trimmed : undefined;
  }

  private normalizeDate(value: unknown): string | null | undefined {
    if (!value) {
      return undefined;
    }

    if (value instanceof Date) {
      return Number.isNaN(value.getTime()) ? undefined : value.toISOString();
    }

    if (typeof value === 'string' || typeof value === 'number') {
      const date = new Date(value);
      if (!Number.isNaN(date.getTime())) {
        return date.toISOString();
      }
    }

    return undefined;
  }

  private resetManualPaymentForm(): void {
    const planCode = this.manualPaymentForm.get('planCode')?.value ?? '';
    this.manualPaymentForm.reset({
      planCode,
      amount: null,
      currency: '',
      endDate: null,
      paidAt: null,
      reference: '',
      comment: ''
    });
    this.manualPaymentForm.markAsPristine();
    this.manualPaymentForm.markAsUntouched();
  }

  private resolveErrorMessage(error: unknown, fallback: string): string {
    if (!error) {
      return fallback;
    }

    if (typeof error === 'string') {
      return error;
    }

    const httpError = error as { error?: unknown; message?: string };
    const response = httpError?.error;

    if (typeof response === 'string') {
      return response;
    }

    if (response && typeof response === 'object') {
      const message = (response as { message?: string }).message;
      if (typeof message === 'string' && message.trim()) {
        return message.trim();
      }

      const errors = (response as { errors?: Record<string, unknown> }).errors;
      if (errors && typeof errors === 'object') {
        const messages: string[] = [];
        Object.values(errors).forEach(value => {
          if (Array.isArray(value)) {
            value.forEach(item => {
              if (typeof item === 'string' && item.trim()) {
                messages.push(item.trim());
              }
            });
          } else if (typeof value === 'string' && value.trim()) {
            messages.push(value.trim());
          }
        });

        if (messages.length > 0) {
          return messages.join(' ');
        }
      }
    }

    if (httpError?.message && typeof httpError.message === 'string') {
      return httpError.message;
    }

    return fallback;
  }
}
