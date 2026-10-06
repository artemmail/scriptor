import { CommonModule } from '@angular/common';
import { afterNextRender, Component, DestroyRef, ElementRef, Injector, OnInit, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs/operators';
import { Subscription } from 'rxjs';
import { MarkdownComponent } from 'ngx-markdown';
import { MatIconModule } from '@angular/material/icon';
import { LocalTimePipe } from '../pipe/local-time.pipe';
import { blogPlainText } from '../blog-feed/blog-feed.component';

import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { Title } from '@angular/platform-browser';

import { BlogService, BlogTopic, BlogComment } from '../services/blog.service';
import { AuthService, UserInfo } from '../services/AuthService.service';

interface BlogCommentViewModel extends BlogComment {
  editing: boolean;
  editText: string;
  submittingEdit: boolean;
  deleting: boolean;
  actionError: string;
  confirmDelete: boolean;
}

interface BlogTopicDetailViewModel extends BlogTopic {
  readingMinutes: number;
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
    MatProgressSpinnerModule, MarkdownComponent, MatIconModule, LocalTimePipe
  ],
  templateUrl: './blog-topic-detail.component.html',
  styleUrls: ['../shared/account-page.css', './blog-topic-detail.component.css']
})
export class BlogTopicDetailComponent implements OnInit {
  @ViewChild('articleBody', { read: ElementRef }) private articleBody?: ElementRef<HTMLElement>;
  @ViewChild('commentsSection') private commentsSection?: ElementRef<HTMLElement>;
  contents: { id: string; title: string; sub: boolean }[] = [];
  confirmTopicDelete = false;
  announcement = '';
  shareMessage = '';
  shareFallback = '';
  private slug = '';
  private loadSubscription?: Subscription;
  readonly katexOptions = {
    throwOnError: false, trust: false,
    delimiters: [
      { left: '$$', right: '$$', display: true },
      { left: '$', right: '$', display: false },
      { left: '\\(', right: '\\)', display: false },
      { left: '\\[', right: '\\]', display: true }
    ]
  };
  topic: BlogTopicDetailViewModel | null = null;
  loading = false;
  loadError = '';
  currentUser: UserInfo | null = null;
  isModerator = false;

  constructor(
    private readonly blogService: BlogService,
    private readonly route: ActivatedRoute,
    private readonly authService: AuthService,
    private readonly destroyRef: DestroyRef,
    private readonly router: Router,
    private readonly injector: Injector,
    private readonly titleService: Title
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
        this.loadSubscription?.unsubscribe();
        const slug = params.get('slug');
        if (!slug) {
          this.topic = null;
          this.loadError = 'Публикация не найдена.';
          this.updatePageTitle(null, true);
          return;
        }

        this.slug = slug;
        this.fetchTopic(slug);
      });
  }

  get busy(): boolean {
    return !!this.topic && (this.topic.deletingTopic || this.topic.submittingComment || this.topic.comments.some(c => c.submittingEdit || c.deleting));
  }

  get returnUrl(): string { return '/blog/' + this.slug + '#comments'; }
  initial(name: string): string { return Array.from(name?.trim() || 'А')[0].toLocaleUpperCase(); }
  retryLoad(): void { if (this.slug && !this.loading) this.fetchTopic(this.slug); }

  buildContents(): void {
    this.contents = Array.from(this.articleBody?.nativeElement.querySelectorAll('h2, h3') || []).map((heading, index) => {
      heading.id = 'bd-section-' + index;
      return { id: heading.id, title: heading.textContent || '', sub: heading.tagName === 'H3' };
    });
    if (this.route.snapshot.fragment === 'comments') {
      // The new contents list changes the mobile layout above the article.
      afterNextRender(() => this.scrollToComments(), { injector: this.injector });
    }
  }

  scrollToSection(id: string): void {
    this.articleBody?.nativeElement.querySelector<HTMLElement>('#' + id)?.scrollIntoView({ block: 'start' });
  }

  scrollToComments(): void { this.commentsSection?.nativeElement.scrollIntoView({ block: 'start' }); }

  async copyLink(): Promise<void> {
    this.shareMessage = '';
    this.shareFallback = '';
    const url = new URL(this.router.url.split('#')[0], window.location.origin).href;
    try { await navigator.clipboard.writeText(url); this.shareMessage = 'Ссылка скопирована'; }
    catch { this.shareFallback = url; this.shareMessage = 'Скопируйте ссылку из поля ниже'; }
  }

  requestTopicDelete(): void {
    if (!this.isModerator || this.busy) return;
    this.confirmTopicDelete = true;
    if (this.topic) this.topic.topicActionError = '';
  }

  requestCommentDelete(comment: BlogCommentViewModel): void {
    if (!this.canDeleteComment(comment) || this.busy || comment.editing) return;
    comment.confirmDelete = true;
    comment.actionError = '';
  }

  submitComment(topic: BlogTopicDetailViewModel): void {
    if (!this.currentUser || this.busy) {
      return;
    }

    topic.commentError = '';
    const text = (topic.newComment ?? '').trim();
    if (!text || text.length > 2000) {
      topic.commentError = 'Введите комментарий длиной от 1 до 2000 символов.';
      return;
    }

    topic.submittingComment = true;
    this.blogService
      .addComment(topic.id, { text })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          topic.submittingComment = false;
        })
      )
      .subscribe({
        next: comment => {
          topic.comments = [...topic.comments, this.mapComment(comment)];
          topic.commentCount = topic.comments.length;
          topic.newComment = '';
          this.announcement = 'Комментарий опубликован.';
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
    if (!this.canEditComment(comment) || this.busy || comment.editing) {
      return;
    }

    comment.editing = true;
    comment.editText = comment.text;
    comment.actionError = '';
    comment.confirmDelete = false;
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
    if (!this.canEditComment(comment) || this.busy || !comment.editing) {
      return;
    }

    const text = (comment.editText ?? '').trim();
    if (!text || text.length > 2000) {
      comment.actionError = 'Введите комментарий длиной от 1 до 2000 символов.';
      return;
    }

    comment.submittingEdit = true;
    comment.actionError = '';

    this.blogService
      .updateComment(topic.id, comment.id, { text })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          comment.submittingEdit = false;
        })
      )
      .subscribe({
        next: updated => {
          comment.text = updated.text;
          comment.editText = updated.text;
          comment.editing = false;
          this.announcement = 'Комментарий обновлён.';
        },
        error: () => {
          comment.actionError = 'Не удалось сохранить изменения. Попробуйте позже.';
        }
      });
  }

  deleteComment(topic: BlogTopicDetailViewModel, comment: BlogCommentViewModel): void {
    if (!this.canDeleteComment(comment) || this.busy || !comment.confirmDelete) {
      return;
    }

    comment.deleting = true;
    comment.actionError = '';

    this.blogService
      .deleteComment(topic.id, comment.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          comment.deleting = false;
        })
      )
      .subscribe({
        next: () => {
          topic.comments = topic.comments.filter(c => c.id !== comment.id);
          topic.commentCount = topic.comments.length;
          this.announcement = 'Комментарий удалён.';
        },
        error: () => {
          comment.actionError = 'Не удалось удалить комментарий. Попробуйте позже.';
        }
      });
  }

  editTopic(topic: BlogTopicDetailViewModel): void {
    if (!this.isModerator || this.busy) {
      return;
    }

    this.router.navigate(['/blog', topic.slug, 'edit']);
  }

  deleteTopic(topic: BlogTopicDetailViewModel): void {
    if (!this.isModerator || this.busy || !this.confirmTopicDelete) {
      return;
    }

    topic.deletingTopic = true;
    topic.topicActionError = '';

    this.blogService
      .deleteTopic(topic.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
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
    this.contents = [];
    this.confirmTopicDelete = false;
    this.announcement = '';
    this.shareMessage = '';
    this.shareFallback = '';
    this.updatePageTitle();

    this.loadSubscription = this.blogService
      .getTopicBySlug(slug)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.loading = false;
        })
      )
      .subscribe({
        next: topic => {
          this.topic = this.mapTopic(topic);
          this.updatePageTitle(this.topic);
        },
        error: error => {
          this.loadError = error.status === 404 ? 'Публикация не найдена. Возможно, её удалили или изменили ссылку.' : 'Не удалось загрузить публикацию. Попробуйте ещё раз.';
          this.updatePageTitle(null, true);
        }
      });
  }

  private mapTopic(topic: BlogTopic): BlogTopicDetailViewModel {
    return {
      ...topic,
      readingMinutes: Math.max(1, Math.ceil(blogPlainText(topic.text || '').split(/\s+/).length / 200)),
      newComment: '',
      submittingComment: false,
      comments: (topic.comments || []).map(comment => this.mapComment(comment)),
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
      actionError: '',
      confirmDelete: false
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

  private updatePageTitle(topic?: BlogTopicDetailViewModel | null, hasError = false): void {
    if (topic) {
      const header = (topic.header ?? '').trim();
      if (header) {
        this.titleService.setTitle(`${header} — блог YouScriptor`);
        return;
      }
    }

    if (hasError) {
      this.titleService.setTitle('Публикация недоступна — блог YouScriptor');
      return;
    }

    this.titleService.setTitle('Блог YouScriptor — публикация');
  }
}
