import { afterNextRender, Component, DestroyRef, Inject, Injector } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterModule } from '@angular/router';
import { filter } from 'rxjs/operators';
import { MatIconRegistry } from '@angular/material/icon';
import { DomSanitizer } from '@angular/platform-browser';
import { SiteHeaderComponent } from './shared/site-header.component';
import { YaMetrikaService } from './services/ya-metrika.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterModule, SiteHeaderComponent],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css'],
})
export class AppComponent {
  constructor(
    router: Router,
    metrika: YaMetrikaService,
    icons: MatIconRegistry,
    sanitizer: DomSanitizer,
    destroyRef: DestroyRef,
    injector: Injector,
    @Inject(DOCUMENT) document: Document,
  ) {
    icons.addSvgIcon('icon-msword', sanitizer.bypassSecurityTrustResourceUrl('assets/msword.svg'));
    icons.addSvgIcon('icon-markdown', sanitizer.bypassSecurityTrustResourceUrl('assets/icon-markdown.svg'));
    icons.addSvgIcon('icon-html', sanitizer.bypassSecurityTrustResourceUrl('assets/icon-html.svg'));

    router.events.pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd), takeUntilDestroyed(destroyRef))
      .subscribe(event => {
        metrika.hit(event.urlAfterRedirects, 'YouScriptor');
        afterNextRender(() => {
          const fragment = router.parseUrl(event.urlAfterRedirects).fragment;
          const target = fragment ? document.getElementById(fragment) : null;
          if (target) target.scrollIntoView({ block: 'start' });
          else document.defaultView?.scrollTo(0, 0);
        }, { injector });
      });
  }
}
