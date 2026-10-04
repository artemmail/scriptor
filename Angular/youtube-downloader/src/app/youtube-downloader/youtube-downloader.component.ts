import { Component, DestroyRef, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Subscription, timer } from 'rxjs';
import { catchError, exhaustMap, finalize, takeWhile } from 'rxjs/operators';
import { saveAs } from 'file-saver';
import { MergedVideoDto, YoutubeService } from '../services/youtube.service';
import { AuthService } from '../services/AuthService.service';
import { StreamDto } from '../models/stream-dto';
import { BitratePipe, FileSizePipe, LocalTimePipe } from '../pipe/local-time.pipe';

type DownloadMode = 'audio' | 'video' | 'silent';

export function youtubeVideoId(input: string): string | null {
  const value = input.trim();
  if (/^[\w-]{11}$/.test(value)) return value;
  try {
    const url = new URL(/^https?:\/\//i.test(value) ? value : 'https://' + value);
    if (!['https:', 'http:'].includes(url.protocol)) return null;
    const host = url.hostname.toLowerCase();
    const parts = url.pathname.split('/').filter(Boolean);
    const id = host === 'youtu.be' ? parts[0]
      : ['youtube.com', 'www.youtube.com', 'm.youtube.com', 'music.youtube.com'].includes(host)
        ? url.pathname === '/watch' ? url.searchParams.get('v')
          : ['shorts', 'embed', 'live'].includes(parts[0]) ? parts[1] : null
        : null;
    return id && /^[\w-]{11}$/.test(id) ? id : null;
  } catch { return null; }
}

@Component({
  standalone: true,
  selector: 'app-youtube-downloader',
  templateUrl: './youtube-downloader.component.html',
  styleUrls: ['../shared/account-page.css', './youtube-downloader.component.css'],
  imports: [CommonModule, ReactiveFormsModule, RouterModule, MatIconModule,
    MatProgressBarModule, BitratePipe, FileSizePipe, LocalTimePipe],
})
export class YoutubeDownloaderComponent implements OnInit {
  readonly modes: { id: DownloadMode; icon: string; title: string; hint: string }[] = [
    { id: 'audio', icon: 'headphones', title: 'Только аудио', hint: 'Звук в MP3' },
    { id: 'video', icon: 'movie', title: 'Видео со звуком', hint: 'Один файл MP4' },
    { id: 'silent', icon: 'videocam_off', title: 'Видео без звука', hint: 'Отдельная видеодорожка' },
  ];
  readonly waveform = [22, 42, 66, 36, 82, 54, 100, 72, 40, 88, 60, 34, 78, 96, 58, 28, 70, 44, 90, 62, 36, 52, 24];
  readonly videoUrlControl = new FormControl('', { nonNullable: true });
  mode: DownloadMode = 'audio';
  streams: StreamDto[] = [];
  selectedVideo: StreamDto | null = null;
  selectedAudios: StreamDto[] = [];
  loadedVideoId: string | null = null;
  isLoading = false;
  hasFetched = false;
  errorMessage = '';
  isAuthenticated = false;
  isMerging = false;
  mergeStatus = '';
  mergeError = '';
  pollingWarning = '';
  mergeFileName = '';
  activeTaskId: string | null = null;
  mergedVideos: MergedVideoDto[] = [];
  historyLoading = false;
  mergedErrorMessage = '';
  historyFilter: 'all' | 'active' | 'done' | 'error' = 'all';
  historyLimit = 6;
  readonly downloading = new Set<string>();
  readonly downloadErrors = new Map<string, string>();
  private streamsRequest?: Subscription;
  private historyRequest?: Subscription;
  private progressRequest?: Subscription;
  private mergeRequest?: Subscription;
  private userId: string | null = null;
  private activeFormat = 'mp3';

  constructor(
    private readonly youtubeService: YoutubeService,
    private readonly titleService: Title,
    private readonly auth: AuthService,
    private readonly destroyRef: DestroyRef,
  ) {
    this.titleService.setTitle('Скачать аудио и видео с YouTube — YouScriptor');
    this.destroyRef.onDestroy(() => {
      this.streamsRequest?.unsubscribe();
      this.historyRequest?.unsubscribe();
      this.progressRequest?.unsubscribe();
      this.mergeRequest?.unsubscribe();
    });
  }

  ngOnInit(): void {
    this.videoUrlControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.streamsRequest?.unsubscribe();
      this.isLoading = false;
      this.loadedVideoId = null;
      this.streams = [];
      this.selectedVideo = null;
      this.selectedAudios = [];
      this.hasFetched = false;
      this.errorMessage = '';
    });
    this.auth.user$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(user => {
      this.isAuthenticated = !!user;
      const id = user?.id ?? null;
      if (id === this.userId) return;
      this.userId = id;
      this.historyRequest?.unsubscribe();
      this.progressRequest?.unsubscribe();
      this.mergeRequest?.unsubscribe();
      this.historyLoading = false;
      this.mergedVideos = [];
      this.mergedErrorMessage = '';
      this.activeTaskId = null;
      this.isMerging = false;
      this.mergeStatus = '';
      this.mergeError = '';
      this.pollingWarning = '';
      this.downloadErrors.clear();
      if (id) this.loadMergedVideos();
    });
    timer(10000, 10000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (this.isAuthenticated && this.mergedVideos.some(item => !this.isTerminal(item.status))) {
        this.loadMergedVideos(true);
      }
    });
  }

  get videoStreams(): StreamDto[] { return this.streams.filter(s => s.type === 'video'); }
  get audioStreams(): StreamDto[] { return this.streams.filter(s => s.type === 'audio'); }
  get canPrepare(): boolean {
    return !!this.loadedVideoId && !this.isLoading && !this.isMerging
      && (this.mode === 'audio' ? this.selectedAudios.length === 1
        : !!this.selectedVideo && (this.mode === 'silent' || this.selectedAudios.length > 0));
  }
  get outputFormat(): string {
    return this.mode === 'audio' ? 'MP3' : this.mode === 'video' ? 'MP4' : (this.selectedVideo?.container || 'Видео').toUpperCase();
  }
  get selectedSize(): number {
    return (this.mode !== 'audio' ? this.selectedVideo?.size || 0 : 0)
      + (this.mode !== 'silent' ? this.selectedAudios.reduce((sum, s) => sum + (s.size || 0), 0) : 0);
  }
  get filteredHistory(): MergedVideoDto[] {
    return this.mergedVideos.filter(item => this.historyFilter === 'all'
      || this.historyFilter === 'active' && !this.isTerminal(item.status)
      || this.historyFilter === 'done' && item.status === 'Done'
      || this.historyFilter === 'error' && item.status === 'Error');
  }
  get visibleHistory(): MergedVideoDto[] { return this.filteredHistory.slice(0, this.historyLimit); }
  get readyCount(): number { return this.mergedVideos.filter(item => item.status === 'Done').length; }
  isTerminal(status: string): boolean { return status === 'Done' || status === 'Error'; }
  quality(stream: StreamDto): string {
    return typeof stream.qualityLabel === 'string' ? stream.qualityLabel : stream.qualityLabel?.label || 'Видео';
  }
  statusLabel(status: string): string {
    return ({ Created: 'В очереди', Downloading: 'Скачиваем дорожки', Merging: 'Готовим файл',
      Done: 'Готово к скачиванию', Error: 'Ошибка подготовки' } as Record<string, string>)[status] || 'Ожидаем обновления';
  }
  setMode(mode: DownloadMode): void {
    if (this.isMerging) return;
    this.mode = mode;
    if (mode === 'audio') this.selectedAudios = this.selectedAudios.slice(0, 1);
    this.applyDefaults();
  }
  private applyDefaults(): void {
    if (!this.selectedVideo) this.selectedVideo = this.videoStreams[0] ?? null;
    if (!this.selectedAudios.length && this.audioStreams.length) this.selectedAudios = [this.audioStreams[0]];
  }
  selectVideo(stream: StreamDto): void {
    if (!this.isMerging && this.videoStreams.includes(stream)) this.selectedVideo = stream;
  }
  selectAudio(stream: StreamDto, selected: boolean): void {
    if (this.isMerging || !this.audioStreams.includes(stream)) return;
    this.selectedAudios = this.mode === 'audio' ? [stream]
      : selected ? Array.from(new Set([...this.selectedAudios, stream])) : this.selectedAudios.filter(s => s !== stream);
  }

  fetchStreams(): void {
    if (this.isMerging || this.isLoading) return;
    const id = youtubeVideoId(this.videoUrlControl.value);
    if (!id) {
      this.errorMessage = 'Вставьте ссылку на YouTube-видео, Shorts или ID из 11 символов.';
      return;
    }
    this.streamsRequest?.unsubscribe();
    this.isLoading = true;
    this.errorMessage = '';
    this.streams = [];
    this.selectedVideo = null;
    this.selectedAudios = [];
    this.loadedVideoId = null;
    this.hasFetched = false;
    this.streamsRequest = this.youtubeService.getAllStreams(id)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.isLoading = false))
      .subscribe({
        next: data => {
          // The merge API accepts separate video/audio streams, not muxed streams.
          this.streams = data.filter(s => s.type === 'audio' || s.type === 'video').sort((a, b) =>
            (parseInt(this.quality(b), 10) || 0) - (parseInt(this.quality(a), 10) || 0)
            || (b.bitrate || 0) - (a.bitrate || 0));
          this.loadedVideoId = id;
          this.hasFetched = true;
          this.applyDefaults();
        },
        error: error => this.errorMessage = this.errorText(error, 'Не удалось получить дорожки. Проверьте доступность ролика и попробуйте снова.'),
      });
  }

  mergeSelectedStreams(): void {
    if (!this.canPrepare || !this.isAuthenticated) return;
    const video = this.mode === 'audio' ? null : this.selectedVideo;
    const audios = this.mode === 'silent' ? [] : this.selectedAudios;
    this.isMerging = true;
    this.mergeStatus = 'Created';
    this.mergeError = '';
    this.pollingWarning = '';
    this.mergeFileName = '';
    this.activeTaskId = null;
    this.activeFormat = this.outputFormat.toLowerCase();
    this.mergeRequest = this.youtubeService.mergeVideoAndAudios(
      this.loadedVideoId!, video ? this.quality(video) : '', video?.container || 'mp3', audios,
    ).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: response => {
        if (!response?.taskId) {
          this.isMerging = false;
          this.mergeStatus = 'Error';
          this.mergeError = 'Сервис не подтвердил создание задачи. Обновите историю перед повторным запуском.';
          return;
        }
        this.activeTaskId = response.taskId;
        this.pollProgress(response.taskId);
        this.loadMergedVideos();
      },
      error: error => {
        this.isMerging = false;
        this.mergeStatus = 'Error';
        this.mergeError = this.errorText(error, 'Не удалось запустить подготовку. Попробуйте снова.');
      },
    });
  }

  private pollProgress(taskId: string): void {
    this.progressRequest?.unsubscribe();
    this.progressRequest = timer(0, 3000).pipe(
      exhaustMap(() => this.youtubeService.getProgress(taskId).pipe(catchError((error: HttpErrorResponse) => {
        if ([401, 403, 404].includes(error.status)) {
          this.progressRequest?.unsubscribe();
          this.isMerging = false;
          this.mergeStatus = 'Error';
          this.pollingWarning = '';
          this.mergeError = this.errorText(error, 'Задача недоступна.');
          return EMPTY;
        }
        this.pollingWarning = 'Не удалось обновить статус. Повторяем запрос; задача продолжает выполняться на сервере.';
        return EMPTY;
      }))),
      takeWhile(status => !this.isTerminal(status.status), true),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(status => {
      this.pollingWarning = '';
      this.mergeStatus = status.status;
      this.mergeFileName = status.fileName || '';
      this.mergeError = status.status === 'Error' ? status.error || 'Не удалось подготовить файл. Попробуйте другой набор дорожек.' : '';
      if (this.isTerminal(status.status)) {
        this.isMerging = false;
        this.loadMergedVideos();
      }
    });
  }

  loadMergedVideos(background = false): void {
    if (!this.isAuthenticated || background && this.historyRequest && !this.historyRequest.closed) return;
    this.historyRequest?.unsubscribe();
    this.historyLoading = !background;
    if (!background) this.mergedErrorMessage = '';
    this.historyRequest = this.youtubeService.getMergedVideos()
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.historyLoading = false))
      .subscribe({
        next: list => {
          this.mergedVideos = [...list].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt));
          this.mergedErrorMessage = '';
        },
        error: error => this.mergedErrorMessage = this.errorText(error, 'Не удалось обновить историю. Попробуйте ещё раз.'),
      });
  }
  setHistoryFilter(filter: 'all' | 'active' | 'done' | 'error'): void {
    this.historyFilter = filter;
    this.historyLimit = 6;
  }
  canDownload(item: MergedVideoDto): boolean {
    return item.status === 'Done' && !!(item.downloadUrl || item.fileName || item.filePath);
  }
  downloadMergedFile(): void {
    if (this.activeTaskId && this.mergeStatus === 'Done') {
      this.download(this.activeTaskId, this.mergeFileName || 'youtube.' + this.activeFormat);
    }
  }
  downloadMergedVideo(item: MergedVideoDto): void {
    if (this.canDownload(item)) this.download(item.taskId, item.fileName || 'youtube-' + item.taskId);
  }
  private download(taskId: string, name: string): void {
    if (this.downloading.has(taskId)) return;
    this.downloading.add(taskId);
    this.downloadErrors.delete(taskId);
    this.youtubeService.downloadMergedResult(taskId)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.downloading.delete(taskId)))
      .subscribe({
        next: blob => saveAs(blob, name),
        error: error => this.downloadErrors.set(taskId, this.errorText(error, 'Не удалось скачать файл. Попробуйте ещё раз.')),
      });
  }
  private errorText(error: HttpErrorResponse, fallback: string): string {
    if (error.status === 401) return 'Войдите в аккаунт, чтобы продолжить.';
    if (error.status === 403) return 'Доступ к этой задаче ограничен.';
    if (error.status === 404) return 'Файл или задача больше недоступны.';
    const message = error.error?.detail || error.error?.message;
    return typeof message === 'string' ? message : fallback;
  }
}
