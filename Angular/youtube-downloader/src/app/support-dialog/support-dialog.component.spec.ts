import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { MatDialogRef } from '@angular/material/dialog';
import { BehaviorSubject } from 'rxjs';
import { AuthService, UserInfo } from '../services/AuthService.service';
import { SupportDialogComponent } from './support-dialog.component';

describe('SupportDialogComponent', () => {
  const user: UserInfo = { id: 'alice', name: 'Alice', displayName: 'Alice', email: 'alice@example.com', roles: [], canHideCaptions: false };
  let users: BehaviorSubject<UserInfo | null>;
  let component: SupportDialogComponent;
  let http: HttpTestingController;
  let dialog: { close: jasmine.Spy; disableClose: boolean };

  beforeEach(async () => {
    users = new BehaviorSubject<UserInfo | null>(user);
    dialog = { close: jasmine.createSpy('close'), disableClose: false };
    await TestBed.configureTestingModule({
      imports: [SupportDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([]),
        { provide: AuthService, useValue: { user$: users.asObservable() } },
        { provide: MatDialogRef, useValue: dialog }]
    }).compileComponents();
    const fixture = TestBed.createComponent(SupportDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    http = TestBed.inject(HttpTestingController);
    component.header = ' Тема ';
    component.text = ' Описание ';
  });

  afterEach(() => http.verify());

  it('sends multipart fields and an attachment once, then shows success', () => {
    const file = new File(['screenshot'], 'screen.png', { type: 'image/png' });
    component.uploadedFile = file;
    component.submit();
    component.submit();
    expect(dialog.disableClose).toBeTrue();
    const request = http.expectOne('/api/support');
    expect(request.request.method).toBe('POST');
    const data = request.request.body as FormData;
    expect(data.get('Header')).toBe('Тема');
    expect(data.get('Text')).toBe('Описание');
    expect(data.get('MessageType')).toBe(component.messageType);
    expect((data.get('UploadedFile') as File).name).toBe('screen.png');
    request.flush({ message: 'Отправлено' });
    expect(component.sent).toBeTrue();
    expect(component.sending).toBeFalse();
    expect(dialog.disableClose).toBeFalse();
  });

  it('keeps the draft and attachment after a server error and allows retry', () => {
    const file = new File(['data'], 'details.txt');
    component.uploadedFile = file;
    component.submit();
    http.expectOne('/api/support').flush({ message: 'Попробуйте позже' }, { status: 503, statusText: 'Unavailable' });
    expect(component.header).toBe(' Тема ');
    expect(component.text).toBe(' Описание ');
    expect(component.uploadedFile).toBe(file);
    expect(component.error).toBe('Попробуйте позже');
    expect(component.canSubmit).toBeTrue();
    expect(dialog.close).not.toHaveBeenCalled();
    component.submit();
    http.expectOne('/api/support').flush({});
    expect(component.sent).toBeTrue();
  });

  it('does not send for a guest or whitespace-only fields', () => {
    users.next(null);
    component.submit();
    expect(component.canSubmit).toBeFalse();
    users.next(user);
    component.text = ' \n ';
    component.submit();
    expect(component.canSubmit).toBeFalse();
    http.expectNone('/api/support');
  });

  it('rejects oversized attachments before sending', () => {
    const file = new File(['data'], 'large.zip');
    Object.defineProperty(file, 'size', { value: component.maxFileSize + 1 });
    const input = { files: [file], value: 'large.zip' };
    component.onFileChange({ target: input } as unknown as Event);
    expect(component.uploadedFile).toBeNull();
    expect(component.fileError).toContain('10 МБ');
    component.submit();
    http.expectNone('/api/support');
    component.removeFile();
    expect(component.canSubmit).toBeTrue();
  });

  it('attaches a pasted screenshot', () => {
    const file = new File(['image'], 'clipboard.png', { type: 'image/png' });
    const preventDefault = jasmine.createSpy('preventDefault');
    component.onPaste({ clipboardData: { files: [file] }, preventDefault } as unknown as ClipboardEvent);
    expect(component.uploadedFile).toBe(file);
    expect(preventDefault).toHaveBeenCalled();
  });
});
