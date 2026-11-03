import { AfterViewInit, Component, ElementRef, EventEmitter, forwardRef, Input, OnDestroy, Output, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MarkdownComponent } from 'ngx-markdown';
import type ToastEditor from '@toast-ui/editor';

@Component({
  selector: 'app-markdown-editor',
  standalone: true,
  imports: [CommonModule, MatIconModule, MarkdownComponent],
  templateUrl: './markdown-editor.component.html',
  styleUrls: ['./markdown-editor.component.css'],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => MarkdownEditorComponent), multi: true }]
})
export class MarkdownEditorComponent implements AfterViewInit, OnDestroy, ControlValueAccessor {
  @ViewChild('toastHost') private toastHost?: ElementRef<HTMLElement>;
  @ViewChild('renderedDocument', { read: ElementRef }) private renderedDocument?: ElementRef<HTMLElement>;
  @Input() label = 'Текст документа';
  @Output() previewReadyChange = new EventEmitter<boolean>();
  markdownContent = '';
  viewMode: 'edit' | 'split' | 'preview' = typeof window !== 'undefined' && window.innerWidth <= 600 ? 'edit' : 'split';
  editorMode: 'wysiwyg' | 'markdown' = 'wysiwyg';
  editorReady = false;
  editorError = '';
  disabled = false;
  private editor?: ToastEditor;
  private destroyed = false;
  private writingValue = false;
  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  readonly katexOptions = {
    throwOnError: false, trust: false,
    delimiters: [
      { left: '$$', right: '$$', display: true }, { left: '$', right: '$', display: false },
      { left: '\\(', right: '\\)', display: false }, { left: '\\[', right: '\\]', display: true }
    ]
  };

  async ngAfterViewInit(): Promise<void> {
    if (typeof window === 'undefined' || !this.toastHost) return;

    try {
      const [{ default: Editor }] = await Promise.all([
        import('@toast-ui/editor'),
        import('@toast-ui/editor/dist/i18n/ru-ru')
      ]);
      if (this.destroyed) return;
      this.editorMode = this.hasMath ? 'markdown' : 'wysiwyg';
      this.editor = new Editor({
        el: this.toastHost.nativeElement,
        height: '600px',
        minHeight: '420px',
        initialEditType: this.editorMode,
        initialValue: this.markdownContent,
        previewStyle: 'tab',
        hideModeSwitch: true,
        language: 'ru-RU',
        usageStatistics: false,
        placeholder: 'Начните писать здесь или вставьте готовый Markdown…',
        toolbarItems: [
          ['heading', 'bold', 'italic', 'strike'],
          ['hr', 'quote'],
          ['ul', 'ol', 'task'],
          ['table', 'link', 'image'],
          ['code', 'codeblock']
        ],
        events: {
          blur: () => this.onTouched(),
          change: () => {
            if (!this.editorReady || this.writingValue || this.disabled) return;
            this.markdownContent = this.editor?.getMarkdown() ?? '';
            this.previewReadyChange.emit(false);
            this.onChange(this.markdownContent);
          }
        }
      });
      this.editorReady = true;
    } catch {
      this.editorError = 'Не удалось загрузить редактор. Обновите страницу и попробуйте ещё раз.';
    }
  }

  ngOnDestroy(): void { this.destroyed = true; this.editor?.destroy(); }

  get hasMath(): boolean { return this.containsMath(this.markdownContent); }

  private containsMath(content: string): boolean {
    return /\$\$|(^|[^\\])\$(?!\s)[^$\n]+\$|\\\(|\\\[/m.test(content);
  }

  setEditorMode(mode: 'wysiwyg' | 'markdown'): void {
    if (mode === 'wysiwyg' && this.hasMath) {
      this.editorError = 'Формулы сохраняются в режиме Markdown. Уберите формулы, чтобы перейти к визуальному редактированию.';
      return;
    }
    this.editorError = '';
    this.editorMode = mode;
    this.editor?.changeMode(mode);
  }

  insertFormula(): void {
    if (this.disabled || !this.editorReady) return;
    this.setEditorMode('markdown');
    this.editor?.insertText('\n\n$$\nE = mc^2\n$$\n\n');
    this.editor?.focus();
  }

  get hasContent(): boolean { return !!this.markdownContent.trim(); }
  get renderedHtml(): string { return this.renderedDocument?.nativeElement.innerHTML || ''; }

  writeValue(value: string | null): void {
    this.markdownContent = value ?? '';
    if (!this.editor) return;
    this.writingValue = true;
    try {
      // Switch before setting Markdown so the visual editor cannot rewrite LaTeX.
      if (this.hasMath) this.setEditorMode('markdown');
      if (this.editor.getMarkdown() !== this.markdownContent) this.editor.setMarkdown(this.markdownContent, false);
    } finally {
      this.writingValue = false;
    }
  }

  registerOnChange(fn: (value: string) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  setDisabledState(disabled: boolean): void { this.disabled = disabled; }

  showPreview(): void { this.viewMode = 'preview'; }
}
