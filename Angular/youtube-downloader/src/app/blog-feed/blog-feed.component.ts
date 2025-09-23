import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators, FormGroup, FormsModule } from '@angular/forms';
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

interface BlogTopicViewModel extends BlogTopic {
  collapsed: boolean;
  textIsTooLong: boolean;
  newComment: string;
  submittingComment: boolean;
  commentError?: string;
}

@Component({
  selector: 'app-blog-feed',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatDividerModule,
    MatProgressSpinnerModule,
    InfiniteScrollModule
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

  topicForm: FormGroup;
  topicSubmitting = false;
  topicError = '';

  currentUser: UserInfo | null = null;
  canCreateTopics = false;

  constructor(
    private readonly blogService: BlogService,
    private readonly fb: FormBuilder,
    private readonly authService: AuthService,
    private readonly destroyRef: DestroyRef
  ) {
    this.topicForm = this.fb.group({
      title: ['', [Validators.required, Validators.maxLength(256)]],
      text: ['', [Validators.required, Validators.maxLength(10000)]]
    });

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

  submitTopic(): void {
    if (!this.canCreateTopics || this.topicSubmitting) {
      return;
    }

    this.topicError = '';

    if (this.topicForm.invalid) {
      this.topicForm.markAllAsTouched();
      return;
    }

    const title = (this.topicForm.value.title ?? '').trim();
    const text = (this.topicForm.value.text ?? '').trim();

    if (!title || !text) {
      this.topicError = 'Заполните заголовок и текст темы.';
      return;
    }

    this.topicSubmitting = true;
    this.blogService
      .createTopic({ title, text })
      .pipe(
        finalize(() => {
          this.topicSubmitting = false;
        })
      )
      .subscribe({
        next: topic => {
          const mapped = this.mapTopic(topic);
          this.topics = [mapped, ...this.topics];
          this.skip += 1;
          this.allLoaded = false;
          this.topicForm.reset();
        },
        error: () => {
          this.topicError = 'Не удалось создать тему. Попробуйте позже.';
        }
      });
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
    return {
      ...topic,
      collapsed: isTooLong,
      textIsTooLong: isTooLong,
      newComment: '',
      submittingComment: false
    };
  }
}
