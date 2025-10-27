import { Component, EventEmitter, Input, OnChanges, OnDestroy, OnInit, Output, SimpleChanges } from '@angular/core';
import { Subscription, timer } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import {
  RecognizeStatus,
  YoutubeCaptionTaskDto,
  SubtitleService
} from '../services/subtitle.service';

// Angular Material
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner'; // Импорт спиннера
import { MatIconModule } from '@angular/material/icon';

// Common
import { CommonModule } from '@angular/common';
import { LocalTimePipe } from '../pipe/local-time.pipe';
import { YandexAdComponent } from "../ydx-ad/yandex-ad.component";

@Component({
  selector: 'app-task-progress',
  standalone: true,
  imports: [
    CommonModule,
    MatIconModule,
    LocalTimePipe,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    YandexAdComponent
],
  templateUrl: './task-progress.component.html',
  styleUrls: ['./task-progress.component.css']
})
export class TaskProgressComponent implements OnInit, OnDestroy, OnChanges {
  @Input() taskId!: string;
  @Output() taskLoaded = new EventEmitter<YoutubeCaptionTaskDto>();
  @Output() taskError = new EventEmitter<string>();
  @Output() taskDone = new EventEmitter<YoutubeCaptionTaskDto>();

  youtubeTask: YoutubeCaptionTaskDto | null = null;
  public RecognizeStatus = RecognizeStatus;

  private autoRefreshSub?: Subscription;

  constructor(private subtitleService: SubtitleService) {}

  ngOnInit(): void {
    this.loadTask();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['taskId'] && !changes['taskId'].firstChange) {
      this.loadTask();
    }
  }

  ngOnDestroy(): void {
    if (this.autoRefreshSub) {
      this.autoRefreshSub.unsubscribe();
    }
  }

  private loadTask(): void {
    if (!this.taskId) return;

    if (this.autoRefreshSub) {
      this.autoRefreshSub.unsubscribe();
    }

    this.subtitleService.getStatus(this.taskId).subscribe({
      next: (task) => {
        this.processTask(task);
        if (!task.done && task.status !== RecognizeStatus.Error) {
          this.autoRefreshSub = timer(10000)
            .pipe(switchMap(() => this.subtitleService.getStatus(this.taskId)))
            .subscribe((updatedTask) => {
              this.processTask(updatedTask);
              if (!updatedTask.done && updatedTask.status !== RecognizeStatus.Error) {
                this.loadTask();
              }
            });
        }
      },
      error: (err) => {
        console.error('Error fetching task status:', err);
        this.taskError.emit('Error fetching task status');
      },
    });
  }

  getStatusText(status: RecognizeStatus | null | undefined): string {
    return this.subtitleService.getStatusText(status);
  }

  getStatusIcon(status: RecognizeStatus | null | undefined): string {
    switch (status) {
      case RecognizeStatus.Error:
        return 'error';
      case RecognizeStatus.Done:
        return 'check_circle';
      default:
        return 'loop';
    }
  }

  getIconClass(status: RecognizeStatus | null | undefined): string {
    if (status === RecognizeStatus.Error) {
      return 'status-error';
    }

    if (status === RecognizeStatus.Done) {
      return 'status-success';
    }

    return 'status-progress';
  }

  getCardClass(status: RecognizeStatus | null | undefined): string {
    if (status === RecognizeStatus.Error) {
      return 'status-error';
    }

    if (status === RecognizeStatus.Done) {
      return 'status-success';
    }

    return '';
  }

  private processTask(task: YoutubeCaptionTaskDto) {
    this.youtubeTask = task;
    this.taskLoaded.emit(task);
    if (task.done) {
      this.taskDone.emit(task);
    }
    if (task.status === RecognizeStatus.Error) {
      this.taskError.emit(task.error || 'Unknown error');
    }
  }
}
