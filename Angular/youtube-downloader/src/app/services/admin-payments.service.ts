import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AdminPaymentOperationDetails,
  AdminYooMoneyBillDetails,
  AdminYooMoneyOperation,
  AdminYooMoneyOperationDetails
} from '../models/admin-payments.model';

@Injectable({ providedIn: 'root' })
export class AdminPaymentsService {
  private readonly baseUrl = '/api/admin/yoomoney';

  constructor(private readonly http: HttpClient) {}

  getOperationHistory(startRecord = 0, records = 30): Observable<AdminYooMoneyOperation[]> {
    let params = new HttpParams();

    if (startRecord > 0) {
      params = params.set('start_record', startRecord.toString());
    }

    if (records > 0) {
      params = params.set('records', records.toString());
    }

    return this.http.get<AdminYooMoneyOperation[]>(`${this.baseUrl}/operation-history`, { params });
  }

  getOperationDetails(operationId: string): Observable<AdminYooMoneyOperationDetails> {
    return this.http.get<AdminYooMoneyOperationDetails>(
      `${this.baseUrl}/operation-details/${encodeURIComponent(operationId)}`
    );
  }

  getBillDetails(billId: string): Observable<AdminYooMoneyBillDetails> {
    return this.http.get<AdminYooMoneyBillDetails>(
      `${this.baseUrl}/bill-details/${encodeURIComponent(billId)}`
    );
  }

  getPaymentOperationDetails(operationId: string): Observable<AdminPaymentOperationDetails> {
    return this.http.get<AdminPaymentOperationDetails>(
      `${this.baseUrl}/payment-operations/${encodeURIComponent(operationId)}`
    );
  }

  applyPaymentOperation(operationId: string): Observable<AdminPaymentOperationDetails> {
    return this.http.post<AdminPaymentOperationDetails>(
      `${this.baseUrl}/payment-operations/${encodeURIComponent(operationId)}/apply`,
      {}
    );
  }
}
