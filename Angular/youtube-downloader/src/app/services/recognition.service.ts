import { Injectable } from '@angular/core';
import { HttpClient, HttpParams, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { StartSubtitleRecognitionResponse } from './subtitle.service';

export interface SpeechRecognitionTaskDto {
  id: string;
  status: string | null;
  done: boolean;
  createdAt: string | null;
  uploadDate: string | null;
  createdBy: string | null;
  result: string | null;
  error: string | null;
  youtubeId: string | null;
  language: string | null;
}

@Injectable({
  providedIn: 'root'
})
export class RecognitionService {
  private apiUrl = '/api/Recognition'; // URL вашего API

  constructor(private http: HttpClient) {}

  /**
   * Получение статуса задачи по ID.
   * @param taskId ID задачи.
   */
  getStatus(taskId: string): Observable<SpeechRecognitionTaskDto> {
    return this.http.get<SpeechRecognitionTaskDto>(`${this.apiUrl}/${taskId}`);
  }

  /**
   * Получение списка всех задач.
   */
  getAllTasks(): Observable<SpeechRecognitionTaskDto[]> {
    return this.http.get<SpeechRecognitionTaskDto[]>(`${this.apiUrl}/all`);
  }

  /**
   * Создание задачи для распознавания речи.
   * @param filePath Путь к файлу.
   * @param user Имя пользователя.
   */
  startRecognition(filePath: string, user: string): Observable<string> {
    const params = new HttpParams()
      .set('filePath', filePath)
      .set('user', user);

    return this.http.post<string>(`${this.apiUrl}/start`, null, { params });
  }

  /**
   * Создание задачи для распознавания субтитров.
   * @param youtubeId ID видео на YouTube.
   * @param language Язык субтитров.
   * @param createdBy Автор задачи (опционально).
   */
  startSubtitleRecognition(
    youtubeId: string,
    language?: string,
    createdBy: string = 'system'
  ): Observable<StartSubtitleRecognitionResponse> {
    const params = new HttpParams()
      .set('youtubeId', youtubeId)
      .set('language', language || '')
      .set('createdBy', createdBy);

    return this.http.post<StartSubtitleRecognitionResponse>(
      `${this.apiUrl}/start-subtitle-recognition`,
      null,
      { params }
    );
  }
}
