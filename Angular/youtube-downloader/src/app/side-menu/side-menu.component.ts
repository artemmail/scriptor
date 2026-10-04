import { Component, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatDividerModule } from '@angular/material/divider';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { SupportDialogComponent } from '../support-dialog/support-dialog.component';
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
    MatDialogModule,
    RouterModule
  ],
  templateUrl: './side-menu.component.html',
})
export class SideMenuComponent {
  @Output() close = new EventEmitter<void>();
  user$: Observable<UserInfo | null>;

  constructor(private auth: AuthService, private router: Router, private dialog: MatDialog) {

    this.user$ = this.auth.user$;
  }

  hasRole(user: UserInfo | null, role: string): boolean {
    return !!user?.roles?.some(r => r.toLowerCase() === role.toLowerCase());
  }

  openSupport(): void {
    this.close.emit();
    this.dialog.open(SupportDialogComponent, {
      panelClass: 'support-dialog-panel',
      width: '640px',
      maxWidth: '95vw',
      maxHeight: '90vh',
      autoFocus: 'first-heading',
      restoreFocus: false,
      closeOnNavigation: false
    });
  }

  navigate(path: string) {
    this.router.navigate([path]).then(() => this.close.emit());
  }
}
