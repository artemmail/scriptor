import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterModule } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { finalize } from 'rxjs/operators';
import { BlogService, BlogTopic } from '../services/blog.service';
import { AuthService } from '../services/AuthService.service';
import { LocalTimePipe } from '../pipe/local-time.pipe';

interface BlogTopicViewModel extends BlogTopic {
  excerpt: string;
  readingMinutes: number;
  authorInitial: string;
  deleting: boolean;
  actionError: string;
}

export function blogPlainText(markdown: string): string {
  return markdown
    .replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '')
    .replace(/<style\b[^>]*>[\s\S]*?<\/style>/gi, '')
    .replace(/<[^>]*>/g, ' ')
    .replace(/!\[[^\]]*\]\([^)]*\)/g, '')
    .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
    .replace(/^\s{0,3}(?:#{1,6}\s+|>\s*|[-*+]\s+|\d+[.)]\s+)/gm, '')
    .replace(/\x60{3}[^\r\n]*|\x60|[*_~]/g, '')
    .replace(/&(?:amp|lt|gt|quot|apos|nbsp);/g, entity =>
      ({ '&amp;': '&', '&lt;': '<', '&gt;': '>', '&quot;': '"', '&apos;': "'", '&nbsp;': ' ' }[entity] || entity))
    .replace(/\s+/g, ' ')
    .trim();
}

@Component({
  selector: 'app-blog-feed',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatProgressBarModule, RouterModule, LocalTimePipe],
  templateUrl: './blog-feed.component.html',
  styleUrls: ['../shared/account-page.css', './blog-feed.component.css'],
})
export class BlogFeedComponent implements OnInit {
  private readonly pageSize = 10;
  private skip = 0;
  readonly skeletons = [1, 2, 3, 4];
  topics: BlogTopicViewModel[] = [];
  loading = false;
  allLoaded = false;
  initialLoad = false;
  feedError = '';
  isModerator = false;
  pendingDeleteId: number | null = null;
  announcement = '';

  constructor(
    private readonly blogService: BlogService,
    private readonly authService: AuthService,
    private readonly destroyRef: DestroyRef,
    private readonly titleService: Title,
  ) {
    this.authService.user$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(user => {
      this.isModerator = !!user?.roles?.some(role => role.toLowerCase() === 'moderator');
      if (!this.isModerator) this.pendingDeleteId = null;
    });
    this.titleService.setTitle('Блог YouScriptor — идеи, опыт и работа с текстом');
  }

  ngOnInit(): void { this.loadTopics(); }
  get deleting(): boolean { return this.topics.some(topic => topic.deleting); }
  trackByTopicId(_: number, topic: BlogTopicViewModel): number { return topic.id; }

  requestDelete(topic: BlogTopicViewModel): void {
    if (!this.isModerator || this.loading || this.deleting) return;
    this.pendingDeleteId = topic.id;
    topic.actionError = '';
  }

  cancelDelete(): void {
    if (!this.deleting) this.pendingDeleteId = null;
  }

  deleteTopic(topic: BlogTopicViewModel): void {
    if (!this.isModerator || this.pendingDeleteId !== topic.id || this.loading || this.deleting) return;
    topic.deleting = true;
    topic.actionError = '';
    this.blogService.deleteTopic(topic.id)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => topic.deleting = false))
      .subscribe({
        next: () => {
          this.topics = this.topics.filter(item => item.id !== topic.id);
          // Offset pagination shifts after deletion; do not skip the next unseen publication.
          this.skip = Math.max(0, this.skip - 1);
          this.pendingDeleteId = null;
          this.announcement = 'Публикация «' + topic.header + '» удалена.';
        },
        error: () => topic.actionError = 'Не удалось удалить публикацию. Попробуйте ещё раз.',
      });
  }

  loadTopics(): void {
    if (this.loading || this.allLoaded || this.deleting) return;
    this.loading = true;
    this.feedError = '';
    this.blogService.getTopics(this.skip, this.pageSize)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => {
        this.loading = false;
        this.initialLoad = true;
      })).subscribe({
        next: topics => {
          const knownIds = new Set(this.topics.map(topic => topic.id));
          const additions = topics.filter(topic => {
            if (knownIds.has(topic.id)) return false;
            knownIds.add(topic.id);
            return true;
          }).map(topic => this.mapTopic(topic));
          this.topics = [...this.topics, ...additions];
          this.skip += topics.length;
          this.allLoaded = topics.length < this.pageSize;
        },
        error: () => this.feedError = this.topics.length
          ? 'Не удалось загрузить следующие публикации. Уже открытые материалы остались на странице.'
          : 'Не удалось загрузить блог. Попробуйте ещё раз.',
      });
  }

  private mapTopic(topic: BlogTopic): BlogTopicViewModel {
    const text = blogPlainText(topic.text || '');
    const words = text ? text.split(/\s+/).length : 0;
    const excerpt = text.length > 270 ? text.slice(0, 270).replace(/\s+\S*$/, '') + '…' : text;
    return {
      ...topic,
      excerpt: excerpt || 'Откройте публикацию, чтобы посмотреть материал и присоединиться к обсуждению.',
      readingMinutes: Math.max(1, Math.ceil(words / 200)),
      authorInitial: Array.from(topic.user?.trim() || 'Y')[0].toLocaleUpperCase(),
      deleting: false,
      actionError: '',
    };
  }
}
