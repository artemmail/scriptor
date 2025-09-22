// ya-metrika.service.ts
import { Injectable } from '@angular/core';

declare var ym: any;

@Injectable({
  providedIn: 'root'
})
export class YaMetrikaService {

  private counterId: number = 99571329; // Ваш ID счетчика

  constructor() { }

  /**
   * Отправляет просмотр страницы
   * @param path Путь страницы
   * @param title Заголовок страницы (опционально)
   */
  hit(path: string, title?: string): void {
    if (typeof ym === 'function') {
      console.warn('hit');
      ym(this.counterId, 'hit', path, { title });
    } else {
      console.warn('Яндекс.Метрика не инициализирована');
    }
  }
}
