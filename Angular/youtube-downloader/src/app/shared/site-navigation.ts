export interface SiteLink {
  label: string;
  path: string;
  description: string;
  icon: string;
  fragment?: string;
  role?: string;
}

export interface SiteLinkGroup {
  id: string;
  label: string;
  title: string;
  links: readonly SiteLink[];
}

export const UTILITY_LINKS: readonly SiteLink[] = [
  { label: 'Конвертер Markdown', path: '/markdown-converter', icon: 'article', description: 'Оформите текст и сохраните его в Word, PDF или HTML.' },
  { label: 'PNG → WebP', path: '/png-to-webp', icon: 'image', description: 'Настройте качество, обрежьте изображение и сравните результат.' },
  { label: 'Пакетный PNG → WebP', path: '/png-to-webp/batch', icon: 'photo_library', description: 'Преобразуйте подборку изображений и скачайте всё одним ZIP.' },
];

export const SITE_LINK_GROUPS: readonly SiteLinkGroup[] = [
  { id: 'work', label: 'Работа', title: 'От записи — к результату', links: [
    { label: 'Мои записи', path: '/transcriptions', icon: 'graphic_eq', description: 'Загрузка аудио и видео, расшифровка и анализ.' },
    { label: 'Скрипторий', path: '/Scriptorium', icon: 'local_library', description: 'YouTube в текст и библиотека расшифровок.' },
    { label: 'Загрузчик YouTube', path: '/down', icon: 'download', description: 'Аудиодорожки и видео в подходящем качестве.' },
  ] },
  { id: 'tools', label: 'Утилиты', title: 'Небольшие инструменты. Полезный результат.', links: UTILITY_LINKS },
  { id: 'about', label: 'О сервисе', title: 'Познакомьтесь с YouScriptor', links: [
    { label: 'Возможности', path: '/', fragment: 'features', icon: 'auto_awesome', description: 'Что можно сделать со своими записями.' },
    { label: 'Постобработка', path: '/', fragment: 'postprocessing', icon: 'tune', description: 'Из разговора — в готовый материал.' },
    { label: 'Как это работает', path: '/', fragment: 'workflow', icon: 'route', description: 'Три шага от источника к документу.' },
    { label: 'Вопросы и ответы', path: '/', fragment: 'faq', icon: 'help_outline', description: 'Всё перед первой записью.' },
  ] },
];

export const MANAGEMENT_LINKS: readonly SiteLink[] = [
  { label: 'Лента материалов', path: '/ServiceNews', icon: 'feed', description: 'Готовые расшифровки и их авторы.', role: 'admin' },
  { label: 'Пользователи', path: '/admin/users', icon: 'people_outline', description: 'Аккаунты и роли.', role: 'admin' },
  { label: 'Платежи', path: '/admin/payments', icon: 'payments', description: 'Оплаты и операции YooMoney.', role: 'admin' },
  { label: 'Тарифные планы', path: '/admin/billing-plans', icon: 'sell', description: 'Пакеты и стоимость.', role: 'admin' },
  { label: 'Профили обработки', path: '/admin/recognition-profiles', icon: 'tune', description: 'Настройки распознавания и анализа.', role: 'admin' },
  { label: 'Новая публикация', path: '/blog/new', icon: 'edit_note', description: 'Создать материал для блога.', role: 'moderator' },
];
