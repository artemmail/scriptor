import { CommonModule, DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Component, DestroyRef, ElementRef, HostListener, Inject, PLATFORM_ID } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { NavigationEnd, Router, RouterModule } from '@angular/router';
import { filter } from 'rxjs/operators';
import { AuthService, UserInfo } from '../services/AuthService.service';
import { SupportDialogComponent } from '../support-dialog/support-dialog.component';
import { MANAGEMENT_LINKS, SITE_LINK_GROUPS, SiteLink, SiteLinkGroup } from './site-navigation';

@Component({
  selector: 'app-site-header',
  standalone: true,
  imports: [CommonModule, RouterModule, MatIconModule],
  templateUrl: './site-header.component.html',
  styleUrls: ['./site-header.component.css'],
})
export class SiteHeaderComponent {
  readonly user$: AuthService['user$'];
  readonly activeOptions = { paths: 'exact', queryParams: 'ignored', matrixParams: 'ignored', fragment: 'exact' } as const;
  mobileLayout = false;
  mobileOpen = false;
  activePanel: string | null = null;
  signingOut = false;
  private trigger: HTMLElement | null = null;

  constructor(
    private readonly auth: AuthService,
    private readonly router: Router,
    private readonly dialog: MatDialog,
    private readonly host: ElementRef<HTMLElement>,
    private readonly destroyRef: DestroyRef,
    @Inject(DOCUMENT) private readonly document: Document,
    @Inject(PLATFORM_ID) platformId: object,
  ) {
    this.user$ = auth.user$;
    if (isPlatformBrowser(platformId)) this.mobileLayout = (document.defaultView?.innerWidth ?? 1280) < 1100;
    router.events.pipe(filter(event => event instanceof NavigationEnd), takeUntilDestroyed(destroyRef))
      .subscribe(() => this.closeMenus());
    this.user$.pipe(takeUntilDestroyed(destroyRef)).subscribe(() => this.closeMenus());
  }

  groupsFor(user: UserInfo | null): readonly SiteLinkGroup[] {
    const links = MANAGEMENT_LINKS.filter(link => user?.roles.some(role => role.toLowerCase() === link.role));
    return links.length ? [...SITE_LINK_GROUPS, { id: 'manage', label: 'Управление', title: 'Управление сайтом', links }] : SITE_LINK_GROUPS;
  }

  trackGroup(_index: number, group: SiteLinkGroup): string { return group.id; }

  isGroupActive(group: SiteLinkGroup): boolean {
    const path = this.router.url.split(/[?#]/)[0];
    if (group.id === 'work' && /^\/(tasks|recognized|edit|y|youtube-downloader)(\/|$)/.test(path)) return true;
    return group.links.some(link => !link.fragment && (path === link.path || path.startsWith(link.path + '/')));
  }

  togglePanel(id: string, event: Event): void {
    this.trigger = event.currentTarget as HTMLElement;
    this.activePanel = this.activePanel === id ? null : id;
  }

  toggleMobile(event: Event): void {
    this.trigger = event.currentTarget as HTMLElement;
    this.mobileOpen = !this.mobileOpen;
    this.activePanel = null;
  }

  closeMenus(restoreFocus = false): void {
    const wasOpen = this.mobileOpen || this.activePanel !== null;
    this.mobileOpen = false;
    this.activePanel = null;
    if (restoreFocus && wasOpen) this.trigger?.focus();
  }

  followLink(link?: SiteLink): void {
    this.closeMenus();
    if (link?.fragment && this.router.url.split(/[?#]/)[0] === link.path) {
      this.document.defaultView?.requestAnimationFrame(() => this.document.getElementById(link.fragment!)?.scrollIntoView({ block: 'start' }));
    }
  }

  skipToContent(event: Event): void {
    event.preventDefault();
    this.closeMenus();
    this.document.getElementById('app-main')?.focus();
  }

  openSupport(): void {
    this.closeMenus();
    this.dialog.open(SupportDialogComponent, {
      panelClass: 'support-dialog-panel', width: '640px', maxWidth: '95vw', maxHeight: '90vh',
      autoFocus: 'first-heading', closeOnNavigation: false,
    });
  }

  logout(): void {
    if (this.signingOut) return;
    this.signingOut = true;
    this.auth.logout().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.signingOut = false;
      this.closeMenus();
      void this.router.navigate(['/login']);
    });
  }

  @HostListener('document:keydown.escape') onEscape(): void { this.closeMenus(true); }
  @HostListener('document:click', ['$event']) onOutsideClick(event: MouseEvent): void {
    if (event.target instanceof Node && !this.host.nativeElement.contains(event.target)) this.closeMenus();
  }
  @HostListener('focusout', ['$event']) onFocusOut(event: FocusEvent): void {
    if (event.relatedTarget instanceof Node && !this.host.nativeElement.contains(event.relatedTarget)) this.closeMenus();
  }
  @HostListener('window:resize') onResize(): void {
    const mobile = (this.document.defaultView?.innerWidth ?? 1280) < 1100;
    if (mobile !== this.mobileLayout) { this.closeMenus(); this.mobileLayout = mobile; }
  }
}
