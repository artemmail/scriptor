import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { LMarkdownEditorModule } from 'ngx-markdown-editor';
import { finalize } from 'rxjs/operators';
import { BlogService } from '../services/blog.service';
import { MarkdownRendererService1 } from '../task-result/markdown-renderer.service';

@Component({
  selector: 'app-blog-topic-create',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatSnackBarModule,
    LMarkdownEditorModule
  ],
  templateUrl: './blog-topic-create.component.html',
  styleUrls: ['./blog-topic-create.component.css']
})
export class BlogTopicCreateComponent {
  private readonly titleMaxLength = 256;
  private readonly textMaxLength = 10000;

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

  constructor(
    private readonly fb: FormBuilder,
    private readonly blogService: BlogService,
    private readonly router: Router,
    private readonly snackBar: MatSnackBar,
    private readonly markdownRenderer: MarkdownRendererService1
  ) {
    this.topicForm = this.fb.group({
      title: ['', [Validators.required, Validators.maxLength(this.titleMaxLength)]],
      text: ['', [Validators.required, Validators.maxLength(this.textMaxLength)]]
    });
  }

  get titleLength(): number {
    return this.topicForm.value.title?.length ?? 0;
  }

  get textLength(): number {
    return this.topicForm.value.text?.length ?? 0;
  }

  onCancel(): void {
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

    this.submitting = true;

    this.blogService
      .createTopic({ title: rawTitle, text: rawText })
      .pipe(finalize(() => (this.submitting = false)))
      .subscribe({
        next: () => {
          this.snackBar.open('Тема опубликована', undefined, { duration: 3000 });
          this.router.navigate(['/blog']);
        },
        error: () => {
          this.submitError = 'Не удалось создать тему. Попробуйте позже.';
        }
      });
  }
}
