import { Injectable } from '@angular/core';
import { CanActivate, ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree, Router } from '@angular/router';
import { Observable } from 'rxjs';
import { map, take } from 'rxjs/operators';
import { AuthService } from './AuthService.service';

@Injectable({ providedIn: 'root' })
export class RoleGuard implements CanActivate {
  constructor(private readonly auth: AuthService, private readonly router: Router) {}

  canActivate(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): Observable<boolean | UrlTree> {
    const roles = (route.data['roles'] as string[] | undefined) ?? [];

    return this.auth.user$.pipe(
      take(1),
      map(user => {
        if (!user) {
          return this.router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
        }

        if (roles.length === 0) {
          return true;
        }

        const hasRole = user.roles.some(userRole =>
          roles.some(required => required.toLowerCase() === userRole.toLowerCase())
        );

        if (hasRole) {
          return true;
        }

        return this.router.createUrlTree(['/blog']);
      })
    );
  }
}
