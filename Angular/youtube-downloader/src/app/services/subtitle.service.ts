import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export enum RecognizeStatus {
  Created = 100,
  Converting = 200,
  Uploading = 210,
  Recognizing = 220,
  RetrievingResult = 230,
  ApplyingPunctuation = 240,
  FetchingSubtitles = 300,
  DownloadingCaptions = 310,
  SegmentingCaptions = 320,
  ApplyingPunctuationSegment = 330,
  Done = 900,
  Error = 999
}

export enum YoutubeCaptionVisibility {
  Public = 0,
  Hidden = 1,
  Deleted = 2,
}

export interface YoutubeCaptionTaskDto {
  id: string;
  title: string | null;
  channelName: string | null;
  channelId: string | null;
  result: string | null;
  error: string | null;
  segmentsTotal: number;
  segmentsProcessed: number;
  status: RecognizeStatus | null;
  done: boolean;
  modifiedAt: string | null;
  createdAt: string | null;
  uploadDate: string | null;
}

export interface YoutubeCaptionTaskDto2 {
  id: string;
  channelId: string;
  channelName: string;
  title: string;
  slug: string;
  createdAt: string;
  uploadDate: string | null;
  status: RecognizeStatus | null;
  done: boolean;
  resultShort?: string;
  error: string;
  segmentsTotal: number;
  segmentsProcessed: number;
  visibility: YoutubeCaptionVisibility;
}

// Новая упрощённая модель для таблицы
export interface YoutubeCaptionTaskTableDto {
  channelName: string;
  createdAt: string;
  uploadDate: string | null;
  title: string;
  slug: string;
}

export interface StartSubtitleRecognitionResponse {
  taskId: string;
  remainingQuota?: number | null;
}

@Injectable({
  providedIn: 'root'
})
export class SubtitleService {
  private apiUrl = '/api/YSubtitiles';

  constructor(private http: HttpClient) {}




generatePdfFromMarkdown(id: string, markdown: string): Observable<Blob> {
    return this.http.post(`/api/generate/pdf`, { id, markdown }, { responseType: 'blob' }) as Observable<Blob>;
  }

  generateWordFromMarkdown(id: string, markdown: string): Observable<Blob> {
    return this.http.post(`/api/generate/docx`, { id, markdown }, { responseType: 'blob' }) as Observable<Blob>;
  }


  generateBbcodeFromMarkdown(id: string, markdown: string): Observable<Blob> {
  return this.http.post(`/api/generate/bbcode`, { id, markdown }, { responseType: 'blob' }) as Observable<Blob>;
}

  getStatus(taskId: string): Observable<YoutubeCaptionTaskDto> {


    return this.http.get<YoutubeCaptionTaskDto>(`${this.apiUrl}/${taskId}`);
  
  
  }

  


    /**
   * Удаляет задачу с указанным taskId.
   * @param taskId — ID или slug задачи
   */
  deleteTask(taskId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${taskId}`);
  }
  

   /**
   * Обновляет поле result для задачи с указанным taskId.
   * @param taskId — ID или slug задачи
   * @param result — новое значение result
   */
   updateResult(taskId: string, result: string): Observable<void> {
    const body = { result };
    return this.http.put<void>(`${this.apiUrl}/${taskId}/result`, body);
  }

  getAllTasks(): Observable<YoutubeCaptionTaskDto[]> {
    return this.http.get<YoutubeCaptionTaskDto[]>(`${this.apiUrl}/all`);
  }

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
      `${this.apiUrl}/start`,
      null,
      { params }
    );
  }

  getTasks(
    page: number,
    pageSize: number,
    sortField?: string,
    sortOrder?: string,
    filter?: string,
    userId?: string | null,
    includeHidden = false
  ): Observable<{ items: YoutubeCaptionTaskDto2[]; totalCount: number }> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());

    if (sortField) {
      params = params.set('sortField', sortField);
    }
    if (sortOrder) {
      params = params.set('sortOrder', sortOrder);
    }
    if (filter) {
      params = params.set('filter', filter);
    }
    if (userId) {
      params = params.set('userId', userId);
    }
    if (includeHidden) {
      params = params.set('includeHidden', 'true');
    }

    return this.http.get<{ items: YoutubeCaptionTaskDto2[]; totalCount: number }>(
      `${this.apiUrl}/GetTasks`,
      { params }
    );
  }

  updateTaskVisibility(taskId: string, visibility: YoutubeCaptionVisibility): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${taskId}/visibility`, { visibility });
  }
  
  generatePdf(taskId: string): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/GeneratePdf/${taskId}`, { responseType: 'blob' });
  }

  generateWord(taskId: string): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/GenerateWord/${taskId}`, { responseType: 'blob' });
  }

  getStatusText(status: RecognizeStatus | null | undefined): string {
    if (status == null) return 'Unknown';
    switch (status) {
      case RecognizeStatus.Created:
        return 'Set to queue';
      case RecognizeStatus.Converting:
        return 'Converting';
      case RecognizeStatus.Uploading:
        return 'Uploading';
      case RecognizeStatus.Recognizing:
        return 'Recognizing';
      case RecognizeStatus.RetrievingResult:
        return 'Retrieving Result';
      case RecognizeStatus.ApplyingPunctuation:
        return 'Applying Punctuation';
      case RecognizeStatus.FetchingSubtitles:
        return 'Fetching video info';
      case RecognizeStatus.DownloadingCaptions:
        return 'Downloading subtitles';
      case RecognizeStatus.SegmentingCaptions:
        return 'Segmenting task';
      case RecognizeStatus.ApplyingPunctuationSegment:
        return 'AI formatting process';
      case RecognizeStatus.Done:
        return 'Done';
      case RecognizeStatus.Error:
        return 'Error';
      default:
        return 'Unknown';
    }
  }

  // Новый метод, отдающий только нужные поля для таблицы
  getAllTasksTable(): Observable<YoutubeCaptionTaskTableDto[]> {
    return this.http.get<YoutubeCaptionTaskTableDto[]>(`${this.apiUrl}/GetAllTasksTable`);
  }

generateSrt(taskId: string, lang?: string): Observable<Blob> {
  let params = new HttpParams();
  if (lang) params = params.set('lang', lang);

  return this.http.get(`${this.apiUrl}/GenerateSrt/${encodeURIComponent(taskId)}`, {
    params,
    responseType: 'blob'
  });
}


}
