import { Component, Inject, OnDestroy } from '@angular/core';
import { CommonModule, DOCUMENT } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, RouterModule } from '@angular/router';
import { Meta, Title } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { finalize, takeUntil } from 'rxjs/operators';
import { SubtitleService } from '../services/subtitle.service';
import { AuthService } from '../services/AuthService.service';

type SourceMode = 'youtube' | 'file' | 'download';

@Component({
  selector: 'app-about3',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './about3.component.html',
  styleUrls: ['./about3.component.css'],
})
export class About3Component implements OnDestroy {
  readonly user$: AuthService['user$'];
  readonly waveform = [18, 28, 43, 25, 56, 72, 44, 28, 53, 82, 62, 40, 66, 90, 55, 32, 48, 75, 96, 62, 38, 58, 84, 47, 25, 50, 72, 38, 61, 82, 48, 29, 43, 66, 37, 20];
  readonly faqs = [
    { question: 'Как получить текст из YouTube-видео?', answer: 'Вставьте ссылку или идентификатор ролика. Сервис проверит доступные дорожки субтитров и предложит выбрать одну, если их несколько. После обработки вы получите текст, который можно читать, редактировать и экспортировать. Доступность результата зависит от доступности ролика и его субтитров.' },
    { question: 'Можно загрузить собственную запись?', answer: 'Да. В разделе «Мои расшифровки» можно загрузить аудио или видео с устройства либо указать публичную ссылку на файл Яндекс Диска. Перед запуском выберите профиль обработки и при необходимости добавьте свои указания для анализа.' },
    { question: 'Что можно сделать с готовой расшифровкой?', answer: 'Открыть её в редакторе, внести правки, скопировать текст или сохранить документ. Для собственных записей доступны Word, PDF и Markdown, а при наличии временной разметки — SRT. Результат можно повторно обработать с другим профилем аналитики.' },
    { question: 'Можно скачать только аудиодорожку с YouTube?', answer: 'Да. Загрузчик показывает доступные аудио- и видеопотоки, их качество, кодек и размер. Можно выбрать аудиодорожку отдельно или объединить выбранные аудио и видео. Набор дорожек зависит от конкретного ролика.' },
    { question: 'Как устроены тарифы и лимиты?', answer: 'Расшифровка собственных записей учитывается в минутах, обработка YouTube — в количестве видео. Актуальные пакеты, остаток лимитов и способы оплаты доступны в разделе «Тарифы и баланс» после входа.' },
  ];

  mode: SourceMode = 'youtube';
  searchValue = '';
  isStarting = false;
  startError: string | null = null;
  quotaExceeded = false;
  private readonly destroy$ = new Subject<void>();
  private readonly previousDescription: string | null;
  private readonly draftKey = 'youscriptor.landing.youtube';

  constructor(
    private readonly subtitleService: SubtitleService,
    private readonly router: Router,
    authService: AuthService,
    title: Title,
    private readonly meta: Meta,
    @Inject(DOCUMENT) private readonly document: Document,
  ) {
    this.user$ = authService.user$;
    try {
      this.searchValue = this.document.defaultView?.sessionStorage.getItem(this.draftKey) ?? '';
    } catch { /* The form also works when browser storage is disabled. */ }
    title.setTitle('YouScriptor — транскрибация YouTube и аудио в текст');
    this.previousDescription = meta.getTag('name="description"')?.content ?? null;
    meta.updateTag({ name: 'description', content: 'Превратите YouTube-видео, лекции, интервью и свои аудиозаписи в удобный текст. Редактируйте расшифровки, сохраняйте Word, PDF и Markdown, скачивайте дорожки YouTube.' });
  }

  scrollTo(id: string, event?: Event): void {
    event?.preventDefault();
    const target = this.document.getElementById(id);
    target?.scrollIntoView({ block: 'start' });
    if (id === 'main') target?.focus({ preventScroll: true });
  }

  chooseMode(mode: SourceMode): void {
    this.mode = mode;
    this.startError = null;
    this.quotaExceeded = false;
  }

  startRecognition(): void {
    const query = this.searchValue.trim();
    if (!query || this.isStarting) return;

    this.isStarting = true;
    this.startError = null;
    this.quotaExceeded = false;
    try {
      this.document.defaultView?.sessionStorage.setItem(this.draftKey, query);
    } catch { /* Browser storage is optional. */ }
    this.subtitleService.startWithTrackChoice(query)
      .pipe(takeUntil(this.destroy$), finalize(() => this.isStarting = false))
      .subscribe({
        next: response => {
          if (response.taskId) {
            try {
              this.document.defaultView?.sessionStorage.removeItem(this.draftKey);
            } catch { /* Browser storage is optional. */ }
            this.router.navigate(['/recognized', response.taskId]);
          }
        },
        error: (error: HttpErrorResponse) => {
          if (error.status === 401) {
            this.router.navigate(['/login'], { queryParams: { returnUrl: '/' } });
            return;
          }
          this.quotaExceeded = error.status === 402;
          const message = error.error?.message ?? error.error?.title;
          this.startError = typeof message === 'string'
            ? message
            : 'Не удалось начать обработку. Проверьте ссылку и попробуйте ещё раз.';
        },
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    if (this.previousDescription === null) this.meta.removeTag('name="description"');
    else this.meta.updateTag({ name: 'description', content: this.previousDescription });
  }
}
