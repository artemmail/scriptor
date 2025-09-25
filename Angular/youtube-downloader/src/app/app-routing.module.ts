import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { YoutubeDownloaderComponent } from './youtube-downloader/youtube-downloader.component';
import { RecognitionTasksComponent } from './recognition-tasks/recognition-tasks.component';
import { OpenAiTranscriptionComponent } from './openai-transcription/openai-transcription.component';
import { OpenAiTranscriptionEditorComponent } from './openai-transcription-editor/openai-transcription-editor.component';

const routes: Routes = [
  { path: '', redirectTo: 'youtube-downloader', pathMatch: 'full' },
  { path: 'youtube-downloader', component: YoutubeDownloaderComponent },
  { path: 'recognition-tasks', component: RecognitionTasksComponent },
  { path: 'transcriptions/:id/edit', component: OpenAiTranscriptionEditorComponent },
  { path: 'transcriptions', component: OpenAiTranscriptionComponent },
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]

})
export class AppRoutingModule {}
