import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { RecognitionProfile, RecognitionProfileInput } from '../models/recognition-profile.model';

@Injectable({ providedIn: 'root' })
export class RecognitionProfilesService {
  private readonly apiUrl = '/api/admin/recognition-profiles';

  constructor(private readonly http: HttpClient) {}

  list(): Observable<RecognitionProfile[]> {
    return this.http.get<RecognitionProfile[]>(this.apiUrl);
  }

  create(payload: RecognitionProfileInput): Observable<RecognitionProfile> {
    return this.http.post<RecognitionProfile>(this.apiUrl, payload);
  }

  update(id: number, payload: RecognitionProfileInput): Observable<RecognitionProfile> {
    return this.http.put<RecognitionProfile>(`${this.apiUrl}/${id}`, payload);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
