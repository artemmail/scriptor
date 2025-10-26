import { CommonModule, DatePipe } from '@angular/common';
import { AfterViewInit, Component, OnInit, ViewChild } from '@angular/core';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { InfiniteScrollModule } from 'ngx-infinite-scroll';
import { MatTableDataSource, MatTableModule } from '@angular/material/table';
import { MatSort, MatSortModule, MatSortable, Sort, SortDirection } from '@angular/material/sort';
import { AdminUsersService } from '../services/admin-users.service';
import { Title } from '@angular/platform-browser';
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
    InfiniteScrollModule,
    MatSortModule,
    MatTableModule
  ]
})
export class AdminUsersComponent implements OnInit, AfterViewInit {
  users: AdminUserListItem[] = [];
  totalCount = 0;
  pageSize = 20;
  pageIndex = 0;
  filterValue = '';
  loading = false;
  availableRoles: string[] = [];
  displayedColumns: string[] = ['email', 'registeredAt', 'recognizedVideos', 'roles'];
  sortActive = 'recognizedVideos';
  sortDirection: SortDirection = 'desc';
  dataSource = new MatTableDataSource<AdminUserListItem>([]);
  private ignoreNextSortEvent = false;

  @ViewChild(MatSort) private sortDirective?: MatSort;

  constructor(
    private readonly adminUsersService: AdminUsersService,
    private readonly dialog: MatDialog,
    private readonly router: Router,
    private readonly titleService: Title
  ) {
    this.titleService.setTitle('Админка — пользователи YouScriptor');
  }

  ngOnInit(): void {
    this.loadRoles();
    this.loadUsers();
  }

  ngAfterViewInit(): void {
    if (!this.sortDirective) {
      return;
    }

    this.sortDirective.disableClear = true;
    this.dataSource.sortData = data => data;
    this.dataSource.sort = this.sortDirective;

    const initialSort: MatSortable = {
      id: this.sortActive,
      start: this.sortDirection || 'asc',
      disableClear: true
    };

    this.ignoreNextSortEvent = true;
    this.sortDirective.sort(initialSort);
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

  private loadUsers(append = false): void {
    this.loading = true;
    const page = this.pageIndex + 1;

    const sortOrder = this.sortDirection === '' ? undefined : this.sortDirection;
    const filter = this.filterValue || undefined;

    this.adminUsersService
      .getUsers(page, this.pageSize, filter, this.sortActive, sortOrder)
      .subscribe({
        next: res => {
          this.users = append ? this.users.concat(res.items) : res.items;
          this.dataSource.data = this.users;
          this.totalCount = res.totalCount;
          this.loading = false;
        },
        error: err => {
          console.error('Failed to load users', err);
          this.loading = false;
        }
      });
  }

  goToUserTasks(user: AdminUserListItem, event: MouseEvent): void {
    event.stopPropagation();
    this.router.navigate(['/tasks'], { queryParams: { userId: user.id } });
  }

  onSortChange(sort: Sort): void {
    if (this.ignoreNextSortEvent) {
      this.ignoreNextSortEvent = false;
      return;
    }

    if (!sort.active) {
      return;
    }

    const direction = sort.direction || 'asc';

    if (this.sortActive === sort.active && this.sortDirection === direction) {
      return;
    }

    this.sortActive = sort.active;
    this.sortDirection = direction;
    this.pageIndex = 0;
    this.loadUsers();
  }

  private loadRoles(): void {
    this.adminUsersService.getRoles().subscribe({
      next: roles => (this.availableRoles = roles),
      error: err => console.error('Failed to load roles', err)
    });
  }
}
