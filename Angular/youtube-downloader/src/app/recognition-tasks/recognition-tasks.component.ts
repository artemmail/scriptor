// recognition-tasks.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClientModule } from '@angular/common/http';
import { RouterModule } from '@angular/router';
import { MarkdownModule } from 'ngx-markdown';
import {
  RecognitionService,
  SpeechRecognitionTaskDto,
} from '../services/recognition.service';

@Component({
  selector: 'app-recognition-tasks',
  standalone: true,
  imports: [CommonModule, HttpClientModule, MarkdownModule, RouterModule],
  templateUrl: './recognition-tasks.component.html',
  styleUrls: ['./recognition-tasks.component.css'],
})
export class RecognitionTasksComponent implements OnInit {
  tasks: SpeechRecognitionTaskDto[] = [];
  totalTasks = 0;
  completedTasks = 0;
  latestUpdate?: Date;

  /**
   * Set со списком "развёрнутых" задач.
   * Если taskId присутствует здесь, значит показываем полный текст.
   */
  expandedTasks = new Set<string>();

  constructor(private recognitionService: RecognitionService) {}

  ngOnInit(): void {
    this.loadTasks();
  }

  loadTasks(): void {
    this.recognitionService.getAllTasks().subscribe((tasks) => {
      this.tasks = tasks;
      this.totalTasks = tasks.length;
      this.completedTasks = tasks.filter((task) => task.done).length;
      this.latestUpdate = this.resolveLatestDate(tasks);
    });
  }

  /**
   * Проверяем, "развёрнут" ли текст для конкретной задачи.
   */
  isExpanded(taskId: string): boolean {
    return this.expandedTasks.has(taskId);
  }

  /**
   * Переключаем "развёрнутость" текста.
   */
  toggleExpand(taskId: string): void {
    if (this.isExpanded(taskId)) {
      this.expandedTasks.delete(taskId);
    } else {
      this.expandedTasks.add(taskId);
    }
  }

  formatLocal(dateString: string | null, format: Intl.DateTimeFormatOptions = {}): string {
    if (!dateString) {
      return '—';
    }

    let normalized = dateString;
    if (!normalized.includes('T')) {
      normalized = normalized.replace(' ', 'T');
    }
    if (!normalized.endsWith('Z') && !normalized.includes('+')) {
      normalized = `${normalized}Z`;
    }

    const date = new Date(normalized);

    if (Number.isNaN(date.getTime())) {
      return '—';
    }

    return new Intl.DateTimeFormat('ru-RU', {
      year: 'numeric',
      month: 'short',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      ...format,
    }).format(date);
  }

  private resolveLatestDate(tasks: SpeechRecognitionTaskDto[]): Date | undefined {
    const timestamps = tasks
      .map((task) => task.uploadDate || task.createdAt)
      .filter((dateString): dateString is string => !!dateString)
      .map((dateString) => {
        if (dateString.includes('T') || dateString.endsWith('Z') || dateString.includes('+')) {
          return new Date(dateString);
        }
        return new Date(`${dateString.replace(' ', 'T')}Z`);
      })
      .filter((date) => !isNaN(date.getTime()));

    if (!timestamps.length) {
      return undefined;
    }

    return new Date(Math.max(...timestamps.map((date) => date.getTime())));
  }
}
