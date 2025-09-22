import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { Router } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';

import { SubtitleService } from '../services/subtitle.service';
import { YandexAdComponent } from '../ydx-ad/yandex-ad.component';

import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';

@Component({
  selector: 'app-recognition-control',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatInputModule,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatChipsModule,
    MatDividerModule,
    YandexAdComponent
  ],
  templateUrl: './recognition-control.component.html',
  styleUrls: ['./recognition-control.component.css']
})
export class RecognitionControlComponent {
  inputValue: string = '';
  title = 'YouScriptor Транскрибация лекций в документ с разметкой (word/pdf)';

  constructor(
    private recognitionService: SubtitleService,
    private titleService: Title,
    private router: Router
  ) {
    this.titleService.setTitle(this.title);
  }

  onStart(): void {
    if (!this.inputValue.trim()) {
      return;
    }

    this.recognitionService
      .startSubtitleRecognition(this.inputValue, 'user')
      .subscribe({
        next: (taskId: string) => {
          this.router.navigate(['/recognized', taskId]);
        },
        error: (err: HttpErrorResponse) => {
          if (err.status === 401) {
            // при 401 перенаправляем на /login
            this.router.navigate(['/login']);
          } else {
            console.error('Error starting task:', err);
          }
        },
      });
  }
}
