import { Component, DestroyRef, OnInit, ViewChild } from '@angular/core';
import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { HttpClientModule } from '@angular/common/http';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { Title } from '@angular/platform-browser';

import { MatTableDataSource, MatTableModule } from '@angular/material/table';
import { MatSort, MatSortModule, Sort } from '@angular/material/sort';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';

import { MarkdownModule } from 'ngx-markdown';
import { InfiniteScrollModule } from 'ngx-infinite-scroll';

import { CommonModule } from '@angular/common';
import { LocalTimePipe } from '../pipe/local-time.pipe';
import { SubtitleService, YoutubeCaptionTaskDto2, RecognizeStatus } from '../services/subtitle.service';
import { VideoDialogComponent, VideoDialogData } from '../video-dialog/video-dialog.component';
import { YandexAdComponent } from '../ydx-ad/yandex-ad.component';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '../services/AuthService.service';
import { interval, forkJoin } from 'rxjs';
import { finalize, map } from 'rxjs/operators';

@Component({
  selector: 'app-subtitles-tasks',
  standalone: true,
  templateUrl: './subtitles-tasks.component.html',
  styleUrls: ['./subtitles-tasks.component.css'],
  imports: [
    CommonModule,
    HttpClientModule,
    RouterModule,
    /* Material & CDK */
    MatTableModule,
    MatSortModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatDialogModule,
    /* 3‑rd party */
    MarkdownModule,
    InfiniteScrollModule,
    /* Stand‑alone components & pipes */
    LocalTimePipe,
    VideoDialogComponent,
    YandexAdComponent,
  ],
})
export class SubtitlesTasksComponent implements OnInit {
  displayedColumns: string[] = [];
  dataSource = new MatTableDataSource<YoutubeCaptionTaskDto2>();
  RecognizeStatus = RecognizeStatus;

  totalItems = 0;
  pageSize = 15;
  pageIndex = 0;
  sortField = '';
  sortOrder = '';
  filterValue = '';
  userIdFilter: string | null = null;

  isMobile = false;
  loading = false;
  expandedTasks = new Set<string>();
  isAuthenticated = false;
  private readonly refreshIntervalMs = 10_000;
  private refreshInProgress = false;

  @ViewChild(MatSort) sort!: MatSort;

  constructor(
    private subtitleService: SubtitleService,
    private titleService: Title,
    private dialog: MatDialog,
    private breakpointObserver: BreakpointObserver,
    private route: ActivatedRoute,
    private authService: AuthService,
    private destroyRef: DestroyRef
  ) {
    this.titleService.setTitle('Transcription Queue');

    this.breakpointObserver
      .observe([Breakpoints.Handset])
      .subscribe(r => (this.isMobile = r.matches));

    this.updateDisplayedColumns(false);

    this.authService.user$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(user => {
        this.isAuthenticated = !!user;
        this.updateDisplayedColumns(this.isAuthenticated);
      });
  }

  ngOnInit(): void {
    this.setupAutoRefresh();
    this.route.queryParamMap.subscribe(params => {
      const newUserId = params.get('userId');
      const hasChanged = this.userIdFilter !== newUserId;
      this.userIdFilter = newUserId;

      if (hasChanged) {
        this.pageIndex = 0;
        this.expandedTasks.clear();
        this.loadTasks();
      } else if (!this.dataSource.data.length) {
        this.loadTasks();
      }
    });
  }

  private setupAutoRefresh(): void {
    interval(this.refreshIntervalMs)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.refreshTasksInProgress());
  }

  private loadTasks(append = false): void {
    this.loading = true;
    const page = this.pageIndex + 1;
    const filter = this.filterValue.trim().toLowerCase();
    this.subtitleService
      .getTasks(
        page,
        this.pageSize,
        this.sortField,
        this.sortOrder,
        filter,
        this.userIdFilter
      )
      .subscribe({
        next: res => {
          if (!append) {
            this.expandedTasks.clear();
          }
          this.dataSource.data = append
            ? this.dataSource.data.concat(res.items)
            : res.items;
          this.totalItems = res.totalCount;
          this.loading = false;
        },
        error: err => {
          console.error('Error loading tasks', err);
          this.loading = false;
        },
      });
  }

  private refreshTasksInProgress(): void {
    if (this.loading || this.refreshInProgress) {
      return;
    }

    const pagesToRefresh = this.collectPagesWithActiveTasks();
    if (!pagesToRefresh.length) {
      return;
    }

    const filter = this.filterValue.trim().toLowerCase();
    this.refreshInProgress = true;

    const requests = pagesToRefresh.map(page =>
      this.subtitleService
        .getTasks(
          page + 1,
          this.pageSize,
          this.sortField,
          this.sortOrder,
          filter,
          this.userIdFilter
        )
        .pipe(map(res => ({ page, res })))
    );

    forkJoin(requests)
      .pipe(finalize(() => {
        this.refreshInProgress = false;
      }))
      .subscribe({
        next: responses => {
          const updatedData = [...this.dataSource.data];

          const totalCountUpdate = responses.find(({ res }) => typeof res.totalCount === 'number')?.res.totalCount;
          if (typeof totalCountUpdate === 'number') {
            this.totalItems = totalCountUpdate;
          }

          responses.forEach(({ page, res }) => {
            res.items.forEach((item, itemIndex) => {
              const globalIndex = page * this.pageSize + itemIndex;
              if (globalIndex < updatedData.length && updatedData[globalIndex].id === item.id) {
                updatedData[globalIndex] = item;
              } else {
                const existingIndex = updatedData.findIndex(t => t.id === item.id);
                if (existingIndex !== -1) {
                  updatedData[existingIndex] = item;
                }
              }
            });
          });

          this.dataSource.data = updatedData;
        },
        error: err => {
          console.error('Error refreshing tasks', err);
        },
      });
  }

  private collectPagesWithActiveTasks(): number[] {
    const activePages = new Set<number>();

    this.dataSource.data.forEach((task, index) => {
      if (!this.isTaskInProgress(task)) {
        return;
      }
      activePages.add(Math.floor(index / this.pageSize));
    });

    return Array.from(activePages.values()).sort((a, b) => a - b);
  }

  private isTaskInProgress(task: YoutubeCaptionTaskDto2): boolean {
    return !task.done && task.status !== RecognizeStatus.Error;
  }

  applyFilter(evt: Event): void {
    this.filterValue = (evt.target as HTMLInputElement).value;
    this.pageIndex = 0;
    this.loadTasks();
  }

  clearFilter(input: HTMLInputElement): void {
    this.filterValue = '';
    this.pageIndex = 0;
    this.loadTasks();
    input.focus();
  }

  onSortChange(sort: Sort): void {
    this.sortField = sort.active;
    this.sortOrder = sort.direction;
    this.pageIndex = 0;
    this.loadTasks();
  }

  onScrollDown(): void {
    if (this.loading) return;
    if (this.dataSource.data.length >= this.totalItems) return;

    this.pageIndex++;
    this.loadTasks(true);
  }

  taskProgress(t: YoutubeCaptionTaskDto2): number {
    if (!t.segmentsTotal) return 0;
    return (t.segmentsProcessed / t.segmentsTotal) * 100;
  }

  isExpanded(id: string): boolean {
    return this.expandedTasks.has(id);
  }
  toggleExpand(id: string): void {
    this.isExpanded(id) ? this.expandedTasks.delete(id) : this.expandedTasks.add(id);
  }

  getStatusIcon(s: RecognizeStatus | null): string {
    switch (s) {
      case RecognizeStatus.Done:  return 'check_circle';
      case RecognizeStatus.Error: return 'cancel';
      default:                    return 'loop';
    }
  }
  getStatusClass(s: RecognizeStatus | null): string {
    switch (s) {
      case RecognizeStatus.Done:  return 'icon-status-done';
      case RecognizeStatus.Error: return 'icon-status-error';
      default:                    return 'icon-status-pending';
    }
  }
  getStatusText(s: RecognizeStatus | null | undefined): string {
    return this.subtitleService.getStatusText(s);
  }

  openVideoDialog(t: YoutubeCaptionTaskDto2): void {
    if (!this.isAuthenticated) {
      return;
    }
    const data: VideoDialogData = {
      videoId: t.id,
      title: t.title,
      channelName: t.channelName,
      channelId: t.channelId,
      uploadDate: t.uploadDate,
    };
    this.dialog.open(VideoDialogComponent, { width: '800px', data });
  }

  private updateDisplayedColumns(includeYoutube: boolean): void {
    this.displayedColumns = includeYoutube
      ? ['status', 'createdAt', 'youtube', 'title', 'channelName', 'result']
      : ['status', 'createdAt', 'title', 'channelName', 'result'];
  }

  get completedTasksCount(): number {
    return this.dataSource.data.filter(task => !!task.done).length;
  }

  get inProgressTasksCount(): number {
    return this.dataSource.data.filter(task => !task.done && task.status !== RecognizeStatus.Error).length;
  }

  get failedTasksCount(): number {
    return this.dataSource.data.filter(task => task.status === RecognizeStatus.Error).length;
  }
}
