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
import { MarkdownEditorComponent } from '../shared/markdown-editor/markdown-editor.component';
import { BlogService } from '../services/blog.service';
import { Title } from '@angular/platform-browser';

const nonBlank: ValidatorFn = control => String(control.value ?? '').trim() ? null : { required: true };

@Component({
  selector: 'app-blog-topic-create',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule, MatSnackBarModule, MatProgressSpinnerModule, MatIconModule, MarkdownEditorComponent],
  templateUrl: './blog-topic-create.component.html',
  styleUrls: ['../shared/account-page.css', './blog-topic-create.component.css']
})
export class BlogTopicCreateComponent implements OnInit {
  @ViewChild(MarkdownEditorComponent) private markdownEditor?: MarkdownEditorComponent;
  @ViewChild('editorTop') private editorTop?: ElementRef<HTMLElement>;

  readonly topicForm;
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

  showPreview(): void {
    this.markdownEditor?.showPreview();
    this.editorTop?.nativeElement.scrollIntoView({ block: 'start' });
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
