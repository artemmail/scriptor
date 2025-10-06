// editor-page.component.ts
import { Component, OnInit }            from '@angular/core';
import { ActivatedRoute, Router }       from '@angular/router';
import { Title }                        from '@angular/platform-browser';
import { CommonModule }                 from '@angular/common';
import { FormsModule }                  from '@angular/forms';
import { MatIconModule }                from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { LMarkdownEditorModule }        from 'ngx-markdown-editor';
import { MatCardModule }                from '@angular/material/card';

import { TaskProgressComponent }        from '../task-progress/task-progress.component';
import {
  SubtitleService,
  YoutubeCaptionTaskDto
} from '../services/subtitle.service';
import { MarkdownRendererService1 }     from '../task-result/markdown-renderer.service';

@Component({
  selector: 'app-editor-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    LMarkdownEditorModule,
    MatCardModule,
    TaskProgressComponent,
    MatIconModule,
    MatSnackBarModule
  ],
  templateUrl: './editor-page.component.html',
  styleUrls: ['./editor-page.component.css']
})
export class EditorPageComponent implements OnInit {
  taskId!: string;
  markdownContent = '';
  loading = true;
  errorMessage: string | null = null;
  task: YoutubeCaptionTaskDto | null = null;

  get hasContent(): boolean {
    return !!this.markdownContent?.trim();
  }

  // Опции редактора
  editorOptions = {
    placeholder: 'Пишите Markdown и LaTeX: $…$ или $$…$$',
    katex: true,
    theme: 'github',
    lineNumbers: true,
    dragDrop: true,
    showPreviewPanel: true,
    hideIcons: []
  };

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private titleService: Title,
    private subtitleService: SubtitleService,
    private snackBar: MatSnackBar,
    private mk: MarkdownRendererService1
  ) {
    // Привязка метода рендеринга
    this.renderWithMath = this.renderWithMath.bind(this);
  }

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      const id = params.get('id');
      if (id) {
        this.taskId = id;
      }
    });
  }

  // Рендеринг LaTeX
  renderWithMath(content: string): string {
    return this.mk.renderMath(content);
  }

  onTaskLoaded(task: YoutubeCaptionTaskDto) {
    this.task = task;
  }

  onTaskDone(task: YoutubeCaptionTaskDto) {
    this.task = task;
    this.taskId = task.id;
    this.markdownContent = task.result || '';
    this.loading = false;
    this.titleService.setTitle(
      `Редактирование: ${task.title || this.taskId}`
    );
  }

  onTaskError(msg: string) {
    this.errorMessage = msg;
    this.loading = false;
    this.titleService.setTitle('Ошибка загрузки');
  }

  onDownloadMd() {
    if (!this.markdownContent) return;
    const blob = new Blob([this.markdownContent], { type: 'text/markdown' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${this.task?.title || this.taskId}.md`;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  onSave(): void {
    if (!this.markdownContent.trim()) return;

    this.subtitleService
      .updateResult(this.taskId, this.markdownContent)
      .subscribe({
        next: () => {
          this.snackBar.open('Сохранено успешно', '', { duration: 2000 });
        },
        error: err => {
          console.error(err);
          this.snackBar.open('Ошибка при сохранении', 'OK', { duration: 3000 });
        }
      });
  }

  onDelete(): void {
    if (!confirm('Вы действительно хотите удалить эту задачу?')) {
      return;
    }
    this.subtitleService.deleteTask(this.taskId)
      .subscribe({
        next: () => {
          this.snackBar.open('Задача удалена', '', { duration: 2000 });
          this.router.navigate(['/tasks']);
        },
        error: err => {
          console.error(err);
          this.snackBar.open('Ошибка при удалении', 'OK', { duration: 3000 });
        }
      });
  }
}
