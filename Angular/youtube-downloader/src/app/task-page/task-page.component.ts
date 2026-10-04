import { Component, DestroyRef, OnInit } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { AuthService } from '../services/AuthService.service';
import { SubtitleService, YoutubeCaptionTaskDto } from '../services/subtitle.service';
import { TaskProgressComponent } from '../task-progress/task-progress.component';
import { TaskResultComponent } from '../task-result/task-result.component';

// Common
import { CommonModule } from '@angular/common';
import { Title } from '@angular/platform-browser';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-task-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    MatIconModule,
    TaskProgressComponent,
    TaskResultComponent
  ],
  templateUrl: './task-page.component.html',
  styleUrls: ['../shared/account-page.css', './task-page.component.css']
})
export class TaskPageComponent implements OnInit {
  taskId!: string;
  task: YoutubeCaptionTaskDto | null = null;
  taskDone = false;
  taskErrorMessage: string | null = null;
  restartErrorMessage: string | null = null;
  restartInProgress = false;
  isAuthenticated = false;
  private restartRequest?: Subscription;

  constructor(
    private route: ActivatedRoute,
    private titleService: Title,
    private router: Router,
    private subtitleService: SubtitleService,
    private readonly auth: AuthService,
    private readonly destroyRef: DestroyRef,
  ) {
    this.auth.user$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(user => this.isAuthenticated = !!user);
    this.destroyRef.onDestroy(() => this.restartRequest?.unsubscribe());
  }

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const paramId = params.get('id');
      if (paramId) {
        if (paramId !== this.taskId) {
          this.restartRequest?.unsubscribe();
          this.titleService.setTitle('Расшифровка — YouScriptor');
          this.taskDone = false;
          this.task = null;
          this.taskErrorMessage = null;
          this.restartErrorMessage = null;
          this.restartInProgress = false;
        }
        this.taskId = paramId;
        // Здесь можно загрузить задачу по taskId, если это необходимо
      }
    });
  }

  onTaskLoaded(task: YoutubeCaptionTaskDto) {
    this.ensureCanonicalUrl(task);
    this.task = task;
    if (task.title) {
      this.titleService.setTitle(task.title);
    }
  }

  onTaskDone(task: YoutubeCaptionTaskDto) {
    this.ensureCanonicalUrl(task);
    this.task = task;
    this.taskDone = true;
    if (task.title) {
      this.titleService.setTitle(task.title);
    }
  }

  onTaskError(msg: string) {
    this.taskDone = false;
    this.taskErrorMessage = msg;
    this.restartErrorMessage = null;
    // Вы можете установить заголовок на значение по умолчанию или оставить прежним
    this.titleService.setTitle('Ошибка задачи');
  }

  onRestartTask(): void {
    if (!this.taskId || this.restartInProgress || !this.isAuthenticated) {
      return;
    }

    this.restartInProgress = true;
    this.restartErrorMessage = null;

    this.restartRequest = this.subtitleService.restartTask(this.taskId)
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (task) => {
        this.restartInProgress = false;
        this.task = task;
        this.taskDone = false;
        this.taskErrorMessage = null;
        this.ensureCanonicalUrl(task);
        if (task.title) {
          this.titleService.setTitle(task.title);
        }
      },
      error: (err) => {
        this.restartInProgress = false;
        this.restartErrorMessage = this.getRestartErrorMessage(err);
      },
    });
  }

  private ensureCanonicalUrl(task: YoutubeCaptionTaskDto): void {
    const slug = task.slug;
    if (slug && slug !== this.taskId) {
      this.taskId = slug;
      this.router.navigate(['/recognized', slug], { replaceUrl: true });
    }
  }

  private getRestartErrorMessage(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 401) {
        return 'Для перезапуска задачи нужно войти в аккаунт.';
      }

      if (err.status === 403) {
        return 'Перезапустить задачу может только владелец или администратор.';
      }

      if (typeof err.error === 'string' && err.error.trim()) {
        return err.error;
      }

      if (err.error?.message) {
        return err.error.message;
      }
    }

    return 'Не удалось перезапустить задачу. Попробуйте позже.';
  }
}
