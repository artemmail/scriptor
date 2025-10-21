import { CommonModule, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AdminPaymentsService } from '../services/admin-payments.service';
import { AdminYooMoneyOperation } from '../models/admin-payments.model';
import {
  AdminPaymentDetailsDialogComponent,
  AdminPaymentDetailsDialogData
} from './admin-payment-details-dialog.component';

interface AdminYooMoneyOperationViewModel extends AdminYooMoneyOperation {
  additionalKeys: string[];
  currency: string | null;
}

@Component({
  selector: 'app-admin-payments',
  standalone: true,
  templateUrl: './admin-payments.component.html',
  styleUrls: ['./admin-payments.component.css'],
  imports: [
    CommonModule,
    MatCardModule,
    DatePipe,
    MatTableModule,
    MatPaginatorModule,
    MatButtonModule,
    MatIconModule,
    MatTooltipModule,
    MatProgressSpinnerModule,
    MatDialogModule
  ]
})
export class AdminPaymentsComponent implements OnInit {
  displayedColumns: string[] = [
    'dateTime',
    'operationId',
    'title',
    'amount',
    'status',
    'additionalData',
    'actions'
  ];
  operations: AdminYooMoneyOperationViewModel[] = [];
  loading = false;
  error: string | null = null;
  pageSize = 30;
  pageIndex = 0;
  total = 0;
  readonly pageSizeOptions = [10, 20, 30, 50];

  constructor(
    private readonly adminPaymentsService: AdminPaymentsService,
    private readonly dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.loadOperations();
  }

  loadOperations(pageIndex = this.pageIndex, pageSize = this.pageSize): void {
    const startRecord = pageIndex * pageSize;
    this.loading = true;
    this.error = null;
    this.adminPaymentsService.getOperationHistory(startRecord, pageSize).subscribe({
      next: operations => {
        const list = operations ?? [];
        if (list.length === 0 && startRecord > 0) {
          this.loading = false;
          const previousPage = Math.max(pageIndex - 1, 0);
          this.total = startRecord;
          this.loadOperations(previousPage, pageSize);
          return;
        }

        this.operations = this.toViewModels(list);
        this.pageSize = pageSize;
        this.pageIndex = pageIndex;
        const hasMore = list.length === pageSize;
        this.total = hasMore ? (pageIndex + 2) * pageSize : startRecord + list.length;
        this.loading = false;
      },
      error: err => {
        this.loading = false;
        this.error = this.resolveErrorMessage(err, 'Не удалось загрузить операции YooMoney.');
      }
    });
  }

  onPageChange(event: PageEvent): void {
    const sizeChanged = event.pageSize !== this.pageSize;
    const targetPage = sizeChanged ? 0 : event.pageIndex;

    if (!sizeChanged && targetPage === this.pageIndex) {
      return;
    }

    this.loadOperations(targetPage, event.pageSize);
  }

  openDetails(operation: AdminYooMoneyOperationViewModel, event?: MouseEvent): void {
    if (event) {
      event.stopPropagation();
    }

    const operationId = operation.operationId?.trim();
    if (!operationId) {
      return;
    }

    const data: AdminPaymentDetailsDialogData = {
      operationId,
      operationSummary: operation
    };

    this.dialog.open(AdminPaymentDetailsDialogComponent, {
      width: '720px',
      maxWidth: '95vw',
      data
    });
  }

  trackByOperationId(index: number, operation: AdminYooMoneyOperationViewModel): string {
    return operation.operationId ?? `${operation.dateTime ?? ''}-${operation.title ?? ''}-${index}`;
  }

  formatAmount(operation: AdminYooMoneyOperationViewModel): string {
    if (operation.amount == null) {
      return '—';
    }

    const currency = operation.currency;
    const amount = this.formatNumber(operation.amount);
    return currency ? `${amount} ${currency}` : amount;
  }

  hasAdditionalData(operation: AdminYooMoneyOperationViewModel): boolean {
    return operation.additionalKeys.length > 0;
  }

  private toViewModels(operations: AdminYooMoneyOperation[]): AdminYooMoneyOperationViewModel[] {
    return operations.map(operation => ({
      ...operation,
      additionalKeys: this.extractAdditionalKeys(operation.additionalData),
      currency: this.extractCurrency(operation.additionalData)
    }));
  }

  private extractAdditionalKeys(additionalData: Record<string, unknown> | null | undefined): string[] {
    if (!additionalData || typeof additionalData !== 'object' || Array.isArray(additionalData)) {
      return [];
    }

    return Object.keys(additionalData).sort((a, b) => a.localeCompare(b));
  }

  private extractCurrency(additionalData: Record<string, unknown> | null | undefined): string | null {
    if (!additionalData || typeof additionalData !== 'object') {
      return null;
    }

    const candidates = ['currency', 'currencyCode', 'currency_code', 'amount_currency'];
    for (const key of candidates) {
      const value = (additionalData as Record<string, unknown>)[key];
      if (typeof value === 'string') {
        const trimmed = value.trim();
        if (trimmed) {
          return trimmed;
        }
      }
    }

    return null;
  }

  private formatNumber(value: number): string {
    return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
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
