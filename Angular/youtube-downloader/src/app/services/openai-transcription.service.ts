import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export enum OpenAiTranscriptionStatus {
  Created = 0,
  Converting = 10,
  Transcribing = 20,
  Segmenting = 25,
  ProcessingSegments = 27,
  Formatting = 30,
  Done = 900,
  Error = 999,
}

export enum OpenAiTranscriptionStepStatus {
  Pending = 0,
  InProgress = 1,
  Completed = 2,
  Error = 3,
}

export interface OpenAiTranscriptionTaskDto {
  id: string;
  fileName: string;
  displayName: string;
  status: OpenAiTranscriptionStatus;
  done: boolean;
  error: string | null;
  createdAt: string;
  modifiedAt: string;
  segmentsTotal: number;
  segmentsProcessed: number;
}

export interface OpenAiTranscriptionTaskDetailsDto extends OpenAiTranscriptionTaskDto {
  recognizedText: string | null;
  processedText: string | null;
  markdownText: string | null;
  steps: OpenAiTranscriptionStepDto[];
  segments: OpenAiRecognizedSegmentDto[];
}

export interface OpenAiTranscriptionStepDto {
  id: number;
  step: OpenAiTranscriptionStatus;
  status: OpenAiTranscriptionStepStatus;
  startedAt: string;
  finishedAt: string | null;
  error: string | null;
}

export interface OpenAiRecognizedSegmentDto {
  segmentId: number;
  order: number;
  text: string;
  processedText: string | null;
  isProcessed: boolean;
  isProcessing: boolean;
  startSeconds: number | null;
  endSeconds: number | null;
}

@Injectable({ providedIn: 'root' })
export class OpenAiTranscriptionService {
  private readonly apiUrl = '/api/OpenAiTranscription';

  constructor(private readonly http: HttpClient) {}

  upload(file: File): Observable<OpenAiTranscriptionTaskDto> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    return this.http.post<OpenAiTranscriptionTaskDto>(this.apiUrl, formData);
  }

  list(): Observable<OpenAiTranscriptionTaskDto[]> {
    return this.http.get<OpenAiTranscriptionTaskDto[]>(this.apiUrl);
  }

  getTask(id: string): Observable<OpenAiTranscriptionTaskDetailsDto> {
    return this.http.get<OpenAiTranscriptionTaskDetailsDto>(`${this.apiUrl}/${id}`);
  }

  updateMarkdown(id: string, markdown: string): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${id}/markdown`, { markdown });
  }

  deleteTask(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  exportPdf(id: string): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/${id}/export/pdf`, { responseType: 'blob' });
  }

  exportDocx(id: string): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/${id}/export/docx`, { responseType: 'blob' });
  }

  exportBbcode(id: string): Observable<string> {
    return this.http.get(`${this.apiUrl}/${id}/export/bbcode`, { responseType: 'text' });
  }

  getStatusText(status: OpenAiTranscriptionStatus | null | undefined): string {
    switch (status) {
      case OpenAiTranscriptionStatus.Created:
        return 'В очереди';
      case OpenAiTranscriptionStatus.Converting:
        return 'Преобразование аудио';
      case OpenAiTranscriptionStatus.Transcribing:
        return 'Распознавание текста';
      case OpenAiTranscriptionStatus.Segmenting:
        return 'Разбиение на сегменты';
      case OpenAiTranscriptionStatus.ProcessingSegments:
        return 'Обработка сегментов';
      case OpenAiTranscriptionStatus.Formatting:
        return 'Форматирование результата';
      case OpenAiTranscriptionStatus.Done:
        return 'Готово';
      case OpenAiTranscriptionStatus.Error:
        return 'Ошибка';
      default:
        return 'Неизвестно';
    }
  }

  getStepStatusText(status: OpenAiTranscriptionStepStatus): string {
    switch (status) {
      case OpenAiTranscriptionStepStatus.Pending:
        return 'Ожидает запуска';
      case OpenAiTranscriptionStepStatus.InProgress:
        return 'В процессе';
      case OpenAiTranscriptionStepStatus.Completed:
        return 'Завершено';
      case OpenAiTranscriptionStepStatus.Error:
        return 'Ошибка';
      default:
        return 'Неизвестно';
    }
  }

  continueTask(id: string): Observable<OpenAiTranscriptionTaskDetailsDto> {
    return this.http.post<OpenAiTranscriptionTaskDetailsDto>(`${this.apiUrl}/${id}/continue`, {});
  }
}
