// recognition-tasks.component.ts
import { Component, OnInit, ViewChild } from '@angular/core';
import { MatTableDataSource } from '@angular/material/table';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { CommonModule } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { HttpClientModule } from '@angular/common/http';
import {
  RecognitionService,
  SpeechRecognitionTaskDto,
} from '../services/recognition.service';
import { MarkdownModule } from 'ngx-markdown';
import { RouterModule } from '@angular/router';
import { LocalTimePipe } from '../pipe/local-time.pipe';

@Component({
  selector: 'app-recognition-tasks',
  standalone: true,
  imports: [
    LocalTimePipe,
    CommonModule,
    MatTableModule,
    MatPaginatorModule,
    MatSortModule,
    HttpClientModule,
    MarkdownModule,
    RouterModule, // для routerLink
  ],
  templateUrl: './recognition-tasks.component.html',
  styleUrls: ['./recognition-tasks.component.css'],
})
export class RecognitionTasksComponent implements OnInit {
  displayedColumns: string[] = [
    'status',
    'done',
    'createdAt',
    'language',
    'result',
  ];
  dataSource = new MatTableDataSource<SpeechRecognitionTaskDto>();

  /**
   * Set со списком "развёрнутых" задач.
   * Если taskId присутствует здесь, значит показываем полный текст.
   */
  expandedTasks = new Set<string>();

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  constructor(private recognitionService: RecognitionService) {}

  ngOnInit(): void {
    this.loadTasks();
  }

  loadTasks(): void {
    this.recognitionService.getAllTasks().subscribe((tasks) => {
      this.dataSource.data = tasks;
      this.dataSource.paginator = this.paginator;
      this.dataSource.sort = this.sort;
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
      // Если уже развёрнут, сворачиваем
      this.expandedTasks.delete(taskId);
    } else {
      // Если свёрнут, разворачиваем
      this.expandedTasks.add(taskId);
    }
  }
}
