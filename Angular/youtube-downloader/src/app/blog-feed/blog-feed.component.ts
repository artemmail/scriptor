import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs/operators';
import { BlogService, BlogTopic } from '../services/blog.service';
import { InfiniteScrollModule } from 'ngx-infinite-scroll';
import { AuthService } from '../services/AuthService.service';
import { Router, RouterModule } from '@angular/router';
import { MarkdownRendererService1 } from '../task-result/markdown-renderer.service';

interface BlogTopicViewModel extends BlogTopic {
  collapsed: boolean;
  textIsTooLong: boolean;
  renderedText: string;
  deleting?: boolean;
  actionError?: string;
}

@Component({
  selector: 'app-blog-feed',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
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

  canCreateTopics = false;
  isModerator = false;

  constructor(
    private readonly blogService: BlogService,
    private readonly authService: AuthService,
    private readonly destroyRef: DestroyRef,
    private readonly router: Router,
    private readonly markdownRenderer: MarkdownRendererService1
  ) {
    this.authService.user$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(user => {
        const roles = user?.roles ?? [];
        this.isModerator = roles.some(r => r.toLowerCase() === 'moderator');
        this.canCreateTopics = this.isModerator;
      });
  }

  ngOnInit(): void {
    this.loadTopics();
  }

  onScrollDown(): void {
    this.loadTopics();
  }

  toggleCollapse(topic: BlogTopicViewModel): void {
    topic.collapsed = !topic.collapsed;
  }

  trackByTopicId(_: number, topic: BlogTopicViewModel): number {
    return topic.id;
  }

  editTopic(topic: BlogTopicViewModel): void {
    if (!this.isModerator) {
      return;
    }

    this.router.navigate(['/blog', topic.slug, 'edit']);
  }

  deleteTopic(topic: BlogTopicViewModel): void {
    if (!this.isModerator || topic.deleting) {
      return;
    }

    const confirmed = confirm(`Удалить тему «${topic.header}»?`);
    if (!confirmed) {
      return;
    }

    topic.deleting = true;
    topic.actionError = '';

    this.blogService
      .deleteTopic(topic.id)
      .pipe(
        finalize(() => {
          topic.deleting = false;
        })
      )
      .subscribe({
        next: () => {
          this.topics = this.topics.filter(t => t.id !== topic.id);
        },
        error: () => {
          topic.actionError = 'Не удалось удалить тему. Попробуйте позже.';
        }
      });
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
      renderedText: rendered,
      deleting: false,
      actionError: ''
    };
  }
}
