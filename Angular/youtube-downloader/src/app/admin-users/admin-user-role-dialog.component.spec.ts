import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { provideNativeDateAdapter } from '@angular/material/core';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatSelectHarness } from '@angular/material/select/testing';
import { MatButtonHarness } from '@angular/material/button/testing';
import { of } from 'rxjs';
import { AdminUserRoleDialogComponent } from './admin-user-role-dialog.component';
import { AdminUsersService } from '../services/admin-users.service';
import { PaymentsService } from '../services/payments.service';

describe('Admin manual payment dialog', () => {
  let users: jasmine.SpyObj<AdminUsersService>;
  const plans = [
    { code: 'welcome_free', name: 'Стартовый пакет', price: 0, currency: 'RUB', includedVideos: 3, includedTranscriptionMinutes: 60 },
    { code: 'credits_3000', name: 'Пакет 3000', price: 3000, currency: 'RUB', includedVideos: 160, includedTranscriptionMinutes: 4800 }
  ];

  beforeEach(async () => {
    users = jasmine.createSpyObj('AdminUsersService', ['getUserSubscription', 'createManualSubscriptionPayment']);
    users.getUserSubscription.and.returnValue(of({ payments: [], remainingVideos: 0 } as any));
    users.createManualSubscriptionPayment.and.returnValue(of({} as any));
    await TestBed.configureTestingModule({
      imports: [AdminUserRoleDialogComponent, MatDialogModule, NoopAnimationsModule],
      providers: [
        provideNativeDateAdapter(),
        { provide: AdminUsersService, useValue: users },
        { provide: PaymentsService, useValue: { getPlans: () => of(plans) } }
      ]
    }).compileComponents();
  });

  afterEach(() => TestBed.inject(MatDialog).closeAll());

  it('updates tariff fields, opens both calendars and refreshes history after saving', async () => {
    // Use a real Material dialog so popup providers are resolved as in the app.
    const ref = TestBed.inject(MatDialog).open(AdminUserRoleDialogComponent, {
      data: { user: { id: 'paid-user', email: 'test@example.com', roles: [] }, availableRoles: [] }
    });
    const host = TestBed.createComponent(TestHostComponent);
    host.detectChanges();
    const loader = TestbedHarnessEnvironment.documentRootLoader(host);
    const tabs = Array.from(document.querySelectorAll<HTMLElement>('[role="tab"]'));
    tabs.find(tab => tab.textContent?.includes('Платежи'))!.click();
    await host.whenStable();
    host.detectChanges();
    const select = await loader.getHarness(MatSelectHarness);
    await select.open();
    await select.clickOptions({ text: /Пакет 3000/ });
    expect(ref.componentInstance.manualPaymentForm.value.amount).toBe(3000);
    expect(ref.componentInstance.manualPaymentForm.value.currency).toBe('RUB');
    const toggles = document.querySelectorAll<HTMLButtonElement>('mat-datepicker-toggle button');
    for (const toggle of Array.from(toggles)) {
      toggle.click();
      await host.whenStable();
      expect(document.querySelector('mat-calendar')).not.toBeNull();
      const backdrop = Array.from(document.querySelectorAll<HTMLElement>('.cdk-overlay-backdrop')).pop();
      backdrop!.click();
      await host.whenStable();
    }
    const save = await loader.getHarness(MatButtonHarness.with({ text: 'Сохранить платёж' }));
    users.getUserSubscription.and.returnValue(of({
      payments: [{ invoiceId: 'manual-invoice', amount: 3000, currency: 'RUB', paymentProvider: 'Manual' }],
      remainingVideos: 160, remainingTranscriptionMinutes: 4800
    } as any));
    await save.click();
    expect(users.createManualSubscriptionPayment).toHaveBeenCalledWith(jasmine.objectContaining({
      userId: 'paid-user', planCode: 'credits_3000', amount: 3000, currency: 'RUB'
    }));
    expect(users.getUserSubscription).toHaveBeenCalledTimes(2);
    expect(document.querySelector('.payments-table')?.textContent).toContain('3000 RUB');
    expect(document.querySelector('.summary-grid')?.textContent).toContain('160 видео');
  });
});

import { Component } from '@angular/core';
@Component({ standalone: true, template: '' })
class TestHostComponent {}
