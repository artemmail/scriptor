import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { LMarkdownEditorModule } from 'ngx-markdown-editor';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs/operators';
import { BlogService } from '../services/blog.service';
import { MarkdownRendererService1 } from '../task-result/markdown-renderer.service';

@Component({
  selector: 'app-blog-topic-create',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    LMarkdownEditorModule
  ],
  templateUrl: './blog-topic-create.component.html',
  styleUrls: ['./blog-topic-create.component.css']
})
export class BlogTopicCreateComponent implements OnInit {
  private readonly titleMaxLength = 256;

  readonly topicForm: FormGroup;

  readonly editorOptions = {
    placeholder: 'Опишите тему с использованием Markdown и LaTeX',
    katex: true,
    theme: 'github',
    lineNumbers: true,
    dragDrop: true,
    showPreviewPanel: true,
    hideIcons: [] as string[]
  };

  readonly preRender = (content: string): string => this.markdownRenderer.renderMath(content ?? '');

  submitting = false;
  submitError = '';
  loadingTopic = false;
  loadError = '';
  private mode: 'create' | 'edit' = 'create';
  private topicId: number | null = null;
  private topicSlug: string | null = null;

  constructor(
    private readonly fb: FormBuilder,
    private readonly blogService: BlogService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly snackBar: MatSnackBar,
    private readonly markdownRenderer: MarkdownRendererService1,
    private readonly destroyRef: DestroyRef
  ) {
    this.topicForm = this.fb.group({
      title: ['', [Validators.required, Validators.maxLength(this.titleMaxLength)]],
      text: ['', [Validators.required]]
    });
  }

  ngOnInit(): void {
    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(params => {
        const slug = params.get('slug');
        if (slug) {
          this.mode = 'edit';
          this.topicSlug = slug;
          this.fetchTopic(slug);
        } else {
          this.mode = 'create';
          this.topicId = null;
          this.topicSlug = null;
          this.loadError = '';
          this.loadingTopic = false;
          this.topicForm.enable();
          this.topicForm.reset({ title: '', text: '' });
        }
      });
  }

  get titleLength(): number {
    return this.topicForm.value.title?.length ?? 0;
  }

  get textLength(): number {
    return this.topicForm.value.text?.length ?? 0;
  }

  onCancel(): void {
    if (this.isEditMode && this.topicSlug) {
      this.router.navigate(['/blog', this.topicSlug]);
      return;
    }

    this.router.navigate(['/blog']);
  }

  onSubmit(): void {
    if (this.submitting) {
      return;
    }

    this.submitError = '';

    if (this.topicForm.invalid) {
      this.topicForm.markAllAsTouched();
      return;
    }

    const rawTitle = (this.topicForm.value.title ?? '').trim();
    const rawText = (this.topicForm.value.text ?? '').trim();

    if (!rawTitle || !rawText) {
      this.submitError = 'Заполните заголовок и текст темы.';
      return;
    }

    if (this.isEditMode && this.topicId === null) {
      this.submitError = 'Тема для редактирования не загружена.';
      return;
    }

    this.submitting = true;

    const request$ = this.isEditMode && this.topicId !== null
      ? this.blogService.updateTopic(this.topicId, { title: rawTitle, text: rawText })
      : this.blogService.createTopic({ title: rawTitle, text: rawText });

    request$
      .pipe(finalize(() => (this.submitting = false)))
      .subscribe({
        next: topic => {
          const message = this.isEditMode ? 'Тема обновлена' : 'Тема опубликована';
          this.snackBar.open(message, undefined, { duration: 3000 });
          this.topicSlug = topic.slug;
          const targetSlug = topic.slug ?? this.topicSlug;
          this.router.navigate(['/blog', targetSlug]);
        },
        error: () => {
          this.submitError = this.isEditMode
            ? 'Не удалось сохранить изменения. Попробуйте позже.'
            : 'Не удалось создать тему. Попробуйте позже.';
        }
      });
  }

  retryLoad(): void {
    if (this.topicSlug) {
      this.fetchTopic(this.topicSlug);
    }
  }

  get isEditMode(): boolean {
    return this.mode === 'edit';
  }

  private fetchTopic(slug: string): void {
    this.loadingTopic = true;
    this.loadError = '';
    this.topicForm.disable();

    this.blogService
      .getTopicBySlug(slug)
      .pipe(
        finalize(() => {
          this.loadingTopic = false;
          if (!this.loadError) {
            this.topicForm.enable();
          }
        })
      )
      .subscribe({
        next: topic => {
          this.topicId = topic.id;
          this.topicSlug = topic.slug;
          this.topicForm.setValue({ title: topic.header, text: topic.text });
        },
        error: () => {
          this.loadError = 'Не удалось загрузить тему для редактирования.';
          this.topicForm.reset({ title: '', text: '' });
        }
      });
  }
}
