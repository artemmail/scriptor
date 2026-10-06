import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { AuthService } from '../services/AuthService.service';
import { AdminMenuComponent } from './admin-menu.component';

@Component({
  selector: 'app-admin-workspace',
  standalone: true,
  imports: [CommonModule, RouterModule, AdminMenuComponent],
  template: `
    <div class="ap-page"><main class="ap-container ad-container">
      <div class="ad-topline"><a routerLink="/">← На главную</a><span>YOUSCRIPTOR / УПРАВЛЕНИЕ</span></div>
      <nav class="ad-nav" aria-label="Разделы управления" *ngIf="auth.user$ | async as user">
        <app-admin-menu *ngIf="hasRole(user.roles, 'admin')" label="Разделы админки"></app-admin-menu>
        <a *ngIf="hasRole(user.roles, 'moderator')" routerLink="/blog/new" routerLinkActive="ad-active" ariaCurrentWhenActive="page">Публикация</a>
        <a routerLink="/blog">Блог ↗</a>
      </nav>
      <header class="ad-heading"><div><p class="ap-eyebrow">{{ eyebrow }}</p><h1>{{ title }}<span>.</span></h1><p>{{ description }}</p></div><div class="ad-heading-actions"><ng-content select="[adminActions]"></ng-content></div></header>
      <ng-content></ng-content>
    </main></div>`,
  styleUrls: ['./account-page.css', './admin-workspace.css'],
})
export class AdminWorkspaceComponent {
  @Input() title = '';
  @Input() description = '';
  @Input() eyebrow = 'ПАНЕЛЬ УПРАВЛЕНИЯ';
  constructor(public readonly auth: AuthService) {}
  hasRole(roles: string[], role: string): boolean { return roles?.some(value => value.toLowerCase() === role) ?? false; }
}
