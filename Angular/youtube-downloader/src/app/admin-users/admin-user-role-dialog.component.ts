import { CommonModule } from '@angular/common';
import { Component, Inject } from '@angular/core';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTabsModule } from '@angular/material/tabs';
import { AdminUserListItem } from '../models/admin-user.model';

export interface AdminUserRoleDialogData {
  user: AdminUserListItem;
  availableRoles: string[];
}

@Component({
  selector: 'app-admin-user-role-dialog',
  standalone: true,
  templateUrl: './admin-user-role-dialog.component.html',
  styleUrls: ['./admin-user-role-dialog.component.css'],
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatTabsModule]
})
export class AdminUserRoleDialogComponent {
  selected = new Set<string>();
  readonly ipAddresses: string[];

  constructor(
    @Inject(MAT_DIALOG_DATA) public readonly data: AdminUserRoleDialogData,
    private readonly dialogRef: MatDialogRef<AdminUserRoleDialogComponent>
  ) {
    data.user.roles?.forEach(role => this.selected.add(role));
    const dedupedIps = Array.from(
      new Set((data.user.youtubeCaptionIps ?? []).map(ip => ip.trim()).filter(ip => !!ip))
    );
    this.ipAddresses = dedupedIps.sort((a, b) => a.localeCompare(b));
  }

  toggleRole(role: string, checked: boolean): void {
    if (checked) {
      this.selected.add(role);
    } else {
      this.selected.delete(role);
    }
  }

  isChecked(role: string): boolean {
    return this.selected.has(role);
  }

  close(): void {
    this.dialogRef.close();
  }

  save(): void {
    this.dialogRef.close({ roles: Array.from(this.selected) });
  }
}
