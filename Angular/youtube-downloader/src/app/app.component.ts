import {
  Component,
  HostListener,
  ViewChild,
  AfterViewInit,
  ElementRef
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { MatSidenavModule, MatSidenav } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatDividerModule } from '@angular/material/divider';

// Импорт для регистрации SVG-иконок
import { MatIconRegistry } from '@angular/material/icon';
import { DomSanitizer } from '@angular/platform-browser';

import { YaMetrikaService } from './services/ya-metrika.service';
import { AuthService, UserInfo } from './services/AuthService.service';
import { Observable } from 'rxjs';

import { SideMenuComponent } from './side-menu/side-menu.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    MatSidenavModule,
    MatToolbarModule,
    MatIconModule,
    MatButtonModule,
    MatListModule,
    MatDividerModule,
    SideMenuComponent
  ],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements AfterViewInit {
  @ViewChild('drawer') drawer!: MatSidenav;

  hideToolbar = false;
  user$!: Observable<UserInfo | null>;

  constructor(
    private router: Router,
    private auth: AuthService,
    private yaMetrika: YaMetrikaService,
    private matIconRegistry: MatIconRegistry,
    private domSanitizer: DomSanitizer
  ) {
    this.user$ = this.auth.user$;

    // Регистрируем SVG-иконку для MS Word
    this.matIconRegistry.addSvgIcon(      'icon-msword',      this.domSanitizer.bypassSecurityTrustResourceUrl('assets/msword.svg')    );

    this.matIconRegistry.addSvgIcon(      'icon-markdown',      this.domSanitizer.bypassSecurityTrustResourceUrl('assets/icon-markdown.svg')    );

        this.matIconRegistry.addSvgIcon(      'icon-html',      this.domSanitizer.bypassSecurityTrustResourceUrl('assets/icon-html.svg')    );

    
  }

  ngAfterViewInit(): void {
    this.router.events
      .pipe(filter(e => e instanceof NavigationEnd))
      .subscribe((e: NavigationEnd) => {
        if (this.drawer.mode === 'over' && this.drawer.opened) {
          this.drawer.close();
        }
        window.scrollTo(0, 0);
        this.hideToolbar = false;
        this.yaMetrika.hit(e.urlAfterRedirects, 'YouScriptor');
      });
  }

  @HostListener('window:scroll')
  onWindowScroll(): void {
    const scrollTop =
      window.pageYOffset ||
      document.documentElement.scrollTop ||
      document.body.scrollTop ||
      0;
    this.hideToolbar = scrollTop > 100;
  }

  onLogout(): void {
    this.auth.logout();
    this.router.navigate(['/login']).then(() => this.drawer.close());
  }
}
