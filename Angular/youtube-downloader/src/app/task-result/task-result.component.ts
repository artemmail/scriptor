import { Component, Input } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { CommonModule } from '@angular/common';
import { MarkdownModule } from 'ngx-markdown';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatRippleModule } from '@angular/material/core';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar } from '@angular/material/snack-bar';
import { YoutubeCaptionTaskDto } from '../services/subtitle.service';
import { SubtitleService } from '../services/subtitle.service';
import { MarkdownRendererService1 } from './markdown-renderer.service';
import { VideoDialogComponent, VideoDialogData } from '../video-dialog/video-dialog.component';
import { LocalTimePipe } from '../pipe/local-time.pipe';
import { YandexAdComponent } from '../ydx-ad/yandex-ad.component';
import { RouterModule } from '@angular/router'; // ⬅️ Добавить

@Component({
  selector: 'app-task-result',
  standalone: true,
  imports: [
    CommonModule,
    MarkdownModule,
    RouterModule,
    MatCardModule,
    MatButtonModule,
    MatRippleModule,
    MatIconModule,
    MatDialogModule,
    MatTooltipModule,
    LocalTimePipe,
    YandexAdComponent
  ],
  templateUrl: './task-result.component.html',
  styleUrls: ['./task-result.component.css']
})
export class TaskResultComponent {
  @Input() youtubeTask: YoutubeCaptionTaskDto | null = null;
  isDownloading = false;
  isCopying = false;

  private englishPromo = 'YouScriptor: Accurate transcription from YouTube videos to PDF and MS Word with markup and formula display for free.';
  private russianPromo = 'YouScriptor: Точная транскрипция с YouTube видео в PDF и MS Word с разметкой и отображением формул бесплатно.';

  constructor(
    private mk: MarkdownRendererService1,
    private sanitizer: DomSanitizer,
    private subtitleService: SubtitleService,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {}

  get promoText(): string {
    if (!this.youtubeTask?.result) {
      return this.englishPromo;
    }
    return this.hasCyrillic(this.youtubeTask.result) ? this.russianPromo : this.englishPromo;
  }

  private hasCyrillic(text: string): boolean {
    return /[\u0400-\u04FF]/.test(text);
  }

  get processedMarkdown(): SafeHtml {
    if (!this.youtubeTask?.result) {
      return '';
    }
    return this.sanitizer.bypassSecurityTrustHtml(
      this.renderMath(this.youtubeTask.result)
    );
  }

  private renderMath(content: string): string {
    return this.mk.renderMath(content);
  }

  openVideoDialog(task: YoutubeCaptionTaskDto | null): void {
    if (!task) return;
    const data: VideoDialogData = {
      videoId: task.id,
      title: task.title,
      channelName: task.channelName,
      channelId: task.channelId,
      uploadDate: task.uploadDate
    };
    this.dialog.open(VideoDialogComponent, {
      width: '800px',
      data
    });
  }

  downloadAsPdf(): void {
    if (this.isDownloading || !this.youtubeTask?.id) return;
    this.isDownloading = true;
    this.subtitleService.generatePdf(this.youtubeTask.id).subscribe(
      blob => this.finishDownload(blob, `task-${this.youtubeTask?.title}.pdf`),
      () => (this.isDownloading = false)
    );
  }

  downloadAsWord(): void {
    if (this.isDownloading || !this.youtubeTask?.id) return;
    this.isDownloading = true;
    this.subtitleService.generateWord(this.youtubeTask.id).subscribe(
      blob => this.finishDownload(blob, `task-${this.youtubeTask?.title}.docx`),
      () => (this.isDownloading = false)
    );
  }

  downloadAsMd(): void {
    if (this.isDownloading || !this.youtubeTask?.result) return;
    this.isDownloading = true;
    this.downloadText(this.youtubeTask.result, 'text/markdown', `task-${this.youtubeTask.title}.md`);
    this.isDownloading = false;
  }

  downloadAsHtml(): void {
    if (this.isDownloading || !this.youtubeTask?.result) return;
    this.isDownloading = true;
    const content = this.renderMath(this.youtubeTask.result);
    this.downloadText(content, 'text/html', `task-${this.youtubeTask.title}.html`);
    this.isDownloading = false;
  }

  copyToClipboard(): void {
    if (this.isCopying || !this.youtubeTask?.result) return;
    this.isCopying = true;
    navigator.clipboard.writeText(this.youtubeTask.result)
      .then(() => {
        this.snackBar.open('Copied to clipboard', '', { duration: 2000 });
      })
      .finally(() => {
        this.isCopying = false;
      });
  }

  copyHtmlToClipboard(): void {
    if (this.isCopying || !this.youtubeTask?.result) return;
    this.isCopying = true;
    const html = (document.querySelector('.markdown-content') as HTMLElement)?.innerHTML || '';
    navigator.clipboard.writeText(html)
      .finally(() => this.isCopying = false);
  }

  clearTaskHistory(): void {
    this.youtubeTask = null;
    this.snackBar.open('Task data cleared', '', { duration: 2000 });
  }

  private finishDownload(blob: Blob, filename: string) {
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    window.URL.revokeObjectURL(url);
    this.isDownloading = false;
  }

  private downloadText(content: string, mime: string, filename: string) {
    const blob = new Blob([content], { type: mime });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    window.URL.revokeObjectURL(url);
  }

downloadAsSrt(lang?: string): void {
  if (this.isDownloading || !this.youtubeTask?.id) return;
  this.isDownloading = true;

  this.subtitleService.generateSrt(this.youtubeTask.id, lang).subscribe({
    next: (blob) => {
      const title = this.youtubeTask?.title?.trim() || 'subtitles';
      const base = this.sanitizeFileName(title);
      const filename = lang ? `${base}.${lang}.srt` : `${base}.srt`;
      this.finishDownload(blob, filename);
    },
    error: () => { this.isDownloading = false; }
  });
}


  private sanitizeFileName(name: string): string {
    // режем недопустимые символы для Windows/Unix и прибираем пробелы
    return name
      .replace(/[\/\\:\*\?"<>\|]/g, '_')
      .replace(/\s+/g, ' ')
      .trim()
      .substring(0, 120);
  }
}