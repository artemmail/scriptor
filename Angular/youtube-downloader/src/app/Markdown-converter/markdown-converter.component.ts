import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatMenuModule } from '@angular/material/menu';
import { LMarkdownEditorModule } from 'ngx-markdown-editor';
import { MatCardModule } from '@angular/material/card';
import { ActionMenuPanelDirective } from '../shared/action-menu-panel.directive';
import { SubtitleService } from '../services/subtitle.service';
import { MarkdownRendererService1 } from '../task-result/markdown-renderer.service';
import { v4 as uuidv4 } from 'uuid';
import { YoutubeCaptionTaskDto } from '../services/subtitle.service';

@Component({
  selector: 'app-markdown-converter',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    LMarkdownEditorModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule,
    MatMenuModule,
    ActionMenuPanelDirective
  ],
  templateUrl: './markdown-converter.component.html',
  styleUrls: ['./markdown-converter.component.css']
})
export class MarkdownConverterComponent implements OnInit {
  markdownContent = '';
  isDownloading = false;
  taskId: string | null = null;
  taskErrorMessage: string | null = null;

  editorOptions = {
    placeholder: 'Пишите Markdown и LaTeX: $…$ или $$…$$',
    katex: true,
    theme: 'github',
    lineNumbers: true,
    dragDrop: true,
    showPreviewPanel: true,
    hideIcons: []
  };

  constructor(
    private subtitleService: SubtitleService,
    private snackBar: MatSnackBar,
    private mk: MarkdownRendererService1,
    private route: ActivatedRoute
  ) {
    this.renderWithMath = this.renderWithMath.bind(this);
  }

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      this.taskId = params.get('id');
      if (this.taskId) {
        this.loadTaskContent(this.taskId);
      }
    });
  }

  private loadTaskContent(taskId: string): void {
    this.subtitleService.getStatus(taskId).subscribe(
      (task: YoutubeCaptionTaskDto) => {
        if (task && task.result) {
          this.markdownContent = task.result;
          this.snackBar.open('Task content loaded', '', { duration: 2000 });
        } else {
          this.taskErrorMessage = 'Task is not ready or has no content';
          this.snackBar.open(this.taskErrorMessage, 'OK', { duration: 3000 });
        }
      },
      error => {
        console.error('Error loading task:', error);
        this.taskErrorMessage = 'Error loading task content';
        this.snackBar.open(this.taskErrorMessage, 'OK', { duration: 3000 });
      }
    );
  }

  renderWithMath(content: string): string {
    return this.mk.renderMath(content);
  }

  onDownloadMd() {
    if (!this.markdownContent.trim()) return;
    this.downloadText(this.markdownContent, 'text/markdown', 'converted.md');
  }

  onDownloadHtml() {
    if (!this.markdownContent.trim()) return;
    const content = this.renderWithMath(this.markdownContent);
    this.downloadText(content, 'text/html', 'converted.html');
  }

  onDownloadPdf() {
    if (this.isDownloading || !this.markdownContent.trim()) return;
    this.isDownloading = true;
    const tempId = uuidv4();
    this.subtitleService.generatePdfFromMarkdown(tempId, this.markdownContent).subscribe(
      blob => this.finishDownload(blob, 'converted.pdf'),
      () => (this.isDownloading = false)
    );
  }

  onDownloadWord() {
    if (this.isDownloading || !this.markdownContent.trim()) return;
    this.isDownloading = true;
    const tempId = uuidv4();
    this.subtitleService.generateWordFromMarkdown(tempId, this.markdownContent).subscribe(
      blob => this.finishDownload(blob, 'converted.docx'),
      () => (this.isDownloading = false)
    );
  }

  onCopyHtml() {
    if (!this.markdownContent.trim()) return;
    const content = this.renderWithMath(this.markdownContent);
    navigator.clipboard.writeText(content).then(() => {
      this.snackBar.open('Copied to clipboard', '', { duration: 2000 });
    }).catch(err => {
      console.error('Clipboard copy error:', err);
      this.snackBar.open('Error copying to clipboard', 'OK', { duration: 3000 });
    });
  }

  onCopyBbcode() {
    if (this.isDownloading || !this.markdownContent.trim()) return;
    this.isDownloading = true;
    const tempId = uuidv4();
    this.subtitleService.generateBbcodeFromMarkdown(tempId, this.markdownContent).subscribe(
      blob => {
        blob.text().then(text => {
          navigator.clipboard.writeText(text).then(() => {
            this.snackBar.open('Copied to clipboard', '', { duration: 2000 });
            this.isDownloading = false;
          }).catch(err => {
            console.error('Clipboard copy error:', err);
            this.snackBar.open('Error copying to clipboard', 'OK', { duration: 3000 });
            this.isDownloading = false;
          });
        }).catch(err => {
          console.error('Blob read error:', err);
          this.snackBar.open('Error reading content', 'OK', { duration: 3000 });
          this.isDownloading = false;
        });
      },
      err => {
        console.error('Service error:', err);
        this.snackBar.open('Error generating BBCode', 'OK', { duration: 3000 });
        this.isDownloading = false;
      }
    );
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

  get hasContent(): boolean {
    return this.markdownContent.trim().length > 0;
  }
}
