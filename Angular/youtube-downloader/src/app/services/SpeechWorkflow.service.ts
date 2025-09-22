// speech-workflow.service.ts
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export enum RecognizeStatus {
  Created = 100,
  Converting = 200,
  Uploading = 210,
  Recognizing = 220,
  RetrievingResult = 230,
  SegmentingCaptions = 320,
  ApplyingPunctuationSegment = 330,
  ApplyingPunctuation = 240,
  Done = 900,
  Error = 999
}

export interface AudioWorkflowTaskDto {
  id: string;
  audioFileId: string;
  status: RecognizeStatus;
  segmentsTotal: number;
  segmentsProcessed: number;
  result?: string;
  preview?: string;
  error?: string;
  createdAt: string;
  modifiedAt: string;
}

@Injectable({
  providedIn: 'root'
})
export class SpeechWorkflowService {
  private apiUrl = '/api/AudioWorkflow';

  constructor(private http: HttpClient) {}

  /** Запустить распознавание по ID файла */
  startRecognition(fileId: string): Observable<string> {
    return this.http.post<string>(`${this.apiUrl}/${fileId}/recognize`, null, { responseType: 'text' as 'json' });
  }

  /** Получить статус задачи */
  getStatus(taskId: string): Observable<AudioWorkflowTaskDto> {
    return this.http.get<AudioWorkflowTaskDto>(`${this.apiUrl}/${taskId}`);
  }

  /** Список задач */
  listTasks(): Observable<AudioWorkflowTaskDto[]> {
    return this.http.get<AudioWorkflowTaskDto[]>(`${this.apiUrl}/tasks`);
  }
}
