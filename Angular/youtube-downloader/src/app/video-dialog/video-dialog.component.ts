import { Component, Inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { CommonModule } from '@angular/common';

/** ВАЖНО: Импортируем нужные Material-модули */
import { MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { LocalTimePipe } from '../pipe/local-time.pipe';

/** Интерфейс для данных, передаваемых в диалог */
export interface VideoDialogData {
  videoId: string;
  title: string | null;        // Разрешаем null
  channelName: string | null;  // Разрешаем null
  channelId: string | null;    // Разрешаем null
  uploadDate: string | null;    // Разрешаем null
}

@Component({
  selector: 'app-video-dialog',
  templateUrl: './video-dialog.component.html',
  styleUrls: ['./video-dialog.component.css'],
  /** Делаем компонент standalone и добавляем нужные модули в imports */
  standalone: true,
  imports: [
    LocalTimePipe,
    CommonModule,
    MatDialogModule,
    MatButtonModule
  ]
})
export class VideoDialogComponent {
  safeVideoUrl: SafeResourceUrl;

  constructor(
    public dialogRef: MatDialogRef<VideoDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: VideoDialogData,
    private sanitizer: DomSanitizer
  ) {
    const youtubeUrl = 'https://www.youtube.com/embed/' + data.videoId;
    this.safeVideoUrl = this.sanitizer.bypassSecurityTrustResourceUrl(youtubeUrl);
  }

  onClose(): void {
    this.dialogRef.close();
  }
}
