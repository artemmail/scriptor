import { Component, OnInit, OnDestroy, Inject } from '@angular/core';
import { CommonModule, DOCUMENT }              from '@angular/common';
import { FormControl, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpClientModule }                 from '@angular/common/http';
import { saveAs }                           from 'file-saver';

// Angular Material
import { MatFormFieldModule }   from '@angular/material/form-field';
import { MatInputModule }       from '@angular/material/input';
import { MatButtonModule }      from '@angular/material/button';
import { MatIconModule }        from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule }       from '@angular/material/table';
import { MatCheckboxModule }    from '@angular/material/checkbox';
import { RouterModule }         from '@angular/router';

import { MergedVideoDto, YoutubeService } from '../services/youtube.service';
import { StreamDto } from '../models/stream-dto';
import { BitratePipe, FileSizePipe } from '../pipe/local-time.pipe';




@Component({
  standalone: true,
  selector: 'app-youtube-downloader',
  templateUrl: './youtube-downloader.component.html',
  styleUrls: ['./youtube-downloader.component.css'],
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    HttpClientModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatTableModule,
    MatCheckboxModule,
    RouterModule,
    BitratePipe,
    FileSizePipe, // <-- pipe подключен
  ]
})
export class YoutubeDownloaderComponent implements OnInit, OnDestroy {
  readonly heroHighlights = [
    'Скачивайте видео и аудио дорожки отдельно или объединяйте их в один файл',
    'Выбирайте нужное качество и контейнер без ручных настроек',
    'Получайте готовый результат и историю задач прямо в браузере'
  ];

  readonly cardHighlights = [
    {
      title: 'Интеллектуальный подбор',
      description: 'Мы группируем потоки по типу, кодеку и размеру, чтобы вам было проще сделать выбор.'
    },
    {
      title: 'Автоматическое объединение',
      description: 'Сервис создаёт задачу, отслеживает прогресс и сообщает, когда файл готов.'
    },
    {
      title: 'История всегда под рукой',
      description: 'Скачивайте готовые результаты из истории задач, даже если закрыли вкладку.'
    }
  ];

  readonly quickTips = [
    'Выберите одну видеодорожку и один аудиопоток — этого достаточно для готового ролика.',
    'Если нужно только аудио, отметьте один поток и получите MP3 без лишних шагов.',
    'Готовые файлы можно скачать напрямую из истории задач или по ссылке после объединения.'
  ];

  // —— Секция: выбор и объединение стримов ——
  videoUrlControl = new FormControl<string>('', [
    Validators.required,
    Validators.pattern(/^(https?:\/\/)?(www\.)?(youtube\.com|youtu\.be)\/.+$/)
  ]);

  baseColumns: string[]      = ['select','typeIcon','codec','quality','bitrate','size'];
  displayedColumns: string[] = [...this.baseColumns];
  showLanguageColumn = false;

  streams: StreamDto[]         = [];
  selectedStreams: StreamDto[] = [];
  isLoading  = false;
  errorMessage = '';

  isMerging       = false;
  mergeProgress   = 0;
  mergeStatus     = '';
  mergeResult     = '';
  mergeDownloadUrl: string | null = null;
  mergeFileName:   string | null = null;

  private lastTaskId: string | null = null;
  private progressInterval: any     = null;

  // —— Секция: список всех задач (Done и не Done) ——
  mergedVideos: MergedVideoDto[] = [];
  mergedErrorMessage = '';

  constructor(
    private youtubeService: YoutubeService,
    @Inject(DOCUMENT) private document: Document | null
  ) {}

  ngOnInit(): void {
    this.loadMergedVideos();
  }

  ngOnDestroy(): void {
    if (this.progressInterval) {
      clearInterval(this.progressInterval);
      this.progressInterval = null;
    }
  }

  // === Потоки ===
  fetchStreams(): void {
    if (this.videoUrlControl.invalid) {
      this.errorMessage = 'Некорректный URL.';
      return;
    }

    const videoUrl = this.videoUrlControl.value!;
    this.isLoading    = true;
    this.errorMessage = '';
    this.mergeResult  = '';
    this.mergeDownloadUrl = null;
    this.mergeFileName    = null;
    this.streams      = [];
    this.selectedStreams = [];
    this.showLanguageColumn = false;
    this.displayedColumns = [...this.baseColumns];
    this.mergeStatus = '';

    this.youtubeService.getAllStreams(videoUrl).subscribe({
      next: data => {
        // сортировка: type -> codec -> size (size по убыванию)
        this.streams = this.sortStreams(data);
        this.isLoading  = false;

        if (this.streams.some(s => s.type === 'audio' && s.language)) {
          this.showLanguageColumn = true;
          if (!this.displayedColumns.includes('language')) {
            this.displayedColumns.push('language');
          }
        }
      },
      error: err => {
        console.error(err);
        this.errorMessage = 'Ошибка при получении потоков.';
        this.isLoading    = false;
      }
    });
  }

  /** Сортировка распознанных потоков */
  private sortStreams(data: StreamDto[]): StreamDto[] {
    const rank = (t?: string | null) => {
      const x = (t || '').toLowerCase();
      if (x === 'video' || x === 'muxed') return 0; // «в начале видео»
      if (x === 'audio') return 1;
      return 2;
    };
    return [...data].sort((a, b) => {
      // 1) тип
      const r = rank(a.type) - rank(b.type);
      if (r !== 0) return r;

      // 2) кодек (A→Z)
      const ca = (a.codec ?? '').toString();
      const cb = (b.codec ?? '').toString();
      const cr = ca.localeCompare(cb, undefined, { sensitivity: 'base', numeric: true });
      if (cr !== 0) return cr;

      // 3) размер (крупные вверх; поменять знак для возрастания)
      const sa = a.size ?? 0;
      const sb = b.size ?? 0;
      return sb - sa;
    });
  }

  toggleSelection(stream: StreamDto, checked: boolean): void {
    if (checked) {
      if (!this.isRowDisabled(stream)) {
        this.selectedStreams.push(stream);
      }
    } else {
      this.removeStream(stream);
      if (stream.type === 'video') {
        const audios = this.selectedStreams.filter(s => s.type === 'audio');
        if (audios.length > 1) {
          this.selectedStreams = [audios[0]];
        }
      }
    }
  }

  isRowDisabled(stream: StreamDto): boolean {
    if (this.isStreamSelected(stream)) return false;
    if (stream.type === 'video') {
      return this.selectedStreams.some(s => s.type === 'video');
    }
    const hasVideo = this.selectedStreams.some(s => s.type === 'video');
    if (hasVideo) return false;
    return this.selectedStreams.filter(s => s.type === 'audio').length >= 1;
  }

  isStreamSelected(stream: StreamDto): boolean {
    return this.selectedStreams.includes(stream);
  }

  removeStream(stream: StreamDto): void {
    const idx = this.selectedStreams.indexOf(stream);
    if (idx > -1) this.selectedStreams.splice(idx, 1);
  }

  mergeSelectedStreams(): void {
    const video  = this.selectedStreams.find(s => s.type === 'video');
    const audios = this.selectedStreams.filter(s => s.type === 'audio');
    if (!video && audios.length !== 1) {
      this.errorMessage = 'Выберите либо 1 видео (и любые аудио), либо ровно 1 аудио без видео.';
      return;
    }

    this.errorMessage     = '';
    this.mergeResult      = '';
    this.mergeDownloadUrl = null;
    this.mergeFileName    = null;
    this.isMerging        = true;
    this.mergeProgress    = 0;
    this.mergeStatus      = '';
    this.lastTaskId       = null;

    const videoUrl = this.videoUrlControl.value!;
    const quality  = video?.qualityLabel
      ? (typeof video.qualityLabel === 'object'
          ? video.qualityLabel.label
          : video.qualityLabel)
      : '';
    const container = video ? (video.container ?? 'mp4') : 'mp3';

    this.youtubeService.mergeVideoAndAudios(
      videoUrl, quality, container, audios
    ).subscribe({
      next: resp => {
        if (!resp?.taskId) {
          this.errorMessage = 'Не удалось получить taskId от сервера.';
          this.isMerging = false;
          return;
        }
        this.lastTaskId = resp.taskId;
        this.pollProgress(resp.taskId);
      },
      error: err => {
        console.error(err);
        this.errorMessage = 'Ошибка при запуске объединения.';
        this.isMerging = false;
      }
    });
  }

  private pollProgress(taskId: string): void {
    if (this.progressInterval) {
      clearInterval(this.progressInterval);
    }
    this.progressInterval = setInterval(() => {
      this.youtubeService.getProgress(taskId).subscribe({
        next: statusResp => {
          this.mergeStatus   = statusResp.status;
          this.mergeProgress = statusResp.progress;
          if (statusResp.error) {
            this.errorMessage = statusResp.error;
          }
          if (statusResp.downloadUrl) this.mergeDownloadUrl = statusResp.downloadUrl;
          if (statusResp.fileName)    this.mergeFileName    = statusResp.fileName;

          if (this.mergeStatus === 'Done' || this.mergeProgress >= 100) {
            clearInterval(this.progressInterval);
            this.progressInterval = null;
            this.isMerging = false;
            this.mergeResult = `Задача завершена: ${this.mergeStatus} (${this.mergeProgress}%)`;
            this.loadMergedVideos();
          }
          if (this.mergeStatus === 'Error') {
            clearInterval(this.progressInterval);
            this.progressInterval = null;
            this.isMerging = false;
            this.mergeResult = `Ошибка при выполнении задачи: ${this.errorMessage}`;
          }
        },
        error: err => console.error('Ошибка при запросе прогресса:', err)
      });
    }, 3000);
  }

  downloadMergedFile(): void {
    if (!this.lastTaskId) {
      this.errorMessage = 'Нет taskId для скачивания результата.';
      return;
    }
    this.youtubeService.downloadMergedResult(this.lastTaskId).subscribe({
      next: blob => saveAs(blob, this.mergeFileName || `merged_${this.lastTaskId}.mp4`),
      error: err => {
        console.error(err);
        this.errorMessage = `Ошибка при скачивании результата: ${err.message}`;
      }
    });
  }

  // === Таблица задач ===
  loadMergedVideos(): void {
    this.youtubeService.getMergedVideos().subscribe({
      next: list => this.mergedVideos = list,
      error: ()   => this.mergedErrorMessage = 'Не удалось загрузить список видео.'
    });
  }

  downloadMergedVideo(item: MergedVideoDto): void {
    if (!item.filePath) return;
    this.youtubeService.downloadMergedResult(item.taskId).subscribe({
      next: blob => saveAs(blob, item.fileName || `merged_${item.taskId}.mp4`),
      error: err => {
        console.error(err);
        this.errorMessage = `Ошибка при скачивании ${item.taskId}: ${err.message}`;
      }
    });
  }

  scrollToWorkspace(): void {
    const target = this.document?.getElementById('downloader-workspace');
    if (target) {
      target.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }
}
