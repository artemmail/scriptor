import { Directive, OnInit } from '@angular/core';
import { MatMenu } from '@angular/material/menu';

@Directive({
  selector: 'mat-menu[actionMenuPanel]',
  standalone: true,
})
export class ActionMenuPanelDirective implements OnInit {
  constructor(private readonly matMenu: MatMenu) {}

  ngOnInit(): void {
    const existing = this.matMenu.panelClass;
    const classes = new Set<string>();

    if (Array.isArray(existing)) {
      existing.filter(Boolean).forEach((value) => classes.add(value));
    } else if (typeof existing === 'string' && existing.trim().length) {
      existing
        .split(/\s+/)
        .filter(Boolean)
        .forEach((value) => classes.add(value));
    }

    classes.add('action-menu-panel');
    this.matMenu.panelClass = Array.from(classes).join(' ');
  }
}
