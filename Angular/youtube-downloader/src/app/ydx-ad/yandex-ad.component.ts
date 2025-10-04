// yandex-ad.component.ts
import { Component, AfterViewInit } from '@angular/core';

declare var Ya: any;

@Component({
  selector: 'app-yandex-ad',
  standalone: true,
  template: `
    <!-- Yandex.RTB R-A-14227572-1 -->
    <div id="yandex_rtb_R-A-14227572-1"></div>
  `
})
export class YandexAdComponent implements AfterViewInit {
  ngAfterViewInit(): void {
    const yaContextCb = (window as any).yaContextCb;
    if (yaContextCb) {
      yaContextCb.push(() => {
        Ya.Context.AdvManager.render({
          blockId: 'R-A-14227572-1',
          renderTo: 'yandex_rtb_R-A-14227572-1'
        });
      });
    }
  }
}
