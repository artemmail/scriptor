import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { RouterModule } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { MatIconModule } from '@angular/material/icon';
import { SubtitleService, YoutubeCaptionTaskTableDto } from '../services/subtitle.service';
import { LocalTimePipe } from '../pipe/local-time.pipe';
import { AdminMenuComponent } from '../shared/admin-menu.component';

@Component({
  selector: 'app-service-news',
  standalone: true,
  imports: [CommonModule, RouterModule, MatIconModule, LocalTimePipe, AdminMenuComponent],
  templateUrl: './service-news.component.html',
  styleUrls: ['../shared/account-page.css', './service-news.component.css'],
})
export class ServiceNewsComponent implements OnInit {
  tasks: YoutubeCaptionTaskTableDto[] = [];
  query = '';
  loading = false;
  loadError = '';

  constructor(private readonly subtitles: SubtitleService, title: Title) {
    title.setTitle('Лента материалов — управление YouScriptor');
  }

  ngOnInit(): void { this.load(); }

  get filteredTasks(): YoutubeCaptionTaskTableDto[] {
    const query = this.query.trim().toLocaleLowerCase();
    if (!query) return this.tasks;
    return this.tasks.filter(task => [task.title, task.channelName, task.createdByName, task.createdByEmail]
      .some(value => value?.toLocaleLowerCase().includes(query)));
  }

  get authorCount(): number {
    return new Set(this.tasks.map(task => task.createdByEmail?.toLocaleLowerCase()).filter(Boolean)).size;
  }

  get channelCount(): number {
    return new Set(this.tasks.map(task => task.channelName?.toLocaleLowerCase()).filter(Boolean)).size;
  }

  load(): void {
    this.loading = true;
    this.loadError = '';
    this.subtitles.getAllTasksTable().subscribe({
      next: tasks => { this.tasks = tasks; this.loading = false; },
      error: () => { this.loading = false; this.loadError = 'Не удалось загрузить материалы. Попробуйте ещё раз.'; },
    });
  }

  onSearch(event: Event): void { this.query = (event.target as HTMLInputElement).value; }
  clearSearch(input: HTMLInputElement): void { input.value = ''; this.query = ''; input.focus(); }
  trackTask(_index: number, task: YoutubeCaptionTaskTableDto): string { return task.slug; }
}
