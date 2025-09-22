import { Routes } from '@angular/router';
import { YoutubeDownloaderComponent } from './youtube-downloader/youtube-downloader.component';
import { RecognitionTasksComponent } from './recognition-tasks/recognition-tasks.component';
import { SubtitlesTasksComponent } from './subtitles-task/subtitles-tasks.component';
import { MarkdownConverterComponent } from './Markdown-converter/markdown-converter.component';

export const appRoutes: Routes = [
  { path: '', redirectTo: 'youtube-downloader', pathMatch: 'full' },
  { path: 'youtube-downloader', component: YoutubeDownloaderComponent },
  { path: 'recognition-tasks', component: SubtitlesTasksComponent },
  { path: 'markdown-converter', component: MarkdownConverterComponent },
  
];
