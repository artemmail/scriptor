import { Component, DestroyRef, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MarkdownEditorComponent } from '../shared/markdown-editor/markdown-editor.component';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, from, Subscription, switchMap } from 'rxjs';
import { SubtitleService } from '../services/subtitle.service';
import { v4 as uuidv4 } from 'uuid';
import { Title } from '@angular/platform-browser';

type ExportFormat = 'md' | 'html' | 'pdf' | 'docx';

@Component({
  selector: 'app-markdown-converter',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MatIconModule, MatProgressSpinnerModule, MarkdownEditorComponent],
  templateUrl: './markdown-converter.component.html',
  styleUrls: ['../shared/account-page.css', './markdown-converter.component.css']
})
export class MarkdownConverterComponent implements OnInit {
  @ViewChild(MarkdownEditorComponent) private markdownEditor?: MarkdownEditorComponent;
  markdownContent = '';
  filename = 'Документ';
  selectedFormat: ExportFormat = 'pdf';
  activeAction = '';
  loadingTask = false;
  taskId: string | null = null;
  taskTitle = '';
  taskErrorMessage = '';
  error = '';
  success = '';
  previewReady = false;
  confirmClear = false;
  copyFallback = '';
  private taskSubscription?: Subscription;
  private destroyed = false;

  readonly exportFormats: { id: ExportFormat; title: string; extension: string; description: string; icon: string }[] = [
    { id: 'pdf', title: 'PDF', extension: '.pdf', description: 'Для чтения и печати', icon: 'picture_as_pdf' },
    { id: 'docx', title: 'Word', extension: '.docx', description: 'Для дальнейшей работы', icon: 'description' },
    { id: 'html', title: 'HTML', extension: '.html', description: 'Для браузера', icon: 'code' },
    { id: 'md', title: 'Markdown', extension: '.md', description: 'Исходный текст', icon: 'notes' }
  ];
  constructor(
    private readonly subtitleService: SubtitleService,
    private readonly route: ActivatedRoute,
    private readonly destroyRef: DestroyRef,
    titleService: Title
  ) {
    titleService.setTitle('Конвертер Markdown — YouScriptor');
    this.destroyRef.onDestroy(() => this.destroyed = true);
  }

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      this.taskSubscription?.unsubscribe();
      this.taskId = params.get('id');
      this.setContent('');
      this.filename = 'Документ';
      this.taskTitle = '';
      this.taskErrorMessage = '';
      this.onContentChange();
      if (this.taskId) this.loadTaskContent(this.taskId);
    });
  }

  get busy(): boolean { return this.loadingTask || !!this.activeAction; }
  get hasContent(): boolean { return !!this.markdownContent.trim(); }
  get wordCount(): number { return this.hasContent ? this.markdownContent.trim().split(/\s+/).length : 0; }
  get safeFilename(): string {
    const name = this.filename.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '-').replace(/[. ]+$/g, '').trim().slice(0, 100);
    return !name ? 'Документ' : /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(name) ? '_' + name : name;
  }
  get canExport(): boolean { return !this.busy && this.hasContent && this.previewReady; }
  get hasMath(): boolean { return this.markdownEditor?.hasMath ?? false; }

  private setContent(content: string): void {
    this.markdownContent = content;
    this.onContentChange();
  }

  onContentChange(): void {
    this.previewReady = false;
    this.error = '';
    this.success = '';
    this.copyFallback = '';
    this.confirmClear = false;
  }

  useExample(): void {
    if (this.busy || this.hasContent) return;
    this.setContent('# Хорошие идеи заслуживают формы\n\nПревратите заметки в **понятный документ** — с заголовками, списками и формулами.\n\n## Начните с главного\n\n- Запишите ключевую мысль\n- Добавьте детали и примеры\n- Выберите формат для скачивания\n\n> Ясный текст начинается с ясной мысли.\n\n## Немного математики\n\nФормула энергии: $E = mc^2$.\n\n| Формат | Для чего |\n| --- | --- |\n| PDF | Чтение и печать |\n| Word | Редактирование |');
  }

  clearContent(): void {
    if (this.busy || !this.confirmClear) return;
    this.setContent('');
  }

  retryTask(): void { if (this.taskId && !this.busy && !this.hasContent) this.loadTaskContent(this.taskId); }

  private loadTaskContent(taskId: string): void {
    this.loadingTask = true;
    this.taskErrorMessage = '';
    this.taskSubscription = this.subtitleService.getStatus(taskId)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loadingTask = false))
      .subscribe({
        next: task => {
          if (!task?.result?.trim()) { this.taskErrorMessage = 'В задаче пока нет текста. Дождитесь завершения обработки или вставьте свой Markdown.'; return; }
          this.setContent(task.result);
          this.taskTitle = task.title || 'Текст из задачи';
          this.filename = task.title || 'Документ';
          this.success = 'Текст задачи загружен. Можно редактировать и скачивать.';
        },
        error: () => this.taskErrorMessage = 'Не удалось загрузить текст задачи. Повторите попытку или вставьте свой Markdown.'
      });
  }

  download(): void {
    if (!this.canExport) return;
    this.resetMessages();
    const format = this.selectedFormat;
    const filename = this.safeFilename + '.' + format;
    if (format === 'md' || format === 'html') {
      const content = format === 'md' ? this.markdownContent : this.htmlDocument();
      this.saveBlob(new Blob([content], { type: format === 'md' ? 'text/markdown;charset=utf-8' : 'text/html;charset=utf-8' }), filename);
      this.success = 'Файл передан браузеру для скачивания.';
      return;
    }
    this.activeAction = format;
    const request = format === 'pdf'
      ? this.subtitleService.generatePdfFromMarkdown(uuidv4(), this.markdownContent)
      : this.subtitleService.generateWordFromMarkdown(uuidv4(), this.markdownContent);
    request.pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.activeAction = '')).subscribe({
      next: blob => { this.saveBlob(blob, filename); this.success = 'Файл передан браузеру для скачивания.'; },
      error: () => this.error = 'Не удалось подготовить файл. Ваш текст остался в редакторе. Попробуйте ещё раз.'
    });
  }

  async copyHtml(): Promise<void> {
    if (!this.canExport) return;
    this.resetMessages();
    this.activeAction = 'html-copy';
    try { await this.copyText(this.markdownEditor?.renderedHtml || ''); }
    finally { this.activeAction = ''; }
  }

  copyBbcode(): void {
    if (!this.canExport) return;
    this.resetMessages();
    this.activeAction = 'bbcode';
    this.subtitleService.generateBbcodeFromMarkdown(uuidv4(), this.markdownContent).pipe(
      switchMap(blob => from(blob.text())),
      switchMap(text => from(this.copyText(text))),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.activeAction = '')
    ).subscribe({ error: () => this.error = 'Не удалось подготовить BBCode. Попробуйте ещё раз.' });
  }

  private async copyText(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      if (!this.destroyed) this.success = 'Скопировано в буфер обмена.';
    } catch {
      if (!this.destroyed) {
        this.copyFallback = text;
        this.error = 'Браузер не разрешил копирование. Выделите и скопируйте готовый код из поля ниже.';
      }
    }
  }

  private resetMessages(): void { this.error = ''; this.success = ''; this.copyFallback = ''; }

  private htmlDocument(): string {
    const escape = (text: string) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    const mathStyles = new URL('assets/katex/katex.min.css', document.baseURI).href;
    return '<!doctype html>\n<html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>' + escape(this.safeFilename) + '</title><link rel="stylesheet" href="' + escape(mathStyles) + '"><style>body{max-width:850px;margin:40px auto;padding:0 24px;font:16px/1.8 system-ui,sans-serif;color:#293323;overflow-wrap:anywhere}h1,h2,h3{line-height:1.3}img{max-width:100%;height:auto}pre,table,.katex-display{overflow-x:auto}pre,blockquote{padding:18px;background:#f1f4e9}blockquote{margin-inline:0;border-left:3px solid #8ca656}table{display:block;border-collapse:collapse}td,th{border:1px solid #d4dcc8;padding:8px 14px}a{color:#526b2c}</style></head><body>' + (this.markdownEditor?.renderedHtml || '') + '</body></html>';
  }

  private saveBlob(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
