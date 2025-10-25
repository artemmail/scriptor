import { Component, OnInit, NgZone } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { AuthService } from '../services/AuthService.service';

@Component({
  standalone: true,
  selector: 'app-login',
  imports: [CommonModule, RouterModule],
  template: `
    <div class="login d-flex flex-column align-items-center mt-5">
      <h2>Login</h2>

      <div class="d-flex flex-column gap-2 mt-3 w-100" style="max-width: 320px;">
        <label class="d-flex gap-2 align-items-start text-start">
          <input
            type="checkbox"
            class="form-check-input mt-1"
            [checked]="allowCalendarManagement"
            (change)="toggleCalendarConsent($event)"
          />
          <span>Разрешить управление календарём из бота</span>
        </label>

        <button
          class="btn btn-primary"
          (click)="loginWithGoogle()">
          Login with a Google ID
        </button>

        <button
          class="btn btn-outline-secondary"
          (click)="loginWithYandex()">
          Login with a Yandex ID
        </button>
      </div>

      <!-- Сообщение об ошибке из callback -->
      <p *ngIf="authError" class="text-danger mt-2 text-center w-75">
        {{ authErrorMessage }}
      </p>
    </div>
  `
})
export class LoginComponent implements OnInit {
  authError = false;
  authErrorMessage = '';
  allowCalendarManagement = false;

  constructor(
    private zone: NgZone,
    private auth: AuthService,
    private route: ActivatedRoute,
    private router: Router
  ) {}

  ngOnInit(): void {
    // Обработка error из query-params
    this.route.queryParams.subscribe(params => {
      const error = params['error'];
      if (error) {
        this.authError = true;
        if (error === 'interaction_required') {
          this.authErrorMessage = 'Для входа требуется взаимодействие. Пожалуйста, войдите в аккаунт выбранного провайдера и попробуйте снова.';
        } else {
          this.authErrorMessage = `Ошибка авторизации: ${error}. Попробуйте войти вручную.`;
        }
      }
    });
  }

  /** интерактивный вход по кнопке */
  loginWithGoogle(): void {
    this.redirectToProvider('google', {
      calendar: this.allowCalendarManagement,
      declined: !this.allowCalendarManagement
    });
  }

  loginWithYandex(): void {
    this.redirectToProvider('yandex');
  }

  toggleCalendarConsent(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    this.allowCalendarManagement = !!target?.checked;
  }

  private redirectToProvider(
    provider: 'google' | 'yandex',
    options?: { calendar?: boolean; declined?: boolean }
  ): void {
    const redirect = encodeURIComponent(`${window.location.origin}/auth/callback`);
    const params = new URLSearchParams({ returnUrl: redirect });

    if (provider === 'google' && options) {
      if (options.calendar) {
        params.set('calendar', 'true');
      } else if (options.declined) {
        params.set('calendarDeclined', 'true');
      }
    }

    window.location.href = `/api/account/signin-${provider}?${params.toString()}`;
  }
}
