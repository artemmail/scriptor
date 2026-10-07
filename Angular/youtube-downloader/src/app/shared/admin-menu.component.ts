import { Component, ElementRef, HostListener, Input } from '@angular/core';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-admin-menu',
  standalone: true,
  imports: [RouterModule],
  template: `
    <details class="am-menu" #menu>
      <summary>{{ label }} <span aria-hidden="true">⌄</span></summary>
      <nav class="am-dropdown" aria-label="Разделы админки">
        <a routerLink="/ServiceNews" routerLinkActive="am-current" ariaCurrentWhenActive="page" (click)="menu.open = false">Лента материалов <span>→</span></a>
        <a routerLink="/admin/users" routerLinkActive="am-current" ariaCurrentWhenActive="page" (click)="menu.open = false">Пользователи <span>→</span></a>
        <a routerLink="/admin/payments" routerLinkActive="am-current" ariaCurrentWhenActive="page" (click)="menu.open = false">Платежи <span>→</span></a>
        <a routerLink="/admin/billing-plans" routerLinkActive="am-current" ariaCurrentWhenActive="page" (click)="menu.open = false">Тарифы <span>→</span></a>
        <a routerLink="/admin/recognition-profiles" routerLinkActive="am-current" ariaCurrentWhenActive="page" (click)="menu.open = false">Профили обработки <span>→</span></a>
      </nav>
    </details>`,
  styles: [`
    :host { display: inline-block; position: relative; z-index: 20; font-family: 'Segoe UI', Arial, sans-serif; }
    .am-menu { position: relative; }
    .am-menu summary { display: flex; align-items: center; gap: 14px; padding: 9px 15px; border: 1px solid #c9d9ae; border-radius: 9px; background: #e8f3d6; color: #344d25; font-size: 12px; font-weight: 650; line-height: 1.5; cursor: pointer; list-style: none; white-space: nowrap; }
    .am-menu summary::-webkit-details-marker { display: none; }
    .am-menu summary::marker { content: ''; }
    .am-menu summary span { font-size: 17px; line-height: 1; transition: transform .2s; }
    .am-menu[open] summary span { transform: rotate(180deg); }
    .am-menu summary:hover, .am-menu[open] summary { background: #d4f568; border-color: #bddf59; }
    .am-menu summary:focus-visible, .am-dropdown a:focus-visible { outline: 2px solid #526b2c; outline-offset: 3px; }
    .am-dropdown { position: absolute; right: 0; top: calc(100% + 8px); display: grid; gap: 3px; width: min(280px, calc(100vw - 32px)); padding: 7px; border: 1px solid #d4dfc4; border-radius: 12px; background: #fffefa; box-shadow: 0 20px 45px rgba(34, 36, 31, .16); }
    .am-dropdown a { display: flex; justify-content: space-between; gap: 18px; padding: 11px 12px; border-radius: 7px; color: #435a32; font-size: 12px; font-weight: 600; line-height: 1.4; text-decoration: none; }
    .am-dropdown a:hover, .am-dropdown a.am-current { background: #eef5e3; color: #314a20; }
    .am-dropdown a span { color: #83a45b; }
    @media (max-width: 600px) { .am-dropdown { right: 0; left: auto; } }
    @media (prefers-reduced-motion: reduce) { .am-menu summary span { transition: none; } }
  `],
})
export class AdminMenuComponent {
  @Input() label = 'Админка';
  constructor(private readonly host: ElementRef<HTMLElement>) {}

  @HostListener('document:click', ['$event'])
  closeOutside(event: MouseEvent): void {
    if (!this.host.nativeElement.contains(event.target as Node)) this.close();
  }

  @HostListener('document:keydown.escape')
  close(): void {
    const details = this.host.nativeElement.querySelector('details');
    if (details) details.open = false;
  }
}
