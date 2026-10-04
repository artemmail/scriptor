import { Component, OnInit, NgZone } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { AuthService } from '../services/AuthService.service';
import { Title } from '@angular/platform-browser';

@Component({
  standalone: true,
  selector: 'app-login',
  imports: [CommonModule, RouterModule],
  templateUrl: './login.component.html',
  styleUrls: ['../shared/account-page.css', './login.component.css']
})
export class LoginComponent implements OnInit {
  authError = false;
  authErrorMessage = '';

  constructor(
    private zone: NgZone,
    private auth: AuthService,
    private route: ActivatedRoute,
    private router: Router,
    private readonly titleService: Title
  ) {
    this.titleService.setTitle('Вход в YouScriptor');
  }

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
