import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

export interface BulkVideoDialogData {
  value?: string;
}

@Component({
  selector: 'app-bulk-video-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
  ],
  templateUrl: './bulk-video-dialog.component.html',
  styleUrls: ['./bulk-video-dialog.component.css'],
})
export class BulkVideoDialogComponent {
  inputValue = '';

  constructor(
    private dialogRef: MatDialogRef<BulkVideoDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: BulkVideoDialogData
  ) {
    this.inputValue = data?.value ?? '';
  }

  cancel(): void {
    this.dialogRef.close(null);
  }

  submit(): void {
    if (!this.inputValue.trim()) {
      return;
    }
    this.dialogRef.close(this.inputValue);
  }
}
