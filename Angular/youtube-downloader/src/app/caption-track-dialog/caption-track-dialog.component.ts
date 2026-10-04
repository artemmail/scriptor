import { CommonModule } from '@angular/common';
import { Component, Inject, OnDestroy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import type { CaptionTrackOption } from '../services/subtitle.service';

export interface CaptionTrackDialogData {
  youtubeId: string;
  tracks: CaptionTrackOption[];
}

@Component({
  selector: 'app-caption-track-dialog',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatDialogModule],
  templateUrl: './caption-track-dialog.component.html',
  styleUrls: ['./caption-track-dialog.component.css']
})
export class CaptionTrackDialogComponent implements OnDestroy {
  secondsLeft = 10;
  private readonly timer = setInterval(() => {
    this.secondsLeft--;
    if (this.secondsLeft <= 0) {
      this.dialogRef.close(null);
    }
  }, 1000);

  constructor(
    @Inject(MAT_DIALOG_DATA) public readonly data: CaptionTrackDialogData,
    private readonly dialogRef: MatDialogRef<CaptionTrackDialogComponent, string | null>
  ) {}

  select(key: string): void {
    this.dialogRef.close(key);
  }

  useAutomatic(): void {
    this.dialogRef.close(null);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }
}
