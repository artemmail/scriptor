import { Component, EventEmitter, Input, OnChanges, OnDestroy, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { EMPTY, Subscription, timer } from 'rxjs';
import { catchError, exhaustMap, takeWhile } from 'rxjs/operators';
import { RecognizeStatus, YoutubeCaptionTaskDto, SubtitleService } from '../services/subtitle.service';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatIconModule } from '@angular/material/icon';
import { LocalTimePipe } from '../pipe/local-time.pipe';

@Component({
  selector: 'app-task-progress',
  standalone: true,
  imports: [CommonModule, MatIconModule, LocalTimePipe, MatProgressBarModule],
  templateUrl: './task-progress.component.html',
  styleUrls: ['./task-progress.component.css'],
})
export class TaskProgressComponent implements OnChanges, OnDestroy {
  @Input() taskId!: string;
  @Output() taskLoaded = new EventEmitter<YoutubeCaptionTaskDto>();
  @Output() taskError = new EventEmitter<string>();
  @Output() taskDone = new EventEmitter<YoutubeCaptionTaskDto>();
  youtubeTask: YoutubeCaptionTaskDto | null = null;
  loadError = '';
  unavailable = false;
  private autoRefreshSub?: Subscription;
  constructor(private subtitleService: SubtitleService) {}
  ngOnChanges(): void { this.youtubeTask = null; this.loadTask(); }
  ngOnDestroy(): void { this.autoRefreshSub?.unsubscribe(); }

  loadTask(): void {
    this.autoRefreshSub?.unsubscribe();
    this.loadError = '';
    this.unavailable = false;
    if (!this.taskId) return;
    this.autoRefreshSub = timer(0, 10000).pipe(
      exhaustMap(() => this.subtitleService.getStatus(this.taskId).pipe(catchError((error: HttpErrorResponse) => {
        this.unavailable = [401, 403, 404].includes(error.status);
        this.loadError = error.status === 404 ? 'Материал не найден или больше недоступен.'
          : error.status === 401 || error.status === 403 ? 'Для просмотра этого материала нужен доступ. Войдите в нужный аккаунт.'
          : 'Не удалось обновить статус. Повторим запрос автоматически.';
        if (this.unavailable) this.autoRefreshSub?.unsubscribe();
        return EMPTY;
      }))),
      takeWhile(task => !task.done && task.status !== RecognizeStatus.Done && task.status !== RecognizeStatus.Error, true),
    ).subscribe(task => {
      this.loadError = '';
      this.youtubeTask = task;
      this.taskLoaded.emit(task);
      // Failed tasks may also have done=true; an error must never appear as a finished document.
      if (task.status === RecognizeStatus.Error) this.taskError.emit(task.error || 'Не удалось обработать видео.');
      else if (task.done || task.status === RecognizeStatus.Done) this.taskDone.emit(task);
    });
  }
  get segmentProgress(): number {
    const task = this.youtubeTask;
    return task && task.segmentsTotal > 0 ? Math.min(100, Math.max(0, task.segmentsProcessed / task.segmentsTotal * 100)) : 0;
  }
  get stage(): number {
    switch (this.youtubeTask?.status) {
      case RecognizeStatus.Created: return 0;
      case RecognizeStatus.FetchingSubtitles:
      case RecognizeStatus.DownloadingCaptions:
      case RecognizeStatus.Converting:
      case RecognizeStatus.Uploading: return 1;
      case RecognizeStatus.Done: return 3;
      default: return 2;
    }
  }
  get statusText(): string {
    switch (this.youtubeTask?.status) {
      case RecognizeStatus.Created: return 'Задача в очереди';
      case RecognizeStatus.FetchingSubtitles: return 'Проверяем видео';
      case RecognizeStatus.DownloadingCaptions: return 'Получаем субтитры';
      case RecognizeStatus.SegmentingCaptions: return 'Разбиваем текст на фрагменты';
      case RecognizeStatus.ApplyingPunctuationSegment:
      case RecognizeStatus.ApplyingPunctuation: return 'Оформляем расшифровку';
      case RecognizeStatus.Converting: return 'Подготавливаем аудио';
      case RecognizeStatus.Uploading: return 'Загружаем запись';
      case RecognizeStatus.Recognizing: return 'Распознаём речь';
      case RecognizeStatus.RetrievingResult: return 'Получаем результат';
      case RecognizeStatus.Done: return 'Расшифровка готова';
      default: return 'Готовим ваш документ';
    }
  }
}
