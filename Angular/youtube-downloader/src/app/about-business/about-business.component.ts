import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

interface Highlight {
  value: string;
  label: string;
}

interface Feature {
  title: string;
  description: string;
}

interface Step {
  title: string;
  description: string;
}

interface CaseStudy {
  company: string;
  result: string;
  details: string;
}

interface FAQItem {
  question: string;
  answer: string;
}

@Component({
  selector: 'app-about-business',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './about-business.component.html',
  styleUrls: ['./about-business.component.css'],
})
export class AboutBusinessComponent {
  readonly highlights: Highlight[] = [
    {
      value: 'до 60%',
      label: 'экономия времени команд при подготовке протоколов',
    },
    {
      value: '5 минут',
      label: 'нужно, чтобы получить структурированную выжимку встречи',
    },
    {
      value: '24/7',
      label: 'доступ к истории переговоров и решениям команды',
    },
  ];

  readonly features: Feature[] = [
    {
      title: 'Автоматическая расшифровка переговоров',
      description:
        'Teamlogs превращает встречи из Zoom, Meet или офлайн в точные текстовые протоколы с распределением по спикерам и тайм-кодами.',
    },
    {
      title: 'Сводки для бизнеса',
      description:
        'Платформа выделяет ключевые договоренности, задачи и риски — достаточно отправить ссылку на запись или загрузить файл.',
    },
    {
      title: 'Единая база знаний',
      description:
        'Готовые выжимки и протоколы мгновенно доступны всей команде. Поиск работает по ключевым словам и контексту.',
    },
    {
      title: 'Безопасность корпоративного уровня',
      description:
        'Данные хранятся в соответствии с 152-ФЗ, доступ разграничивается по ролям, есть журнал действий и SSO.',
    },
  ];

  readonly steps: Step[] = [
    {
      title: 'Подключите Teamlogs к вашим источникам',
      description:
        'Интегрируйтесь с Zoom, Microsoft Teams, Google Meet или загрузите записи вручную. Поддерживаем аудио, видео и стенограммы.',
    },
    {
      title: 'Получите черновик протокола автоматически',
      description:
        'AI распознаёт речь, делит её на смысловые блоки, выделяет темы, решения и ответственных. Остаётся только утвердить.',
    },
    {
      title: 'Распространите результаты в пару кликов',
      description:
        'Отправляйте резюме встречи в корпоративные мессенджеры, CRM или почту. История хранится и пополняется автоматически.',
    },
  ];

  readonly caseStudies: CaseStudy[] = [
    {
      company: 'IT-компания, 350+ сотрудников',
      result: 'Сократили цикл согласования решений с 5 до 2 дней',
      details:
        'Вся продуктовая переписка и заседания фиксируются в Teamlogs. Руководители получают отчёт утром после встречи и сразу вносят корректировки.',
    },
    {
      company: 'Консалтинговая группа',
      result: 'Увеличили пропускную способность проектных команд на 30%',
      details:
        'Аналитики используют умные сводки как готовый черновик презентаций для клиента. Протоколы автоматически попадают в CRM.',
    },
  ];

  readonly faqs: FAQItem[] = [
    {
      question: 'Можно ли настроить выгрузку в наши системы?',
      answer:
        'Да. Teamlogs поддерживает вебхуки, REST API, а также интеграции с Slack, Telegram, Bitrix24 и корпоративной почтой.',
    },
    {
      question: 'Как обеспечена конфиденциальность данных?',
      answer:
        'Все файлы шифруются при передаче и хранении. Для корпоративных клиентов доступен выделенный контур и соглашение о неразглашении.',
    },
    {
      question: 'Какие языки распознавания поддерживаются?',
      answer:
        'Русский и английский по умолчанию. По запросу подключаем отраслевые модели и дополнительные языки.',
    },
  ];
}
