import { CommonModule, DatePipe, JsonPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, Inject, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AdminPaymentsService } from '../services/admin-payments.service';
import {
  AdminYooMoneyBillDetails,
  AdminYooMoneyOperation,
  AdminYooMoneyOperationDetails
} from '../models/admin-payments.model';

export interface AdminPaymentDetailsDialogData {
  operationId: string;
  operationSummary?: AdminYooMoneyOperation | null;
}

@Component({
  selector: 'app-admin-payment-details-dialog',
  standalone: true,
  templateUrl: './admin-payment-details-dialog.component.html',
  styleUrls: ['./admin-payment-details-dialog.component.css'],
  imports: [
    CommonModule,
    DatePipe,
    JsonPipe,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatDividerModule,
    MatTooltipModule,
    MatProgressSpinnerModule
  ]
})
export class AdminPaymentDetailsDialogComponent implements OnInit {
  readonly operationId: string;
  readonly summary?: AdminYooMoneyOperation | null;

  operationDetails: AdminYooMoneyOperationDetails | null = null;
  billDetails: AdminYooMoneyBillDetails | null = null;
  billIdentifier: string | null = null;
  loading = false;
  error: string | null = null;
  loadingBill = false;
  billError: string | null = null;

  constructor(
    @Inject(MAT_DIALOG_DATA) data: AdminPaymentDetailsDialogData,
    private readonly dialogRef: MatDialogRef<AdminPaymentDetailsDialogComponent>,
    private readonly adminPaymentsService: AdminPaymentsService
  ) {
    this.operationId = data.operationId;
    this.summary = data.operationSummary;
  }

  ngOnInit(): void {
    this.loadOperationDetails();
  }

  close(): void {
    this.dialogRef.close();
  }

  reloadDetails(): void {
    this.loadOperationDetails();
  }

  loadBillDetails(): void {
    if (!this.billIdentifier) {
      return;
    }

    this.fetchBillDetails(this.billIdentifier, false);
  }

  async copyJson(value: unknown): Promise<void> {
    if (typeof navigator === 'undefined' || !navigator.clipboard) {
      return;
    }

    try {
      const text = typeof value === 'string' ? value : JSON.stringify(value, null, 2);
      if (!text) {
        return;
      }

      await navigator.clipboard.writeText(text);
    } catch (err) {
      console.warn('Failed to copy JSON to clipboard', err);
    }
  }

  private loadOperationDetails(): void {
    if (!this.operationId) {
      this.error = 'Не указан идентификатор операции.';
      return;
    }

    this.loading = true;
    this.error = null;
    this.adminPaymentsService.getOperationDetails(this.operationId).subscribe({
      next: details => {
        this.operationDetails = details;
        this.loading = false;
        this.billIdentifier = this.extractBillIdentifier(details);
        if (this.billIdentifier) {
          this.fetchBillDetails(this.billIdentifier, true);
        } else {
          this.billDetails = null;
          this.billError = null;
        }
      },
      error: err => {
        this.loading = false;
        this.error = this.resolveErrorMessage(err, 'Не удалось загрузить детали операции YooMoney.');
      }
    });
  }

  private fetchBillDetails(billId: string, silent: boolean): void {
    if (!billId) {
      return;
    }

    this.loadingBill = true;
    if (!silent) {
      this.billError = null;
    }

    this.adminPaymentsService.getBillDetails(billId).subscribe({
      next: details => {
        this.billDetails = details;
        this.loadingBill = false;
        if (silent) {
          this.billError = null;
        }
      },
      error: err => {
        this.loadingBill = false;
        this.billDetails = null;
        const message = this.resolveErrorMessage(err, 'Не удалось загрузить детали счёта YooMoney.');
        this.billError = message;
      }
    });
  }

  private extractBillIdentifier(operation: AdminYooMoneyOperationDetails | null): string | null {
    if (!operation) {
      return null;
    }

    return this.findBillIdentifier(operation.additionalData);
  }

  private findBillIdentifier(source: unknown): string | null {
    if (!source) {
      return null;
    }

    if (typeof source === 'string') {
      const trimmed = source.trim();
      return this.looksLikeBillId(trimmed) ? trimmed : null;
    }

    if (Array.isArray(source)) {
      for (const item of source) {
        const nested = this.findBillIdentifier(item);
        if (nested) {
          return nested;
        }
      }

      return null;
    }

    if (typeof source === 'object') {
      for (const [key, value] of Object.entries(source as Record<string, unknown>)) {
        if (this.isBillKey(key) && typeof value === 'string') {
          const trimmed = value.trim();
          if (trimmed) {
            return trimmed;
          }
        }

        const nested = this.findBillIdentifier(value);
        if (nested) {
          return nested;
        }
      }
    }

    return null;
  }

  private isBillKey(key: string): boolean {
    const normalized = key.toLowerCase();
    return [
      'invoiceid',
      'invoice_id',
      'invoice',
      'billid',
      'bill_id',
      'bill',
      'orderid',
      'order_id',
      'order',
      'paymentinvoiceid'
    ].includes(normalized);
  }

  private looksLikeBillId(value: string): boolean {
    return value.length >= 6 && /^[a-z0-9\-_/]+$/i.test(value);
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
