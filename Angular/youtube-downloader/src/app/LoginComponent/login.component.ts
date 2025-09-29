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
    this.redirectToProvider('google');
  }

  loginWithYandex(): void {
    this.redirectToProvider('yandex');
  }

  private redirectToProvider(provider: 'google' | 'yandex'): void {
    const redirect = encodeURIComponent(`${window.location.origin}/auth/callback`);
    window.location.href = `/api/account/signin-${provider}?returnUrl=${redirect}`;
  }
}
