import { Injectable } from '@angular/core';
import { HttpClient, HttpParams, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { StreamDto } from '../models/stream-dto';

// src/app/models/merged-video.dto.ts
export interface MergedVideoDto {
  taskId: string;
  filePath?: string | null;
  fileName?: string | null;   // NEW
  title?: string | null;      // NEW
  youtubeId?: string | null;  // NEW
  youtubeUrl?: string | null; // опционально
  downloadUrl?: string | null;// NEW
  createdAt: string;
  status: string;
  progress: number;
}


@Injectable({
  providedIn: 'root'
})
export class YoutubeService {
  private apiUrl = '/api/Youtube'; // URL вашего бэкенда

  constructor(private http: HttpClient) {}

  // Получение всех доступных потоков
  getAllStreams(videoUrlOrId: string): Observable<StreamDto[]> {
    const params = new HttpParams().set('videoUrlOrId', videoUrlOrId);
    return this.http.get<StreamDto[]>(`${this.apiUrl}/streams`, { params });
  }

  getMergedVideos(): Observable<MergedVideoDto[]> {
  return this.http.get<MergedVideoDto[]>(`${this.apiUrl}/merged`);
}

  // Скачивание выбранного потока (отдельно)
  downloadStream(videoUrlOrId: string, stream: StreamDto): Observable<Blob> {
    let params = new HttpParams()
      .set('videoUrlOrId', videoUrlOrId)
      .set('type', stream.type)
      .set('container', stream.container);

    if (stream.qualityLabel) {
      // Если у нас qualityLabel - объект, например { label: '720p', ... }
      // нужно взять именно строку. Или предполагаем, что там уже строка.
      const qLabel = typeof stream.qualityLabel === 'object'
        ? stream.qualityLabel.label
        : stream.qualityLabel;
      params = params.set('qualityLabel', qLabel);
    }

    if (stream.type === 'audio' && stream.language) {
      params = params.set('language', stream.language);
    }

    // Настройка заголовков
    const headers = new HttpHeaders({ 'Content-Type': 'application/json' });

    // Бэкенд возвращает файл (Blob)
    return this.http.post(`${this.apiUrl}/download`, null, {
      params,
      headers,
      responseType: 'blob'
    });
  }

  // Объединение видео + нескольких аудио (через очередь)
  // Бэкенд теперь возвращает taskId, а не сразу mergedFilePath.
  mergeVideoAndAudios(
    videoUrlOrId: string,
    qualityLabel: string | null,
    container: string | null,
    audioStreams: StreamDto[]
  ): Observable<any> {
    const body = {
      videoUrlOrId,
      qualityLabel,
      container,
      audioStreams
    };

    // POST /api/Youtube/merge
    // Предполагается, что бэкенд возвращает { taskId, message }
    return this.http.post(`${this.apiUrl}/merge`, body, {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' })
    });
  }

  // Новый метод: запрос прогресса для taskId
  getProgress(taskId: string): Observable<any> {
    // Предполагаем, что бэкенд реализовал GET /api/Youtube/progress/{taskId}
    // и возвращает { status, progress, error }
    return this.http.get<any>(`${this.apiUrl}/progress/${taskId}`);
  }

  downloadMergedResult(taskId: string): Observable<Blob> {
    // GET /api/Youtube/downloadResult/{taskId}
    // Хотим получить Blob (файл)
    return this.http.get(`${this.apiUrl}/downloadResult/${taskId}`, {
      responseType: 'blob'
    });
  }
}
