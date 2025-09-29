import { Routes } from '@angular/router';
import { YoutubeDownloaderComponent } from './youtube-downloader/youtube-downloader.component';
import { RecognitionTasksComponent } from './recognition-tasks/recognition-tasks.component';
import { SubtitlesTasksComponent } from './subtitles-task/subtitles-tasks.component';
import { MarkdownConverterComponent } from './Markdown-converter/markdown-converter.component';
import { OpenAiTranscriptionComponent } from './openai-transcription/openai-transcription.component';
import { BillingComponent } from './billing/billing.component';
import { AuthGuard } from './services/auth.guard';
import { AboutBusinessComponent } from './about-business/about-business.component';

export const appRoutes: Routes = [
  { path: '', redirectTo: 'youtube-downloader', pathMatch: 'full' },
  { path: 'youtube-downloader', component: YoutubeDownloaderComponent },
  { path: 'recognition-tasks', component: SubtitlesTasksComponent },
  { path: 'transcriptions', component: OpenAiTranscriptionComponent },
  { path: 'markdown-converter', component: MarkdownConverterComponent },
  { path: 'billing', component: BillingComponent, canActivate: [AuthGuard] },
  { path: 'about', component: AboutBusinessComponent },

];
