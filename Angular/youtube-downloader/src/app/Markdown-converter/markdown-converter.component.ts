import { Component, DestroyRef, ElementRef, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MarkdownComponent } from 'ngx-markdown';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, from, Subscription, switchMap } from 'rxjs';
import { SubtitleService } from '../services/subtitle.service';
import { v4 as uuidv4 } from 'uuid';
import { Title } from '@angular/platform-browser';

type ExportFormat = 'md' | 'html' | 'pdf' | 'docx';

@Component({
  selector: 'app-markdown-converter',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MatIconModule, MatProgressSpinnerModule, MarkdownComponent],
  templateUrl: './markdown-converter.component.html',
  styleUrls: ['../shared/account-page.css', './markdown-converter.component.css']
})
export class MarkdownConverterComponent implements OnInit {
  @ViewChild('sourceEditor') private sourceEditor?: ElementRef<HTMLTextAreaElement>;
  @ViewChild('renderedDocument', { read: ElementRef }) private renderedDocument?: ElementRef<HTMLElement>;
  markdownContent = '';
  filename = 'Документ';
  viewMode: 'split' | 'write' | 'preview' = 'split';
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
  readonly formats = [
    { label: 'Заголовок', icon: 'title', before: '## ', after: '', sample: 'Заголовок раздела', block: true },
    { label: 'Жирный', icon: 'format_bold', before: '**', after: '**', sample: 'Важная мысль', block: false },
    { label: 'Курсив', icon: 'format_italic', before: '*', after: '*', sample: 'Акцент', block: false },
    { label: 'Список', icon: 'format_list_bulleted', before: '- ', after: '', sample: 'Пункт списка', block: true },
    { label: 'Цитата', icon: 'format_quote', before: '> ', after: '', sample: 'Текст цитаты', block: true },
    { label: 'Ссылка', icon: 'link', before: '[', after: '](https://example.com)', sample: 'Текст ссылки', block: false },
    { label: 'Изображение', icon: 'image', before: '![', after: '](https://example.com/image.jpg)', sample: 'Описание изображения', block: true },
    { label: 'Код', icon: 'code', before: '```\n', after: '\n```', sample: 'Ваш код', block: true },
    { label: 'Формула LaTeX', icon: 'functions', before: '$$\n', after: '\n$$', sample: 'E = mc^2', block: true }
  ];
  readonly katexOptions = {
    throwOnError: false, trust: false,
    delimiters: [
      { left: '$$', right: '$$', display: true }, { left: '$', right: '$', display: false },
      { left: '\\(', right: '\\)', display: false }, { left: '\\[', right: '\\]', display: true }
    ]
  };

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
      this.markdownContent = '';
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

  onContentChange(): void {
    this.previewReady = false;
    this.error = '';
    this.success = '';
    this.copyFallback = '';
    this.confirmClear = false;
  }

  useExample(): void {
    if (this.busy || this.hasContent) return;
    this.markdownContent = '# Хорошие идеи заслуживают формы\n\nПревратите заметки в **понятный документ** — с заголовками, списками и формулами.\n\n## Начните с главного\n\n- Запишите ключевую мысль\n- Добавьте детали и примеры\n- Выберите формат для скачивания\n\n> Ясный текст начинается с ясной мысли.\n\n## Немного математики\n\nФормула энергии: $E = mc^2$.\n\n| Формат | Для чего |\n| --- | --- |\n| PDF | Чтение и печать |\n| Word | Редактирование |';
    this.onContentChange();
  }

  clearContent(): void {
    if (this.busy || !this.confirmClear) return;
    this.markdownContent = '';
    this.onContentChange();
  }

  insertFormat(format: typeof this.formats[number]): void {
    const editor = this.sourceEditor?.nativeElement;
    if (!editor || this.busy) return;
    const start = editor.selectionStart, end = editor.selectionEnd;
    const selected = this.markdownContent.slice(start, end) || format.sample;
    const prefix = format.block && start > 0 && this.markdownContent[start - 1] !== '\n' ? '\n\n' : '';
    const suffix = format.block && end < this.markdownContent.length && this.markdownContent[end] !== '\n' ? '\n\n' : '';
    const content = format.block && ['## ', '- ', '> '].includes(format.before)
      ? selected.split('\n').map(line => format.before + line).join('\n')
      : format.before + selected + format.after;
    this.markdownContent = this.markdownContent.slice(0, start) + prefix + content + suffix + this.markdownContent.slice(end);
    this.onContentChange();
    editor.value = this.markdownContent;
    editor.focus();
    const selectionStart = start + prefix.length + format.before.length;
    editor.setSelectionRange(selectionStart, selectionStart + selected.length);
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
          this.markdownContent = task.result;
          this.taskTitle = task.title || 'Текст из задачи';
          this.filename = task.title || 'Документ';
          this.onContentChange();
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
    try { await this.copyText(this.renderedDocument?.nativeElement.innerHTML || ''); }
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
    return '<!doctype html>\n<html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>' + escape(this.safeFilename) + '</title><link rel="stylesheet" href="' + escape(mathStyles) + '"><style>body{max-width:850px;margin:40px auto;padding:0 24px;font:16px/1.8 system-ui,sans-serif;color:#293323;overflow-wrap:anywhere}h1,h2,h3{line-height:1.3}img{max-width:100%;height:auto}pre,table,.katex-display{overflow-x:auto}pre,blockquote{padding:18px;background:#f1f4e9}blockquote{margin-inline:0;border-left:3px solid #8ca656}table{display:block;border-collapse:collapse}td,th{border:1px solid #d4dcc8;padding:8px 14px}a{color:#526b2c}</style></head><body>' + (this.renderedDocument?.nativeElement.innerHTML || '') + '</body></html>';
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
