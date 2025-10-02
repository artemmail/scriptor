import { Component, ElementRef, Input, OnDestroy, ViewChild } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { CommonModule } from '@angular/common';
import { MarkdownModule } from 'ngx-markdown';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatMenuModule } from '@angular/material/menu';
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
    MatIconModule,
    MatDialogModule,
    MatMenuModule,
    LocalTimePipe,
    YandexAdComponent
  ],
  templateUrl: './task-result.component.html',
  styleUrls: ['./task-result.component.css']
})
export class TaskResultComponent implements OnDestroy {
  @Input() youtubeTask: YoutubeCaptionTaskDto | null = null;
  @ViewChild('markdownContent') private markdownContentRef?: ElementRef<HTMLElement>;
  isDownloading = false;
  isCopying = false;
  isResultFullscreen = false;
  private originalBodyOverflow: string | null = null;

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

  get hasResultText(): boolean {
    return !!this.youtubeTask?.result?.trim();
  }

  get canDownloadFromServer(): boolean {
    return !!this.youtubeTask?.id;
  }

  get hasAnyDownloadOption(): boolean {
    return this.canDownloadFromServer || this.hasResultText;
  }

  get hasAnyCopyOption(): boolean {
    return this.hasResultText;
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
    const base = this.getFileBaseName();
    this.subtitleService.generatePdf(this.youtubeTask.id).subscribe({
      next: (blob) => this.finishDownload(blob, `${base}.pdf`),
      error: () => (this.isDownloading = false)
    });
  }

  downloadAsWord(): void {
    if (this.isDownloading || !this.youtubeTask?.id) return;
    this.isDownloading = true;
    const base = this.getFileBaseName();
    this.subtitleService.generateWord(this.youtubeTask.id).subscribe({
      next: (blob) => this.finishDownload(blob, `${base}.docx`),
      error: () => (this.isDownloading = false)
    });
  }

  downloadAsMd(): void {
    if (this.isDownloading || !this.youtubeTask?.result) return;
    this.isDownloading = true;
    const base = this.getFileBaseName();
    this.downloadText(this.youtubeTask.result, 'text/markdown', `${base}.md`);
    this.isDownloading = false;
  }

  downloadAsHtml(): void {
    if (this.isDownloading || !this.youtubeTask?.result) return;
    this.isDownloading = true;
    const content = this.renderMath(this.youtubeTask.result);
    const base = this.getFileBaseName();
    this.downloadText(content, 'text/html', `${base}.html`);
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
    const html = this.markdownContentRef?.nativeElement?.innerHTML?.trim() ?? '';
    const target = html || this.youtubeTask.result;
    navigator.clipboard.writeText(target)
      .finally(() => (this.isCopying = false));
  }

  toggleFullscreen(): void {
    this.isResultFullscreen = !this.isResultFullscreen;
    this.updateBodyScrollLock();
  }

  clearTaskHistory(): void {
    this.youtubeTask = null;
    this.snackBar.open('Task data cleared', '', { duration: 2000 });
  }

  ngOnDestroy(): void {
    if (this.isResultFullscreen) {
      this.isResultFullscreen = false;
      this.updateBodyScrollLock();
    }
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

  private getFileBaseName(): string {
    const title = this.youtubeTask?.title?.trim();
    if (title) {
      return this.sanitizeFileName(title);
    }

    if (this.youtubeTask?.id) {
      return this.sanitizeFileName(`task-${this.youtubeTask.id}`);
    }

    return 'task-result';
  }

  private updateBodyScrollLock(): void {
    if (typeof document === 'undefined') {
      return;
    }

    if (this.isResultFullscreen) {
      if (this.originalBodyOverflow === null) {
        this.originalBodyOverflow = document.body.style.overflow || '';
      }
      document.body.style.overflow = 'hidden';
    } else {
      if (this.originalBodyOverflow !== null) {
        document.body.style.overflow = this.originalBodyOverflow;
        this.originalBodyOverflow = null;
      } else {
        document.body.style.removeProperty('overflow');
      }
    }
  }
}
