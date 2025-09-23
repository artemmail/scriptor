import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs/operators';

import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { BlogService, BlogTopic, BlogComment } from '../services/blog.service';
import { MarkdownRendererService1 } from '../task-result/markdown-renderer.service';
import { AuthService, UserInfo } from '../services/AuthService.service';

interface BlogTopicDetailViewModel extends BlogTopic {
  renderedText: string;
  newComment: string;
  submittingComment: boolean;
  commentError?: string;
}

@Component({
  selector: 'app-blog-topic-detail',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterModule,
    MatCardModule,
    MatButtonModule,
    MatDividerModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './blog-topic-detail.component.html',
  styleUrls: ['./blog-topic-detail.component.css']
})
export class BlogTopicDetailComponent implements OnInit {
  topic: BlogTopicDetailViewModel | null = null;
  loading = false;
  loadError = '';
  currentUser: UserInfo | null = null;

  constructor(
    private readonly blogService: BlogService,
    private readonly route: ActivatedRoute,
    private readonly markdownRenderer: MarkdownRendererService1,
    private readonly authService: AuthService,
    private readonly destroyRef: DestroyRef
  ) {
    this.authService.user$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(user => {
        this.currentUser = user;
      });
  }

  ngOnInit(): void {
    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(params => {
        const slug = params.get('slug');
        if (!slug) {
          this.topic = null;
          this.loadError = 'Публикация не найдена.';
          return;
        }

        this.fetchTopic(slug);
      });
  }

  submitComment(topic: BlogTopicDetailViewModel): void {
    if (!this.currentUser || topic.submittingComment) {
      return;
    }

    topic.commentError = '';
    const text = (topic.newComment ?? '').trim();
    if (!text) {
      topic.commentError = 'Введите текст комментария.';
      return;
    }

    topic.submittingComment = true;
    this.blogService
      .addComment(topic.id, { text })
      .pipe(
        finalize(() => {
          topic.submittingComment = false;
        })
      )
      .subscribe({
        next: comment => {
          topic.comments = [...topic.comments, comment];
          topic.commentCount = topic.comments.length;
          topic.newComment = '';
        },
        error: () => {
          topic.commentError = 'Не удалось отправить комментарий. Попробуйте позже.';
        }
      });
  }

  trackByCommentId(_: number, comment: BlogComment): number {
    return comment.id;
  }

  private fetchTopic(slug: string): void {
    this.loading = true;
    this.loadError = '';
    this.topic = null;

    this.blogService
      .getTopicBySlug(slug)
      .pipe(
        finalize(() => {
          this.loading = false;
        })
      )
      .subscribe({
        next: topic => {
          this.topic = this.mapTopic(topic);
        },
        error: () => {
          this.loadError = 'Не удалось загрузить публикацию. Попробуйте позже.';
        }
      });
  }

  private mapTopic(topic: BlogTopic): BlogTopicDetailViewModel {
    return {
      ...topic,
      renderedText: this.markdownRenderer.renderMath(topic.text),
      newComment: '',
      submittingComment: false
    };
  }
}
