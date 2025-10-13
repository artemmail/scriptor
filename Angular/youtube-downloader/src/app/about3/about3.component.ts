import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { MatExpansionModule } from '@angular/material/expansion';
import { SubtitleService } from '../services/subtitle.service';
import { YandexAdComponent } from '../ydx-ad/yandex-ad.component';

interface WorkflowStep {
  readonly title: string;
  readonly description: string;
  readonly image: string;
}

interface GalleryItem {
  readonly title: string;
  readonly image: string;
  readonly alt: string;
}

interface AdvantageItem {
  readonly icon: string;
  readonly title: string;
  readonly description: string;
}

interface BusinessFeature {
  readonly title: string;
  readonly description: string;
  readonly image: string;
}

type RouterCommand = string | string[];

interface PricingPlan {
  readonly title: string;
  readonly description: string;
  readonly perks: readonly string[];
  readonly link: RouterCommand;
  readonly cta: string;
}

interface TrustedCompany {
  readonly src: string;
  readonly alt: string;
}

interface FaqItem {
  readonly question: string;
  readonly answer: string;
}

@Component({
  selector: 'app-about3',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MatExpansionModule, YandexAdComponent],
  templateUrl: './about3.component.html',
  styleUrls: ['./about3.component.css'],
})
export class About3Component {
  constructor(
    private readonly subtitleService: SubtitleService,
    private readonly router: Router,
  ) {}

  readonly uploadRoute: RouterCommand = '/transcriptions';
  readonly trialRoute: RouterCommand = '/billing';

  searchValue = '';
  isStarting = false;
  startError: string | null = null;

  readonly trustedCompanies: readonly TrustedCompany[] = [
    {
      src: 'assets/about3/YouScriptor/avito-seeklogocom_12.png',
      alt: 'Логотип Авито — клиент сервиса расшифровки аудио и видео в текст',
    },
    {
      src: 'assets/about3/YouScriptor/VK_Full_Logo_12x.png',
      alt: 'Логотип ВКонтакте — клиент сервиса расшифровки аудио и видео в текст',
    },
    {
      src: 'assets/about3/YouScriptor/beeline_logo.png',
      alt: 'Логотип Билайн — клиент сервиса расшифровки аудио и видео в текст',
    },
    {
      src: 'assets/about3/YouScriptor/Samok_12x.png',
      alt: 'Логотип Самокат — клиент сервиса расшифровки аудио и видео в текст',
    },
    {
      src: 'assets/about3/YouScriptor/Lenta_New_Logo_22x.png',
      alt: 'Логотип Лента — клиент сервиса расшифровки аудио и видео в текст',
    },
    {
      src: 'assets/about3/YouScriptor/Skyeng_Base_12x.png',
      alt: 'Логотип Skyeng — клиент сервиса расшифровки аудио и видео в текст',
    },
    {
      src: 'assets/about3/YouScriptor/Skillbox_12x.png',
      alt: 'Логотип Skillbox — клиент сервиса расшифровки аудио и видео в текст',
    },
    {
      src: 'assets/about3/YouScriptor/CIAN_BIG 1.png',
      alt: 'Логотип Циан — клиент сервиса расшифровки аудио и видео в текст',
    },
    {
      src: 'assets/about3/YouScriptor/Skolkovo_Foundation_.png',
      alt: 'Логотип Сколково — клиент сервиса расшифровки аудио и видео в текст',
    },
    {
      src: 'assets/about3/YouScriptor/logo-sber.png',
      alt: 'Логотип Сбер — клиент сервиса расшифровки аудио и видео в текст',
    },
    {
      src: 'assets/about3/YouScriptor/Untitled_12x.png',
      alt: 'Логотип РБК — клиент сервиса расшифровки аудио и видео в текст',
    },
  ];

  readonly workflowSteps: readonly WorkflowStep[] = [
    {
      title: 'Загрузите файл',
      description:
        'Загрузите или перетащите файл в указанную область. Чем лучше качество аудио, тем понятнее будет итоговая расшифровка.',
      image: 'assets/about3/YouScriptor/upload_frame-600x.png',
    },
    {
      title: 'Дождитесь окончания расшифровки',
      description:
        'Сервису нужно 3−5% времени от длительности записи, чтобы перевести ваше аудио в текст.',
      image: 'assets/about3/YouScriptor/files_frame-600x.png',
    },
    {
      title: 'Редактируйте прямо в браузере',
      description:
        'Проверьте расшифровку, прослушайте фрагменты и внесите правки через встроенный онлайн-редактор.',
      image: 'assets/about3/YouScriptor/transcript_frame-600x.png',
    },
    {
      title: 'Скачайте результат',
      description:
        'Сохраните результат на устройство в формате DOCX, XLSX или SRT и поделитесь с коллегами.',
      image: 'assets/about3/YouScriptor/export_frame-600x.png',
    },
  ];

  readonly transcriptionFeatures: readonly string[] = [
    'Расшифровка речи',
    'Высокая точность распознавания',
    'Скорость обработки — 1 час за 3 минуты',
    'Расстановка знаков препинания',
    'Разделение на спикеров',
    'Тайм-коды',
    '78 языков',
  ];

  readonly editorBlocks: readonly GalleryItem[] = [
    {
      title: 'Прослушивайте материал',
      image: 'assets/about3/YouScriptor/transcript_player-600x.png',
      alt: 'Онлайн-редактор с возможностью прослушивания аудио во время правки текста расшифровки',
    },
    {
      title: 'Выделяйте важные моменты',
      image: 'assets/about3/YouScriptor/transcript_formatter-600x.png',
      alt: 'Инструменты форматирования и выделения текста цветным маркером в редакторе расшифровки',
    },
    {
      title: 'Подписывайте спикеров',
      image: 'assets/about3/YouScriptor/transcript_speakers-600x.png',
      alt: 'Точная разметка диалога с удобной сменой и переименованием спикеров',
    },
  ];

  readonly aiFeatures: readonly string[] = [
    'Как ChatGPT, но для ваших расшифровок',
    'Ответит на вопросы по расшифровке',
    'Мгновенно подготовит резюме встречи',
    'Сделает контент на основе расшифровки — от статьи до постов в соцсетях',
  ];

  readonly YouScriptorFeatures: readonly AdvantageItem[] = [
    {
      icon: '①',
      title: 'Вайб-рекрутинг',
      description: 'Загружайте видео запись собеседования и получите автоматический отчет об опыте, hard- и soft- скилах кандидата, его красных флагах.',
    },
    {
      icon: '②',
      title: 'Вайб-джобхантинг',
      description: 'Получите анализ токсичности компании, риски выгорания, карьерных перспектив, что может скрывать работодатель и ваши навыки самопрезентации.',
    },
    {
      icon: '③',
      title: 'Вайб-менеджмент',
      description: 'Анализируйте записи рабочих совезаний и получайте отчет о ходе работы команды. Что сделано, где пробуксовка. Анализ переговоров с партнерами, коммерческих предложений',
    },
  ];

  readonly businessFeatures: readonly BusinessFeature[] = [
    {
      title: 'Командная работа',
      description: 'Создайте рабочее пространство в YouScriptor и пригласите коллег для совместной работы.',
      image: 'assets/about3/YouScriptor/workspace_card-471x.png',
    },
    {
      title: 'Общий доступ к файлам',
      description: 'Настройте уровни доступа и делитесь ссылками на расшифровки внутри команды.',
      image: 'assets/about3/YouScriptor/files_sharing-572x.png',
    },
    {
      title: 'Детализация расходов',
      description: 'Отслеживайте баланс минут и количество загруженных файлов в реальном времени.',
      image: 'assets/about3/YouScriptor/workspace_analytics-572x.png',
    },
    {
      title: 'Доступна интеграция по API',
      description: 'Интегрируйте распознавание речи в свои сервисы через простой REST API.',
      image: 'assets/about3/YouScriptor/api_integration-560x.png',
    },
  ];

  readonly pricingPlans: readonly PricingPlan[] = [
    {
      title: 'YouScriptor Online',
      description: 'Попробуйте YouScriptor бесплатно и получите 15 тестовых минут. Оплачивайте с российских и зарубежных карт или со счета организации.',
      perks: ['15 тестовых минут на старте', 'Удобная оплата картой и по счету', 'Доступ из браузера и мобильных устройств'],
      link: this.trialRoute,
      cta: 'Попробовать онлайн',
    },
    {
      title: 'YouScriptor On-premise',
      description:
        'Полноценная версия сервиса разворачивается на ваших серверах. Данные обрабатываются внутри инфраструктуры компании.',
      perks: ['Развертывание в частной сети', 'Работа без доступа к интернету', 'Персональная поддержка и SLA'],
      link: '/about',
      cta: 'Получить предложение',
    },
  ];

  readonly faqs: readonly FaqItem[] = [
    {
      question: 'Как преобразовать видео в текст?',
      answer:
        'Загрузите ваш видеоролик в YouScriptor — сервис преобразует его в текст с высокой точностью. Сразу после обработки вы сможете редактировать транскрипт и экспортировать результат в DOCX, XLSX или SRT.',
    },
    {
      question: 'Можно ли транскрибировать аудио в текст?',
      answer:
        'Да. Мы поддерживаем популярные аудиоформаты, включая MP3, WAV, M4A, FLAC и другие. Просто перетащите файлы в загрузчик — обработка займёт всего несколько минут.',
    },
    {
      question: 'Какие форматы файлов принимает YouScriptor?',
      answer:
        'Сервис работает с аудио- и видеоформатами MP3, MP4, WAV, MOV, AVI, M4A, WEBM и десятками других расширений. Если сомневаетесь, загрузите файл — мы автоматически проверим его совместимость.',
    },
    {
      question: 'Можно ли получить тестовый доступ к сервису?',
      answer:
        'Новые пользователи получают 15 тестовых минут для оценки качества распознавания. Этого достаточно, чтобы загрузить несколько файлов, попробовать редактор и посмотреть экспорт.',
    },
    {
      question: 'Как подключить сервис со счета юридического лица?',
      answer:
        'Оформите счёт в личном кабинете и оплатите его от имени компании. После оплаты баланс минут пополнится автоматически, а закрывающие документы будут доступны в разделе «Биллинг».',
    },
    {
      question: 'Сгорают ли минуты при приобретении пакетов?',
      answer:
        'Минуты списываются только за фактически обработанные материалы. Вы можете пополнять баланс пакетами и расходовать их постепенно всей командой.',
    },
    {
      question: 'Какую поддержку я получу от сервиса?',
      answer:
        'Команда YouScriptor помогает на каждом этапе — от подключения и настройки командного доступа до интеграции по API. Пишите нам в чат поддержки или на почту, и мы ответим в течение рабочего дня.',
    },
    {
      question: 'Как создать корпоративный аккаунт в YouScriptor?',
      answer:
        'Зарегистрируйтесь на платформе и создайте рабочее пространство. Пригласите коллег по email, настройте роли и уровни доступа, а затем распределите минуты между участниками.',
    },
  ];

  startRecognition(): void {
    const query = this.searchValue.trim();
    if (!query || this.isStarting) {
      return;
    }

    this.isStarting = true;
    this.startError = null;

    this.subtitleService.startSubtitleRecognition(query, 'user').subscribe({
      next: (taskId: string) => {
        this.isStarting = false;
        this.router.navigate(['/recognized', taskId]);
      },
      error: (err: HttpErrorResponse) => {
        this.isStarting = false;

        if (err.status === 401) {
          this.router.navigate(['/login']);
          return;
        }

        console.error('Не удалось запустить распознавание', err);
        this.startError = 'Не удалось запустить распознавание. Попробуйте ещё раз позже.';
      },
    });
  }
}
