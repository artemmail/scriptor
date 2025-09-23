import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
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

interface BlogCommentViewModel extends BlogComment {
  editing: boolean;
  editText: string;
  submittingEdit: boolean;
  deleting: boolean;
  actionError: string;
}

interface BlogTopicDetailViewModel extends BlogTopic {
  renderedText: string;
  newComment: string;
  submittingComment: boolean;
  commentError?: string;
  comments: BlogCommentViewModel[];
  deletingTopic: boolean;
  topicActionError: string;
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
  isModerator = false;

  constructor(
    private readonly blogService: BlogService,
    private readonly route: ActivatedRoute,
    private readonly markdownRenderer: MarkdownRendererService1,
    private readonly authService: AuthService,
    private readonly destroyRef: DestroyRef,
    private readonly router: Router
  ) {
    this.authService.user$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(user => {
        this.currentUser = user;
        const roles = user?.roles ?? [];
        this.isModerator = roles.some(r => r.toLowerCase() === 'moderator');
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
          topic.comments = [...topic.comments, this.mapComment(comment)];
          topic.commentCount = topic.comments.length;
          topic.newComment = '';
        },
        error: () => {
          topic.commentError = 'Не удалось отправить комментарий. Попробуйте позже.';
        }
      });
  }

  trackByCommentId(_: number, comment: BlogCommentViewModel): number {
    return comment.id;
  }

  canEditComment(comment: BlogCommentViewModel): boolean {
    return this.isModerator || this.isOwnComment(comment);
  }

  canDeleteComment(comment: BlogCommentViewModel): boolean {
    return this.isModerator || this.isOwnComment(comment);
  }

  startEditComment(comment: BlogCommentViewModel): void {
    if (!this.canEditComment(comment) || comment.submittingEdit || comment.editing) {
      return;
    }

    comment.editing = true;
    comment.editText = comment.text;
    comment.actionError = '';
  }

  cancelEditComment(comment: BlogCommentViewModel): void {
    if (comment.submittingEdit) {
      return;
    }

    comment.editing = false;
    comment.editText = comment.text;
    comment.actionError = '';
  }

  saveComment(topic: BlogTopicDetailViewModel, comment: BlogCommentViewModel): void {
    if (!this.canEditComment(comment) || comment.submittingEdit) {
      return;
    }

    const text = (comment.editText ?? '').trim();
    if (!text) {
      comment.actionError = 'Введите текст комментария.';
      return;
    }

    comment.submittingEdit = true;
    comment.actionError = '';

    this.blogService
      .updateComment(topic.id, comment.id, { text })
      .pipe(
        finalize(() => {
          comment.submittingEdit = false;
        })
      )
      .subscribe({
        next: updated => {
          comment.text = updated.text;
          comment.editText = updated.text;
          comment.editing = false;
        },
        error: () => {
          comment.actionError = 'Не удалось сохранить изменения. Попробуйте позже.';
        }
      });
  }

  deleteComment(topic: BlogTopicDetailViewModel, comment: BlogCommentViewModel): void {
    if (!this.canDeleteComment(comment) || comment.deleting) {
      return;
    }

    const confirmed = confirm('Удалить комментарий?');
    if (!confirmed) {
      return;
    }

    comment.deleting = true;
    comment.actionError = '';

    this.blogService
      .deleteComment(topic.id, comment.id)
      .pipe(
        finalize(() => {
          comment.deleting = false;
        })
      )
      .subscribe({
        next: () => {
          topic.comments = topic.comments.filter(c => c.id !== comment.id);
          topic.commentCount = topic.comments.length;
        },
        error: () => {
          comment.actionError = 'Не удалось удалить комментарий. Попробуйте позже.';
        }
      });
  }

  editTopic(topic: BlogTopicDetailViewModel): void {
    if (!this.isModerator) {
      return;
    }

    this.router.navigate(['/blog', topic.slug, 'edit']);
  }

  deleteTopic(topic: BlogTopicDetailViewModel): void {
    if (!this.isModerator || topic.deletingTopic) {
      return;
    }

    const confirmed = confirm('Удалить тему целиком?');
    if (!confirmed) {
      return;
    }

    topic.deletingTopic = true;
    topic.topicActionError = '';

    this.blogService
      .deleteTopic(topic.id)
      .pipe(
        finalize(() => {
          topic.deletingTopic = false;
        })
      )
      .subscribe({
        next: () => {
          this.router.navigate(['/blog']);
        },
        error: () => {
          topic.topicActionError = 'Не удалось удалить тему. Попробуйте позже.';
        }
      });
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
      submittingComment: false,
      comments: topic.comments.map(comment => this.mapComment(comment)),
      deletingTopic: false,
      topicActionError: ''
    };
  }

  private mapComment(comment: BlogComment): BlogCommentViewModel {
    return {
      ...comment,
      editing: false,
      editText: comment.text,
      submittingEdit: false,
      deleting: false,
      actionError: ''
    };
  }

  private isOwnComment(comment: BlogComment): boolean {
    if (!this.currentUser) {
      return false;
    }

    if (comment.userId && this.currentUser.id) {
      return comment.userId === this.currentUser.id;
    }

    const normalize = (value: string | undefined | null) =>
      (value ?? '').trim().toLowerCase();

    const commentUser = normalize(comment.user);
    return (
      commentUser !== '' &&
      (commentUser === normalize(this.currentUser.email) ||
        commentUser === normalize(this.currentUser.displayName) ||
        commentUser === normalize(this.currentUser.name))
    );
  }
}
