import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

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

interface PricingPlan {
  readonly title: string;
  readonly description: string;
  readonly perks: readonly string[];
  readonly link: string;
  readonly cta: string;
}

interface TrustedCompany {
  readonly src: string;
  readonly alt: string;
}

@Component({
  selector: 'app-about3',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './about3.component.html',
  styleUrls: ['./about3.component.css'],
})
export class About3Component {
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
      title: 'Пакетная загрузка',
      description: 'Одновременно до 10 файлов, каждый размером до&nbsp;1,5 ГБ.',
    },
    {
      icon: '②',
      title: 'Экспорт файлов',
      description: 'Скачивайте DOCX, SRT и XLSX для дальнейшей работы.',
    },
    {
      icon: '③',
      title: 'Любые форматы',
      description: 'Поддерживаем MP3, MP4, WAV, FLAC, WMA, AAC, WEBM и другие форматы записи.',
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
      description:
        'Попробуйте YouScriptor бесплатно и получите 15 тестовых минут. Оплачивайте с российских и зарубежных карт или со счета организации.',
      perks: ['15 тестовых минут на старте', 'Удобная оплата картой и по счету', 'Доступ из браузера и мобильных устройств'],
      link: 'https://YouScriptor.ru/pricing',
      cta: 'Попробовать онлайн',
    },
    {
      title: 'YouScriptor On-premise',
      description:
        'Полноценная версия сервиса разворачивается на ваших серверах. Данные обрабатываются внутри инфраструктуры компании.',
      perks: ['Развертывание в частной сети', 'Работа без доступа к интернету', 'Персональная поддержка и SLA'],
      link: 'https://YouScriptor.ru/business',
      cta: 'Получить предложение',
    },
  ];
}
