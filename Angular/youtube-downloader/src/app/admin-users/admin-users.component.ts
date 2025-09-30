import { CommonModule, DatePipe } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { InfiniteScrollModule } from 'ngx-infinite-scroll';
import { AdminUsersService } from '../services/admin-users.service';
import { AdminUserListItem } from '../models/admin-user.model';
import { AdminUserRoleDialogComponent } from './admin-user-role-dialog.component';

@Component({
  selector: 'app-admin-users',
  standalone: true,
  templateUrl: './admin-users.component.html',
  styleUrls: ['./admin-users.component.css'],
  imports: [
    CommonModule,
    DatePipe,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatButtonModule,
    MatDialogModule,
    InfiniteScrollModule
  ]
})
export class AdminUsersComponent implements OnInit {
  users: AdminUserListItem[] = [];
  totalCount = 0;
  pageSize = 20;
  pageIndex = 0;
  filterValue = '';
  loading = false;
  availableRoles: string[] = [];

  constructor(
    private readonly adminUsersService: AdminUsersService,
    private readonly dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.loadRoles();
    this.loadUsers();
  }

  applyFilter(event: Event): void {
    const value = (event.target as HTMLInputElement).value.trim().toLowerCase();
    this.filterValue = value;
    this.pageIndex = 0;
    this.loadUsers();
  }

  clearFilter(): void {
    if (!this.filterValue) {
      return;
    }

    this.filterValue = '';
    this.pageIndex = 0;
    this.loadUsers();
  }

  onScrollDown(): void {
    if (this.loading) {
      return;
    }

    if (this.users.length >= this.totalCount) {
      return;
    }

    this.pageIndex++;
    this.loadUsers(true);
  }

  openUserDialog(user: AdminUserListItem): void {
    const dialogRef = this.dialog.open(AdminUserRoleDialogComponent, {
      width: '480px',
      data: {
        user,
        availableRoles: this.availableRoles
      }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (!result || !Array.isArray(result.roles)) {
        return;
      }

      this.adminUsersService.updateUserRoles(user.id, result.roles).subscribe({
        next: roles => {
          user.roles = roles;
        },
        error: err => {
          console.error('Failed to update roles', err);
        }
      });
    });
  }

  trackByUserId(_: number, item: AdminUserListItem): string {
    return item.id;
  }

  private loadUsers(append = false): void {
    this.loading = true;
    const page = this.pageIndex + 1;

    this.adminUsersService
      .getUsers(page, this.pageSize, this.filterValue)
      .subscribe({
        next: res => {
          this.users = append ? this.users.concat(res.items) : res.items;
          this.totalCount = res.totalCount;
          this.loading = false;
        },
        error: err => {
          console.error('Failed to load users', err);
          this.loading = false;
        }
      });
  }

  private loadRoles(): void {
    this.adminUsersService.getRoles().subscribe({
      next: roles => (this.availableRoles = roles),
      error: err => console.error('Failed to load roles', err)
    });
  }
}
