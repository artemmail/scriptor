import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface AudioFile {
  id: string;
  originalFileName: string;
  originalFilePath: string;
  convertedFileName?: string;
  convertedFilePath?: string;
  createdBy: string;
  uploadedAt: string;
}

@Injectable({
  providedIn: 'root'
})
export class AudioFileService {
  private apiUrl = '/api/AudioFiles';

  constructor(private http: HttpClient) {}

  /** Загрузить файл */
  upload(file: File): Observable<AudioFile> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<AudioFile>(this.apiUrl, form);
  }

  /** Получить список текущего пользователя */
  list(): Observable<AudioFile[]> {
    return this.http.get<AudioFile[]>(this.apiUrl);
  }

  /** Удалить файл по ID */
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
