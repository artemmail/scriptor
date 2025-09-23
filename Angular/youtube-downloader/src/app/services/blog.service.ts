import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface BlogComment {
  id: number;
  text: string;
  user: string;
  createdAt: string;
}

export interface BlogTopic {
  id: number;
  slug: string;
  header: string;
  text: string;
  user: string;
  createdAt: string;
  commentCount: number;
  comments: BlogComment[];
}

@Injectable({ providedIn: 'root' })
export class BlogService {
  private readonly apiUrl = '/api/blog';

  constructor(private http: HttpClient) {}

  getTopics(skip: number, take: number): Observable<BlogTopic[]> {
    const params = new HttpParams()
      .set('skip', skip)
      .set('take', take);

    return this.http.get<BlogTopic[]>(`${this.apiUrl}/topics`, { params });
  }

  createTopic(payload: { title: string; text: string }): Observable<BlogTopic> {
    return this.http.post<BlogTopic>(`${this.apiUrl}/topics`, payload);
  }

  addComment(topicId: number, payload: { text: string }): Observable<BlogComment> {
    return this.http.post<BlogComment>(`${this.apiUrl}/topics/${topicId}/comments`, payload);
  }
}
