import { CommonModule } from '@angular/common';
import { Component, DestroyRef, ElementRef, HostListener, OnInit, ViewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs/operators';
import { Subscription } from 'rxjs';
import { MarkdownComponent } from 'ngx-markdown';
import { BlogService } from '../services/blog.service';
import { Title } from '@angular/platform-browser';

const nonBlank: ValidatorFn = control => String(control.value ?? '').trim() ? null : { required: true };

@Component({
  selector: 'app-blog-topic-create',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule, MatSnackBarModule, MatProgressSpinnerModule, MatIconModule, MarkdownComponent],
  templateUrl: './blog-topic-create.component.html',
  styleUrls: ['../shared/account-page.css', './blog-topic-create.component.css']
})
export class BlogTopicCreateComponent implements OnInit {
  @ViewChild('textEditor') private textEditor?: ElementRef<HTMLTextAreaElement>;
  @ViewChild('editorTop') private editorTop?: ElementRef<HTMLElement>;

  readonly topicForm;
  readonly formats = [
    { label: 'Заголовок', icon: 'title', before: '## ', after: '', sample: 'Заголовок раздела', block: true },
    { label: 'Жирный', icon: 'format_bold', before: '**', after: '**', sample: 'Важная мысль', block: false },
    { label: 'Курсив', icon: 'format_italic', before: '*', after: '*', sample: 'Акцент', block: false },
    { label: 'Список', icon: 'format_list_bulleted', before: '- ', after: '', sample: 'Пункт списка', block: true },
    { label: 'Цитата', icon: 'format_quote', before: '> ', after: '', sample: 'Текст цитаты', block: true },
    { label: 'Ссылка', icon: 'link', before: '[', after: '](https://example.com)', sample: 'Текст ссылки', block: false },
    { label: 'Изображение по ссылке', icon: 'image', before: '![', after: '](https://example.com/image.jpg)', sample: 'Описание изображения', block: true },
    { label: 'Блок кода', icon: 'code', before: '```\n', after: '\n```', sample: 'Ваш код', block: true },
    { label: 'Формула LaTeX', icon: 'functions', before: '$$\n', after: '\n$$', sample: 'E = mc^2', block: true }
  ];

  editorMode: 'write' | 'preview' = 'write';
  readonly katexOptions = {
    throwOnError: false,
    trust: false,
    delimiters: [
      { left: '$$', right: '$$', display: true },
      { left: '$', right: '$', display: false },
      { left: '\\(', right: '\\)', display: false },
      { left: '\\[', right: '\\]', display: true }
    ]
  };
  submitting = false;
  submitError = '';
  loadingTopic = false;
  loadError = '';
  isEditMode = false;
  confirmLeave = false;
  private topicId: number | null = null;
  private topicSlug: string | null = null;
  private loadSubscription?: Subscription;
  private original = { title: '', text: '' };

  constructor(
    fb: FormBuilder,
    private readonly blogService: BlogService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly snackBar: MatSnackBar,
    private readonly destroyRef: DestroyRef,
    private readonly titleService: Title
  ) {
    this.topicForm = fb.nonNullable.group({
      title: ['', [nonBlank, Validators.maxLength(256)]],
      text: ['', [nonBlank]]
    });
  }

  ngOnInit(): void {
    this.topicForm.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.submitError = '';
      this.confirmLeave = false;
    });
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      this.loadSubscription?.unsubscribe();
      this.topicSlug = params.get('slug');
      this.isEditMode = !!this.topicSlug;
      this.topicId = null;
      this.loadError = '';
      this.submitError = '';
      this.confirmLeave = false;
      this.editorMode = 'write';
      this.original = { title: '', text: '' };
      this.topicForm.reset(this.original);
      this.updatePageTitle();
      if (this.topicSlug) this.fetchTopic(this.topicSlug);
      else this.topicForm.enable();
    });
  }

  get titleText(): string { return this.topicForm.controls.title.value; }
  get text(): string { return this.topicForm.controls.text.value; }
  get wordCount(): number { return this.text.trim() ? this.text.trim().split(/\s+/).length : 0; }
  get readingMinutes(): number { return Math.max(1, Math.ceil(this.wordCount / 200)); }
  get hasChanges(): boolean { return this.titleText !== this.original.title || this.text !== this.original.text; }
  get canSubmit(): boolean {
    return !this.submitting && !this.loadingTopic && !this.loadError && this.topicForm.valid && (!this.isEditMode || this.hasChanges);
  }

  fieldInvalid(name: 'title' | 'text'): boolean {
    const field = this.topicForm.controls[name];
    return field.invalid && (field.touched || field.dirty);
  }

  setEditorMode(mode: 'write' | 'preview', scroll = false): void {
    this.editorMode = mode;
    if (scroll) this.editorTop?.nativeElement.scrollIntoView({ block: 'start' });
  }

  insertFormat(format: typeof this.formats[number]): void {
    const textarea = this.textEditor?.nativeElement;
    if (!textarea || this.submitting) return;
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    const selected = this.text.slice(start, end) || format.sample;
    const prefix = format.block && start > 0 && this.text[start - 1] !== '\n' ? '\n\n' : '';
    const suffix = format.block && end < this.text.length && this.text[end] !== '\n' ? '\n\n' : '';
    const content = format.block && ['- ', '> ', '## '].includes(format.before)
      ? selected.split('\n').map(line => format.before + line).join('\n')
      : format.before + selected + format.after;
    this.topicForm.controls.text.setValue(this.text.slice(0, start) + prefix + content + suffix + this.text.slice(end));
    this.topicForm.controls.text.markAsDirty();
    // Update before restoring the selection so the native textarea keeps the intended range.
    textarea.value = this.text;
    textarea.focus();
    const selectionStart = start + prefix.length + format.before.length;
    textarea.setSelectionRange(selectionStart, selectionStart + selected.length);
  }

  onCancel(): void {
    if (this.submitting) return;
    if (this.hasChanges) this.confirmLeave = true;
    else this.leaveEditor();
  }

  leaveEditor(): void {
    if (this.submitting) return;
    this.router.navigate(this.isEditMode && this.topicSlug ? ['/blog', this.topicSlug] : ['/blog']);
  }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasChanges) { event.preventDefault(); event.returnValue = ''; }
  }

  onSubmit(): void {
    if (this.submitting || this.loadingTopic || this.loadError) return;
    this.topicForm.markAllAsTouched();
    if (!this.canSubmit) return;
    if (this.isEditMode && this.topicId === null) return;
    this.submitError = '';
    this.confirmLeave = false;
    const payload = { title: this.titleText.trim(), text: this.text.trim() };
    this.submitting = true;
    this.topicForm.disable({ emitEvent: false });
    const request$ = this.isEditMode
      ? this.blogService.updateTopic(this.topicId!, payload)
      : this.blogService.createTopic(payload);
    request$.pipe(takeUntilDestroyed(this.destroyRef), finalize(() => {
      this.submitting = false;
      this.topicForm.enable({ emitEvent: false });
    })).subscribe({
      next: topic => {
        this.original = this.topicForm.getRawValue();
        this.topicForm.markAsPristine();
        this.snackBar.open(this.isEditMode ? 'Публикация обновлена' : 'Публикация создана', undefined, { duration: 3000 });
        this.router.navigate(['/blog', topic.slug]);
      },
      error: () => this.submitError = 'Не удалось сохранить публикацию. Ваш текст остался в редакторе. Попробуйте ещё раз.'
    });
  }

  retryLoad(): void { if (this.topicSlug && !this.loadingTopic) this.fetchTopic(this.topicSlug); }

  private fetchTopic(slug: string): void {
    this.loadingTopic = true;
    this.loadError = '';
    this.topicForm.disable({ emitEvent: false });
    this.loadSubscription = this.blogService.getTopicBySlug(slug)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => {
        this.loadingTopic = false;
        if (!this.loadError) this.topicForm.enable({ emitEvent: false });
      })).subscribe({
        next: topic => {
          this.topicId = topic.id;
          this.topicSlug = topic.slug;
          this.original = { title: topic.header, text: topic.text };
          this.topicForm.reset(this.original, { emitEvent: false });
          this.updatePageTitle(topic.header);
        },
        error: () => this.loadError = 'Не удалось загрузить публикацию для редактирования. Попробуйте ещё раз.'
      });
  }

  private updatePageTitle(title?: string): void {
    this.titleService.setTitle(this.isEditMode
      ? `${title ? 'Редактирование: ' + title : 'Редактирование публикации'} — YouScriptor`
      : 'Новая публикация — блог YouScriptor');
  }
}
