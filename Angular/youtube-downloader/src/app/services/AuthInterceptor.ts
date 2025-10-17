import { inject } from '@angular/core';
import {
  HttpRequest, HttpHandlerFn, HttpEvent,
  HttpErrorResponse, HttpInterceptorFn
} from '@angular/common/http';
import { Router } from '@angular/router';
import { AuthService } from '../services/AuthService.service';
import { Observable, BehaviorSubject, throwError } from 'rxjs';
import { catchError, filter, first, switchMap } from 'rxjs/operators';

let isRefreshing = false;
const refreshTokenSubject = new BehaviorSubject<string | null>(null);

export const authInterceptor: HttpInterceptorFn =
  (req: HttpRequest<any>, next: HttpHandlerFn): Observable<HttpEvent<any>> => {
    const authService = inject(AuthService);
    const router      = inject(Router);
    const token       = authService.getAccessToken();

    // не трогаем ручки логина / refresh
    if (
      req.url.endsWith('/login') ||
      req.url.endsWith('/refresh-token') ||
      req.url.endsWith('/logout')
    ) {
      return next(req);
    }

    // подкладываем Bearer
    const authReq = token
      ? req.clone({ headers: req.headers.set('Authorization', `Bearer ${token}`) })
      : req;

    return next(authReq).pipe(
      catchError(err => {
        if (err instanceof HttpErrorResponse && [401, 419].includes(err.status)) {
          if (!token) {
            authService.clearAuthState();
            router.navigate(['/login']);
            return throwError(() => err);
          }
          // пробуем обновить JWT
          return attemptRefresh(authReq, next, authService, router);
        }
        return throwError(() => err);
      })
    );
  };

function attemptRefresh(
  req: HttpRequest<any>,
  next: HttpHandlerFn,
  auth: AuthService,
  router: Router
): Observable<HttpEvent<any>> {
  if (!isRefreshing) {
    isRefreshing = true;
    refreshTokenSubject.next(null);

    return auth.refreshToken().pipe(
      switchMap(res => {
        isRefreshing = false;
        auth.setAccessToken(res.token);
        refreshTokenSubject.next(res.token);
        return next(req.clone({
          headers: req.headers.set('Authorization', `Bearer ${res.token}`)
        }));
      }),
      catchError(finalErr => {
        isRefreshing = false;
        // удаляем токены и уводим на /login
        auth.clearAuthState();
        auth.logout().subscribe({
          next: () => router.navigate(['/login']),
          error: () => router.navigate(['/login'])
        });
        return throwError(() => finalErr);
      })
    );
  }

  // уже идёт обновление → ждём
  return refreshTokenSubject.pipe(
    filter(t => t != null),
    first(),
    switchMap(t =>
      next(req.clone({ headers: req.headers.set('Authorization', `Bearer ${t}`) }))
    )
  );
}