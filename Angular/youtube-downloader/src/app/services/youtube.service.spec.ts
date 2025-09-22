import { Injectable } from '@angular/core';
import { HttpClient, HttpParams, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface StreamDto {
  type: string;
  qualityLabel?: string;
  container: string;
  language?: string;
  codec: string;
  bitrate: number;
  size: number;
}

@Injectable({
  providedIn: 'root'
})
export class YoutubeService {

  private apiUrl = '/api/Youtube'; // Замените на ваш URL бэкенда

  constructor(private http: HttpClient) { }

  // Получение всех доступных потоков
  getAllStreams(videoUrlOrId: string): Observable<StreamDto[]> {
    const params = new HttpParams().set('videoUrlOrId', videoUrlOrId);
    return this.http.get<StreamDto[]>(`${this.apiUrl}/streams`, { params });
  }

  // Скачивание выбранного потока
  downloadStream(videoUrlOrId: string, stream: StreamDto): Observable<any> {
    let params = new HttpParams()
      .set('videoUrlOrId', videoUrlOrId)
      .set('type', stream.type)
      .set('container', stream.container);

    if (stream.qualityLabel) {
      params = params.set('qualityLabel', stream.qualityLabel);
    }

    if (stream.type === 'audio' && stream.language) {
      params = params.set('language', stream.language);
    }

    // Настройка заголовков для скачивания
    const headers = new HttpHeaders({
      'Content-Type': 'application/json'
    });

    // Предполагается, что бэкенд возвращает файл или ссылку на скачивание
    return this.http.post(`${this.apiUrl}/download`, null, { params, headers, responseType: 'blob' });
  }
}
