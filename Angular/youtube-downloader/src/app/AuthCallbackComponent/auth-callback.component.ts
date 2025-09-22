import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../services/AuthService.service';

@Component({
  standalone: true,
  selector: 'app-auth-callback',
  template: `<p>Авторизация...</p>`,
})
export class AuthCallbackComponent implements OnInit {
  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private authService: AuthService
  ) {}

  ngOnInit(): void {
    this.route.queryParams.subscribe(params => {
      const error = params['error'];
      if (error) {
        console.error('Auth error from callback: ' + error);
        this.router.navigate(['/login'], { queryParams: { error } });
        return;
      }

      const accessToken = params['token'];
      if (accessToken) {
        this.authService.setAccessToken(accessToken);
        this.router.navigate(['/']);
      } else {
        console.error('Токен не найден в URL');
        this.router.navigate(['/login']);
      }
    });
  }
}