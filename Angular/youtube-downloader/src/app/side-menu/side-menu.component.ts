import { Component, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatDividerModule } from '@angular/material/divider';
import { MatExpansionModule } from '@angular/material/expansion';
import { RouterModule, Router } from '@angular/router';
import { Observable } from 'rxjs';
import { AuthService, UserInfo } from '../services/AuthService.service';

@Component({
  selector: 'app-side-menu',
  standalone: true,
  imports: [
    CommonModule,
    MatListModule,
    MatIconModule,
    MatDividerModule,
    MatExpansionModule,
    RouterModule
  ],
  templateUrl: './side-menu.component.html',
})
export class SideMenuComponent {
  @Output() close = new EventEmitter<void>();
  user$: Observable<UserInfo | null>;

  constructor(private auth: AuthService, private router: Router) {

    this.user$ = this.auth.user$;
  }

  hasRole(user: UserInfo | null, role: string): boolean {
    return !!user?.roles?.some(r => r.toLowerCase() === role.toLowerCase());
  }

  navigate(path: string) {
    this.router.navigate([path]).then(() => this.close.emit());
  }
}
