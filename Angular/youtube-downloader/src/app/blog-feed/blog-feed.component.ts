import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDividerModule } from '@angular/material/divider';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs/operators';
import { BlogService, BlogTopic, BlogComment } from '../services/blog.service';
import { InfiniteScrollModule } from 'ngx-infinite-scroll';
import { AuthService, UserInfo } from '../services/AuthService.service';
import { RouterModule } from '@angular/router';
import { MarkdownRendererService1 } from '../task-result/markdown-renderer.service';

interface BlogTopicViewModel extends BlogTopic {
  collapsed: boolean;
  textIsTooLong: boolean;
  newComment: string;
  submittingComment: boolean;
  commentError?: string;
  renderedText: string;
}

@Component({
  selector: 'app-blog-feed',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatDividerModule,
    MatProgressSpinnerModule,
    InfiniteScrollModule,
    RouterModule
  ],
  templateUrl: './blog-feed.component.html',
  styleUrls: ['./blog-feed.component.css']
})
export class BlogFeedComponent implements OnInit {
  private readonly collapseThreshold = 700;
  private readonly pageSize = 10;

  topics: BlogTopicViewModel[] = [];
  loading = false;
  allLoaded = false;
  skip = 0;
  initialLoad = false;
  feedError = '';

  currentUser: UserInfo | null = null;
  canCreateTopics = false;

  constructor(
    private readonly blogService: BlogService,
    private readonly authService: AuthService,
    private readonly destroyRef: DestroyRef,
    private readonly markdownRenderer: MarkdownRendererService1
  ) {
    this.authService.user$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(user => {
        this.currentUser = user;
        const roles = user?.roles ?? [];
        this.canCreateTopics = roles.some(r => r.toLowerCase() === 'moderator');
      });
  }

  ngOnInit(): void {
    this.loadTopics();
  }

  onLoadMore(): void {
    this.loadTopics();
  }

  onScrollDown(): void {
    this.loadTopics();
  }

  toggleCollapse(topic: BlogTopicViewModel): void {
    topic.collapsed = !topic.collapsed;
  }

  submitComment(topic: BlogTopicViewModel): void {
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

  trackByTopicId(_: number, topic: BlogTopicViewModel): number {
    return topic.id;
  }

  trackByCommentId(_: number, comment: BlogComment): number {
    return comment.id;
  }

  private loadTopics(): void {
    if (this.loading || this.allLoaded) {
      return;
    }

    this.loading = true;
    this.feedError = '';

    this.blogService
      .getTopics(this.skip, this.pageSize)
      .pipe(
        finalize(() => {
          this.loading = false;
          this.initialLoad = true;
        })
      )
      .subscribe({
        next: topics => {
          if (topics.length === 0) {
            this.allLoaded = true;
            return;
          }

          const mapped = topics.map(t => this.mapTopic(t));
          this.topics = [...this.topics, ...mapped];
          this.skip += topics.length;
          if (topics.length < this.pageSize) {
            this.allLoaded = true;
          }
        },
        error: () => {
          this.feedError = 'Не удалось загрузить ленту. Попробуйте позже.';
        }
      });
  }

  private mapTopic(topic: BlogTopic): BlogTopicViewModel {
    const isTooLong = topic.text.length > this.collapseThreshold;
    const rendered = this.markdownRenderer.renderMath(topic.text);
    return {
      ...topic,
      collapsed: isTooLong,
      textIsTooLong: isTooLong,
      newComment: '',
      submittingComment: false,
      renderedText: rendered
    };
  }
}
