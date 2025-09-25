import { Routes } from '@angular/router';
import { YoutubeDownloaderComponent } from './youtube-downloader/youtube-downloader.component';
import { RecognitionTasksComponent } from './recognition-tasks/recognition-tasks.component';
import { SubtitlesTasksComponent } from './subtitles-task/subtitles-tasks.component';
import { MarkdownConverterComponent } from './Markdown-converter/markdown-converter.component';
import { OpenAiTranscriptionComponent } from './openai-transcription/openai-transcription.component';
import { OpenAiTranscriptionEditorComponent } from './openai-transcription-editor/openai-transcription-editor.component';
import { OpenAiTranscriptionMarkdownEditorComponent } from './openai-transcription-markdown-editor/openai-transcription-markdown-editor.component';

export const appRoutes: Routes = [
  { path: '', redirectTo: 'youtube-downloader', pathMatch: 'full' },
  { path: 'youtube-downloader', component: YoutubeDownloaderComponent },
  { path: 'recognition-tasks', component: SubtitlesTasksComponent },
  { path: 'transcriptions/:id/edit', component: OpenAiTranscriptionEditorComponent },
  { path: 'transcriptions/:id/markdown', component: OpenAiTranscriptionMarkdownEditorComponent },
  { path: 'transcriptions', component: OpenAiTranscriptionComponent },
  { path: 'markdown-converter', component: MarkdownConverterComponent },

];
