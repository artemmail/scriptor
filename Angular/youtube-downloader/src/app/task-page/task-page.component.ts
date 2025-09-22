import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { YoutubeCaptionTaskDto } from '../services/subtitle.service';
import { TaskProgressComponent } from '../task-progress/task-progress.component';
import { TaskResultComponent } from '../task-result/task-result.component';

// Common
import { CommonModule } from '@angular/common';
import { Title } from '@angular/platform-browser';

@Component({
  selector: 'app-task-page',
  standalone: true,
  imports: [
    CommonModule,
    TaskProgressComponent,
    TaskResultComponent
  ],
  templateUrl: './task-page.component.html',
  styleUrls: ['./task-page.component.css']
})
export class TaskPageComponent implements OnInit {
  taskId!: string;
  task: YoutubeCaptionTaskDto | null = null;
  taskDone = false;
  taskErrorMessage: string | null = null;

  constructor(private route: ActivatedRoute, private titleService: Title) {}

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      const paramId = params.get('id');
      if (paramId) {
        this.taskId = paramId;
        // Здесь можно загрузить задачу по taskId, если это необходимо
      }
    });
  }

  onTaskLoaded(task: YoutubeCaptionTaskDto) {
    this.task = task;
    if (task.title) {
      this.titleService.setTitle(task.title);
    }
  }

  onTaskDone(task: YoutubeCaptionTaskDto) {
    this.task = task;
    this.taskDone = true;
    if (task.title) {
      this.titleService.setTitle(task.title);
    }
  }

  onTaskError(msg: string) {
    console.warn('Task error:', msg);
    this.taskErrorMessage = msg;
    // Вы можете установить заголовок на значение по умолчанию или оставить прежним
    this.titleService.setTitle('Ошибка задачи');
  }
}
