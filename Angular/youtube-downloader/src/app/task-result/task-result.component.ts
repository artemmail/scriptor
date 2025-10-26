import { Component, DestroyRef, ElementRef, Input, OnDestroy, ViewChild } from '@angular/core';
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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '../services/AuthService.service';
import { ActionMenuPanelDirective } from '../shared/action-menu-panel.directive';

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
    YandexAdComponent,
    ActionMenuPanelDirective
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
  isAuthenticated = false;

  private englishPromo = 'YouScriptor: Accurate transcription from YouTube videos to PDF and MS Word with markup and formula display for free.';
  private russianPromo = 'YouScriptor: Точная транскрипция с YouTube видео в PDF и MS Word с разметкой и отображением формул бесплатно.';

  constructor(
    private mk: MarkdownRendererService1,
    private sanitizer: DomSanitizer,
    private subtitleService: SubtitleService,
    private dialog: MatDialog,
    private snackBar: MatSnackBar,
    private readonly authService: AuthService,
    private readonly destroyRef: DestroyRef
  ) {
    this.authService.user$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(user => {
        this.isAuthenticated = !!user;
      });
  }

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
    return (
      this.canCopyPlainText ||
      this.canCopyMarkdown ||
      this.canCopyHtml ||
      this.canCopyBbcode ||
      this.canCopySrt
    );
  }

  get canCopyPlainText(): boolean {
    return this.hasResultText;
  }

  get canCopyMarkdown(): boolean {
    return this.hasResultText;
  }

  get canCopyHtml(): boolean {
    return this.hasResultText;
  }

  get canCopyBbcode(): boolean {
    return this.hasResultText && this.canDownloadFromServer;
  }

  get canCopySrt(): boolean {
    return this.canDownloadFromServer;
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
    if (!task || !this.isAuthenticated) return;
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

  downloadAsTxt(): void {
    if (this.isDownloading || !this.youtubeTask?.result) return;
    this.isDownloading = true;
    const base = this.getFileBaseName();
    const plainText = this.stripMarkdown(this.youtubeTask.result);
    this.downloadText(plainText, 'text/plain', `${base}.txt`);
    this.isDownloading = false;
  }

  copyPlainTextToClipboard(): void {
    if (this.isCopying || !this.youtubeTask?.result) {
      return;
    }

    const plainText = this.stripMarkdown(this.youtubeTask.result);
    if (!plainText.trim()) {
      return;
    }

    this.isCopying = true;
    this.copyTextToClipboard(plainText, 'Текст скопирован в буфер обмена');
  }

  copyMarkdownToClipboard(): void {
    const markdown = this.youtubeTask?.result;
    if (this.isCopying || !markdown?.trim()) {
      return;
    }

    this.isCopying = true;
    this.copyTextToClipboard(markdown, 'Markdown скопирован в буфер обмена');
  }

  copyHtmlToClipboard(): void {
    if (this.isCopying || !this.youtubeTask?.result) {
      return;
    }

    const html = this.markdownContentRef?.nativeElement?.innerHTML ?? '';
    const target = html.trim().length ? html : this.youtubeTask.result;
    if (!target?.trim()) {
      return;
    }

    this.isCopying = true;
    this.copyTextToClipboard(target, 'HTML скопирован в буфер обмена');
  }

  copyBbcodeToClipboard(): void {
    if (this.isCopying || !this.youtubeTask?.id || !this.youtubeTask.result?.trim()) {
      return;
    }

    this.isCopying = true;
    this.subtitleService.generateBbcodeFromMarkdown(this.youtubeTask.id, this.youtubeTask.result).subscribe({
      next: (blob) => {
        blob.text().then((text) => {
          if (!text.trim()) {
            this.isCopying = false;
            this.snackBar.open('BBCode недоступен.', 'OK', { duration: 3000 });
            return;
          }
          this.copyTextToClipboard(text, 'BBCode скопирован в буфер обмена');
        }).catch((error) => {
          console.error('Blob read error', error);
          this.isCopying = false;
          this.snackBar.open('Не удалось подготовить BBCode.', 'OK', { duration: 3000 });
        });
      },
      error: (error) => {
        console.error('BBCode export error', error);
        this.isCopying = false;
        this.snackBar.open('Не удалось подготовить BBCode.', 'OK', { duration: 3000 });
      }
    });
  }

  copySrtToClipboard(): void {
    if (this.isCopying || !this.youtubeTask?.id) {
      return;
    }

    this.isCopying = true;
    this.subtitleService.generateSrt(this.youtubeTask.id).subscribe({
      next: (blob) => {
        blob.text().then((text) => {
          if (!text.trim()) {
            this.isCopying = false;
            this.snackBar.open('SRT недоступен.', 'OK', { duration: 3000 });
            return;
          }
          this.copyTextToClipboard(text, 'SRT скопирован в буфер обмена');
        }).catch((error) => {
          console.error('Blob read error', error);
          this.isCopying = false;
          this.snackBar.open('Не удалось подготовить SRT.', 'OK', { duration: 3000 });
        });
      },
      error: (error) => {
        console.error('SRT export error', error);
        this.isCopying = false;
        this.snackBar.open('Не удалось подготовить SRT.', 'OK', { duration: 3000 });
      }
    });
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

  private copyTextToClipboard(
    text: string,
    successMessage: string,
    errorMessage = 'Не удалось скопировать в буфер обмена'
  ): void {
    navigator.clipboard.writeText(text)
      .then(() => {
        this.snackBar.open(successMessage, '', { duration: 2000 });
      })
      .catch((error) => {
        console.error('Clipboard copy error', error);
        this.snackBar.open(errorMessage, 'OK', { duration: 3000 });
      })
      .finally(() => {
        this.isCopying = false;
      });
  }

  private stripMarkdown(content: string): string {
    return content
      .replace(/```[\s\S]*?```/g, '')
      .replace(/`([^`]+)`/g, '$1')
      .replace(/\*\*([^*]+)\*\*/g, '$1')
      .replace(/\*([^*]+)\*/g, '$1')
      .replace(/\[(.*?)\]\((.*?)\)/g, '$1')
      .replace(/^#+\s*(.*)$/gm, '$1')
      .replace(/^>\s?(.*)$/gm, '$1')
      .replace(/^[\-*+]\s+(.*)$/gm, '$1')
      .replace(/\r?\n{3,}/g, '\n\n');
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
