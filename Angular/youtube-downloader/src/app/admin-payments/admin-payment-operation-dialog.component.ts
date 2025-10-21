import { CommonModule, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, Inject, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AdminPaymentOperationDetails } from '../models/admin-payments.model';
import { AdminPaymentsService } from '../services/admin-payments.service';

export interface AdminPaymentOperationDialogData {
  paymentOperationId: string;
}

@Component({
  selector: 'app-admin-payment-operation-dialog',
  standalone: true,
  templateUrl: './admin-payment-operation-dialog.component.html',
  styleUrls: ['./admin-payment-operation-dialog.component.css'],
  imports: [
    CommonModule,
    DatePipe,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatDividerModule,
    MatProgressSpinnerModule
  ]
})
export class AdminPaymentOperationDialogComponent implements OnInit {
  readonly paymentOperationId: string;
  details: AdminPaymentOperationDetails | null = null;
  loading = false;
  error: string | null = null;
  applying = false;
  applyError: string | null = null;

  constructor(
    @Inject(MAT_DIALOG_DATA) data: AdminPaymentOperationDialogData,
    private readonly dialogRef: MatDialogRef<AdminPaymentOperationDialogComponent>,
    private readonly adminPaymentsService: AdminPaymentsService
  ) {
    this.paymentOperationId = data.paymentOperationId;
  }

  ngOnInit(): void {
    this.loadDetails();
  }

  close(): void {
    this.dialogRef.close();
  }

  reload(): void {
    this.applyError = null;
    this.loadDetails();
  }

  get isApplied(): boolean {
    return !!this.details?.applied;
  }

  applyOperation(): void {
    if (!this.paymentOperationId || this.loading || this.applying || this.isApplied) {
      return;
    }

    this.applying = true;
    this.applyError = null;
    this.adminPaymentsService.applyPaymentOperation(this.paymentOperationId).subscribe({
      next: details => {
        this.details = details;
        this.applying = false;
        this.applyError = null;
      },
      error: err => {
        this.applying = false;
        this.applyError = this.resolveErrorMessage(
          err,
          'Не удалось применить платежную операцию.'
        );
      }
    });
  }

  formatAmount(details: AdminPaymentOperationDetails | null): string {
    if (!details) {
      return '—';
    }

    const amount = details.amount;
    if (amount == null) {
      return '—';
    }

    const formatted = amount.toLocaleString(undefined, {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2
    });

    return details.currency ? `${formatted} ${details.currency}` : formatted;
  }

  get payloadText(): string | null {
    const payload = this.details?.payload;
    if (payload == null) {
      return null;
    }

    const text = typeof payload === 'string' ? payload.trim() : String(payload);
    return text.length > 0 ? text : null;
  }

  private loadDetails(): void {
    if (!this.paymentOperationId) {
      this.error = 'Не указан идентификатор операции оплаты.';
      this.details = null;
      return;
    }

    this.loading = true;
    this.error = null;
    this.adminPaymentsService.getPaymentOperationDetails(this.paymentOperationId).subscribe({
      next: details => {
        this.details = details;
        this.loading = false;
        this.applyError = null;
      },
      error: err => {
        this.loading = false;
        this.details = null;
        this.error = this.resolveErrorMessage(err, 'Не удалось загрузить детали платежной операции.');
        this.applyError = null;
      }
    });
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
