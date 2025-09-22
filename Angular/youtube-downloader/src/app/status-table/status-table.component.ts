// status-table.component.ts
import { Component, OnChanges, Input, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { RecognitionService, SpeechRecognitionTaskDto } from '../services/recognition.service';

@Component({
  selector: 'app-status-table',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatTableModule],
  template: `
    <mat-card>
      <mat-card-title>Статус задачи распознавания</mat-card-title>
      <mat-card-content>
        <div *ngIf="isLoading">Загрузка данных...</div>
        <div *ngIf="!isLoading && errorMessage">{{ errorMessage }}</div>
        <div *ngIf="!isLoading && !errorMessage && taskData">
          <table mat-table [dataSource]="[taskData]" class="mat-elevation-z8">
            <!-- Определение столбцов -->
            <ng-container matColumnDef="id">
              <th mat-header-cell *matHeaderCellDef> ID </th>
              <td mat-cell *matCellDef="let element"> {{element.id}} </td>
            </ng-container>
            <ng-container matColumnDef="status">
              <th mat-header-cell *matHeaderCellDef> Статус </th>
              <td mat-cell *matCellDef="let element"> {{element.status}} </td>
            </ng-container>
            <ng-container matColumnDef="done">
              <th mat-header-cell *matHeaderCellDef> Выполнено </th>
              <td mat-cell *matCellDef="let element"> {{element.done}} </td>
            </ng-container>
            <ng-container matColumnDef="createdAt">
              <th mat-header-cell *matHeaderCellDef> Дата создания </th>
              <td mat-cell *matCellDef="let element"> {{element.createdAt}} </td>
            </ng-container>
            <ng-container matColumnDef="createdBy">
              <th mat-header-cell *matHeaderCellDef> Автор </th>
              <td mat-cell *matCellDef="let element"> {{element.createdBy}} </td>
            </ng-container>
            <ng-container matColumnDef="result">
              <th mat-header-cell *matHeaderCellDef> Результат </th>
              <td mat-cell *matCellDef="let element"> {{element.result}} </td>
            </ng-container>
            <ng-container matColumnDef="error">
              <th mat-header-cell *matHeaderCellDef> Ошибка </th>
              <td mat-cell *matCellDef="let element"> {{element.error}} </td>
            </ng-container>
            <ng-container matColumnDef="youtubeId">
              <th mat-header-cell *matHeaderCellDef> YouTube ID </th>
              <td mat-cell *matCellDef="let element"> {{element.youtubeId}} </td>
            </ng-container>
            <ng-container matColumnDef="language">
              <th mat-header-cell *matHeaderCellDef> Язык </th>
              <td mat-cell *matCellDef="let element"> {{element.language}} </td>
            </ng-container>
  
            <!-- Заголовок и строка данных -->
            <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
          </table>
        </div>
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    table { width: 100%; }
    mat-card { margin: 20px; }
  `]
})
export class StatusTableComponent implements OnChanges {
  @Input() taskId: string = '';

  displayedColumns: string[] = [
    'id',
    'status',
    'done',
    'createdAt',
    'createdBy',
    'result',
    'error',
    'youtubeId',
    'language'
  ];
  taskData!: SpeechRecognitionTaskDto;
  isLoading = false;
  errorMessage = '';

  constructor(private recognitionService: RecognitionService) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['taskId'] && this.taskId) {
      this.fetchTaskStatus(this.taskId);
    }
  }

  fetchTaskStatus(taskId: string): void {
    this.isLoading = true;
    this.errorMessage = '';
    this.recognitionService.getStatus(taskId).subscribe({
      next: (data) => {
        this.taskData = data;
        this.isLoading = false;
      },
      error: (err) => {
        console.error('Ошибка при получении статуса задачи:', err);
        this.errorMessage = 'Ошибка загрузки данных задачи.';
        this.isLoading = false;
      }
    });
  }
}
