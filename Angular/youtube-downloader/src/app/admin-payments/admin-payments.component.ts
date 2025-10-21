import { CommonModule, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableDataSource, MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AdminYooMoneyOperation } from '../models/admin-payments.model';
import { AdminPaymentsService } from '../services/admin-payments.service';
import {
  AdminPaymentDetailsDialogComponent,
  AdminPaymentDetailsDialogData
} from './admin-payment-details-dialog.component';
import {
  AdminPaymentOperationDialogComponent,
  AdminPaymentOperationDialogData
} from './admin-payment-operation-dialog.component';

interface SpendingCategory {
  name?: string | null;
  code?: string | null;
}

interface AdminYooMoneyOperationViewModel extends AdminYooMoneyOperation {
  currency: string | null;
  type: string | null;
  direction: string | null;
  label: string | null;
  groupId: string | null;
  isSbpOperation: boolean | null;
  spendingCategories: ReadonlyArray<SpendingCategory>;
}

interface OperationMetadata {
  currency: string | null;
  type: string | null;
  direction: string | null;
  label: string | null;
  groupId: string | null;
  isSbpOperation: boolean | null;
  spendingCategories: ReadonlyArray<SpendingCategory>;
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
    MatIconModule,
    MatTooltipModule,
    MatProgressSpinnerModule,
    MatDialogModule
  ]
})
export class AdminPaymentsComponent implements OnInit {
  readonly displayedColumns: string[] = [
    'operation_id',
    'datetime',
    'title',
    'amount',
    'type',
    'direction',
    'label',
    'group_id',
    'is_sbp_operation',
    'spendingCategories'
  ];
  readonly dataSource = new MatTableDataSource<AdminYooMoneyOperationViewModel>();
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

        const viewModels = this.toViewModels(list);
        this.operations = viewModels;
        this.dataSource.data = viewModels;
        this.pageSize = pageSize;
        this.pageIndex = pageIndex;
        const hasMore = list.length === pageSize;
        this.total = hasMore ? (pageIndex + 2) * pageSize : startRecord + list.length;
        this.loading = false;
      },
      error: err => {
        this.loading = false;
        this.dataSource.data = [];
        this.operations = [];
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

  openOperationDetails(operation: AdminYooMoneyOperationViewModel, event?: MouseEvent): void {
    if (event) {
      event.preventDefault();
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

  openLabelDetails(label: string | null | undefined, event?: MouseEvent): void {
    if (event) {
      event.preventDefault();
      event.stopPropagation();
    }

    const identifier = label?.trim();
    if (!identifier) {
      return;
    }

    const data: AdminPaymentOperationDialogData = {
      paymentOperationId: identifier
    };

    this.dialog.open(AdminPaymentOperationDialogComponent, {
      width: '640px',
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

  private toViewModels(operations: AdminYooMoneyOperation[]): AdminYooMoneyOperationViewModel[] {
    return operations.map(operation => {
      const metadata = this.extractOperationMetadata(operation.additionalData);
      return {
        ...operation,
        currency: metadata.currency,
        type: metadata.type,
        direction: metadata.direction,
        label: metadata.label,
        groupId: metadata.groupId,
        isSbpOperation: metadata.isSbpOperation,
        spendingCategories: metadata.spendingCategories
      };
    });
  }

  private extractOperationMetadata(
    additionalData: Record<string, unknown> | null | undefined
  ): OperationMetadata {
    const defaults: OperationMetadata = {
      currency: null,
      type: null,
      direction: null,
      label: null,
      groupId: null,
      isSbpOperation: null,
      spendingCategories: []
    };

    if (!additionalData || typeof additionalData !== 'object' || Array.isArray(additionalData)) {
      return defaults;
    }

    const normalized = new Map<string, unknown>();
    for (const [key, value] of Object.entries(additionalData)) {
      normalized.set(key.toLowerCase(), value);
    }

    const pickValue = (...keys: string[]): unknown => {
      for (const key of keys) {
        if (Object.prototype.hasOwnProperty.call(additionalData, key)) {
          return (additionalData as Record<string, unknown>)[key];
        }

        const normalizedKey = key.toLowerCase();
        if (normalized.has(normalizedKey)) {
          return normalized.get(normalizedKey);
        }
      }

      return undefined;
    };

    const metadata: OperationMetadata = { ...defaults };
    const currency = this.extractString(
      pickValue('amount_currency', 'currency', 'currencyCode', 'currency_code')
    );
    metadata.currency = currency ? currency.toUpperCase() : null;
    metadata.type = this.extractString(pickValue('type'));
    const direction = this.extractString(pickValue('direction'));
    metadata.direction = direction ? direction.toLowerCase() : null;
    metadata.label = this.extractString(pickValue('label', 'billId', 'bill_id', 'invoiceId', 'invoice_id'));
    metadata.groupId = this.extractString(pickValue('group_id', 'groupId'));
    metadata.isSbpOperation = this.extractBoolean(
      pickValue('is_sbp_operation', 'sbp', 'isSbpOperation')
    );
    metadata.spendingCategories = this.extractSpendingCategories(
      pickValue('spendingCategories', 'spending_categories')
    );

    return metadata;
  }

  private extractString(value: unknown): string | null {
    if (typeof value === 'string') {
      const trimmed = value.trim();
      return trimmed.length > 0 ? trimmed : null;
    }

    if (typeof value === 'number' || typeof value === 'bigint') {
      return value.toString();
    }

    if (value instanceof Date) {
      return value.toISOString();
    }

    return null;
  }

  private extractBoolean(value: unknown): boolean | null {
    if (typeof value === 'boolean') {
      return value;
    }

    if (typeof value === 'number') {
      if (value === 1) {
        return true;
      }

      if (value === 0) {
        return false;
      }
    }

    if (typeof value === 'string') {
      const normalized = value.trim().toLowerCase();
      if (!normalized) {
        return null;
      }

      if (['true', 'yes', 'y', '1'].includes(normalized)) {
        return true;
      }

      if (['false', 'no', 'n', '0'].includes(normalized)) {
        return false;
      }
    }

    return null;
  }

  private extractSpendingCategories(value: unknown): ReadonlyArray<SpendingCategory> {
    if (value == null) {
      return [];
    }

    const source = Array.isArray(value) ? value : [value];
    const categories: SpendingCategory[] = [];

    for (const item of source) {
      const normalized = this.normalizeSpendingCategory(item);
      if (normalized) {
        categories.push(normalized);
      }
    }

    return categories;
  }

  private normalizeSpendingCategory(value: unknown): SpendingCategory | null {
    if (typeof value === 'string') {
      const name = this.extractString(value);
      return name ? { name } : null;
    }

    if (value && typeof value === 'object') {
      const record = value as Record<string, unknown>;
      const name = this.extractString(record['name'] ?? record['title']);
      const code = this.extractString(record['code'] ?? record['id']);
      if (name || code) {
        return { name, code };
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
