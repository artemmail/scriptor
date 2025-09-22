import { Component, OnInit } from '@angular/core';
import { MatTableDataSource } from '@angular/material/table';
import { SubtitleService, YoutubeCaptionTaskTableDto } from '../services/subtitle.service';

// Импортируем необходимые модули и кастомный pipe
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { LocalTimePipe } from '../pipe/local-time.pipe';

@Component({
  selector: 'app-service-news',
  standalone: true,
  imports: [CommonModule, RouterModule, MatTableModule, LocalTimePipe],
  templateUrl: './service-news.component.html',
  styleUrls: ['./service-news.component.css']
})
export class ServiceNewsComponent implements OnInit {
  // Столбцы: дата (createdAt), заголовок (title) и название канала (channelName)
  displayedColumns: string[] = ['createdAt', 'title', 'channelName'];
  dataSource = new MatTableDataSource<YoutubeCaptionTaskTableDto>();

  constructor(private subtitleService: SubtitleService) {}

  ngOnInit(): void {
    // Загружаем данные с помощью нового метода getAllTasksTable (без параметров)
    this.subtitleService.getAllTasksTable().subscribe(
      data => {
        this.dataSource.data = data;
      },
      error => {
        console.error('Ошибка загрузки данных:', error);
      }
    );
  }
}
