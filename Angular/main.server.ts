import { enableProdMode, importProvidersFrom } from '@angular/core';
import { renderApplication } from '@angular/platform-server';
import { provideServerRendering } from '@angular/platform-server';
import { AppComponent } from './app/app.component';


import { provideRouter, Routes } from '@angular/router';
import { appRoutes } from './app/app.routes';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { HttpClientModule } from '@angular/common/http';
import { MarkdownModule } from 'ngx-markdown';
import { bootstrapApplication } from '@angular/platform-browser';
import { environment } from './app/enviroments/environment';
import { RecognitionControlComponent } from './app/app-recognition-control/recogintion.component';
import { YoutubeDownloaderComponent } from './app/youtube-downloader/youtube-downloader.component';
import { TaskPageComponent } from './app/task-page/task-page.component';
import { SubtitlesTasksComponent } from './app/subtitles-task/subtitles-tasks.component';

console.log("hi1");

if (environment.production) {
  console.log("hi2");
  enableProdMode();
}


const routes: Routes = [
  {
    path: '',
    component: RecognitionControlComponent, // главная страница
  },
  {
    path: 'y',
    component: YoutubeDownloaderComponent, // главная страница
  },
  { path: 'recognized/:id', component: TaskPageComponent },
  {
    path: 'tasks',
    component: SubtitlesTasksComponent, // страница со списком задач
  },
];

export function render(url: string, documentFilePath: string): Promise<string> {
  console.log("hi");

  return renderApplication(
    () => {
      console.log("bootstrapApplication called");
      return bootstrapApplication(AppComponent, {
        providers: [
          provideServerRendering(),
          provideRouter(routes),
          importProvidersFrom(BrowserAnimationsModule),
          importProvidersFrom(HttpClientModule),
          importProvidersFrom(MarkdownModule.forRoot()),
        ],
      });
    },
    {
      url,
      document: documentFilePath,
    },
  ).then(result => {
    console.log("renderApplication success");
    return result;
  }).catch(error => {
    console.error("renderApplication error:", error);
    throw error;
  });
}

