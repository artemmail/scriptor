import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface BlogComment {
  id: number;
  text: string;
  userId: string;
  user: string;
  createdAt: string;
}

export interface BlogTopic {
  id: number;
  slug: string;
  header: string;
  text: string;
  userId: string;
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

  getTopicBySlug(slug: string): Observable<BlogTopic> {
    return this.http.get<BlogTopic>(`${this.apiUrl}/topics/by-slug/${slug}`);
  }

  createTopic(payload: { title: string; text: string }): Observable<BlogTopic> {
    return this.http.post<BlogTopic>(`${this.apiUrl}/topics`, payload);
  }

  updateTopic(topicId: number, payload: { title: string; text: string }): Observable<BlogTopic> {
    return this.http.put<BlogTopic>(`${this.apiUrl}/topics/${topicId}`, payload);
  }

  deleteTopic(topicId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/topics/${topicId}`);
  }

  addComment(topicId: number, payload: { text: string }): Observable<BlogComment> {
    return this.http.post<BlogComment>(`${this.apiUrl}/topics/${topicId}/comments`, payload);
  }

  updateComment(topicId: number, commentId: number, payload: { text: string }): Observable<BlogComment> {
    return this.http.put<BlogComment>(
      `${this.apiUrl}/topics/${topicId}/comments/${commentId}`,
      payload
    );
  }

  deleteComment(topicId: number, commentId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/topics/${topicId}/comments/${commentId}`);
  }
}
