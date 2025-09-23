import { enableProdMode, importProvidersFrom } from '@angular/core';
import { bootstrapApplication, provideClientHydration, withEventReplay } from '@angular/platform-browser';
import { AppComponent } from './app/app.component';
import { environment } from './app/environments/environment';

// Модули
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { HttpClientModule, provideHttpClient, withInterceptors } from '@angular/common/http';

// Импорт роутинга
import { provideRouter, Routes } from '@angular/router';

// Ваши компоненты
import { YoutubeDownloaderComponent } from './app/youtube-downloader/youtube-downloader.component';
import { RecognitionTasksComponent } from './app/recognition-tasks/recognition-tasks.component';
import { MarkdownModule } from 'ngx-markdown';
import { RecognitionControlComponent } from './app/app-recognition-control/recognition-control.component';
import { SubtitlesTasksComponent } from './app/subtitles-task/subtitles-tasks.component';
import { TaskPageComponent } from './app/task-page/task-page.component';
import { ServiceNewsComponent } from './app/service-news/service-news.component';
import { authInterceptor } from './app/services/AuthInterceptor';
import { LoginComponent } from './app/LoginComponent/login.component';
import { AuthCallbackComponent } from './app/AuthCallbackComponent/auth-callback.component';
import { EditorPageComponent } from './app/editor-pade/editor-page.component';
import { AudioFilesComponent } from './app/audio-file/audio-files.component';
import { MarkdownConverterComponent } from './app/Markdown-converter/markdown-converter.component';
import { BlogFeedComponent } from './app/blog-feed/blog-feed.component';
import { BlogTopicCreateComponent } from './app/blog-topic-create/blog-topic-create.component';
import { BlogTopicDetailComponent } from './app/blog-topic-detail/blog-topic-detail.component';
import { RoleGuard } from './app/services/role.guard';








if (environment.production) {
  enableProdMode();
}

// Описываем маршруты
const routes: Routes = [
  { path: 'auth/callback', component: AuthCallbackComponent },


  {
    path: 'audio',
    component: AudioFilesComponent, // главная страница
  },


  {
    path: 'down',
    component: YoutubeDownloaderComponent, // главная страница
  },

  {
    path: '',
    component: RecognitionControlComponent, // главная страница
  },
  {
    path: 'y',
    component: YoutubeDownloaderComponent,
  },
  {
    path: 'recognized/:id',
    component: TaskPageComponent,
  },
  { path: 'markdown-converter/:id', component: MarkdownConverterComponent },
  { path: 'markdown-converter', component: MarkdownConverterComponent },
  {
    path: 'edit/:id',
    component: EditorPageComponent,
  },
  // Псевдоним для recognized – доступ по пути ServiceNews/Content/:id
  {
    path: 'ServiceNews/Content/:id',
    component: TaskPageComponent,
  },
  {
    path: 'tasks',
    component: SubtitlesTasksComponent, // страница со списком задач
  },
  {
    path: 'blog/new',
    component: BlogTopicCreateComponent,
    canActivate: [RoleGuard],
    data: { roles: ['Moderator'] }
  },
  {
    path: 'blog/:slug/edit',
    component: BlogTopicCreateComponent,
    canActivate: [RoleGuard],
    data: { roles: ['Moderator'] }
  },
  {
    path: 'blog/:slug',
    component: BlogTopicDetailComponent,
  },
  {
    path: 'ServiceNews',
    component: ServiceNewsComponent, // страница со списком задач
  },
  {
    path: 'blog',
    component: BlogFeedComponent,
  },
  { path: 'login', component: LoginComponent },
  { path: 'auth/callback', component: AuthCallbackComponent },

  // Любой нераспознанный путь – перенаправляем на главную страницу
  {
    path: '**',
    redirectTo: '',
    pathMatch: 'full',
  },
];

// Подключаем маршруты и остальные провайдеры
bootstrapApplication(AppComponent, {
  providers: [
    provideHttpClient(
      withInterceptors([authInterceptor])
    ),
    importProvidersFrom(MarkdownModule.forRoot()),
    provideRouter(routes),
    importProvidersFrom(BrowserAnimationsModule),
    importProvidersFrom(HttpClientModule),
    provideClientHydration(withEventReplay()),
  ]
}).catch(err => console.error(err));
