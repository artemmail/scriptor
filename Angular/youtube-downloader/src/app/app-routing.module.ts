import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { YoutubeDownloaderComponent } from './youtube-downloader/youtube-downloader.component';
import { RecognitionTasksComponent } from './recognition-tasks/recognition-tasks.component';
import { OpenAiTranscriptionComponent } from './openai-transcription/openai-transcription.component';
import { BillingComponent } from './billing/billing.component';
import { AuthGuard } from './services/auth.guard';
import { AboutBusinessComponent } from './about-business/about-business.component';

const routes: Routes = [
  { path: '', redirectTo: 'youtube-downloader', pathMatch: 'full' },
  { path: 'youtube-downloader', component: YoutubeDownloaderComponent },
  { path: 'recognition-tasks', component: RecognitionTasksComponent },
  { path: 'transcriptions', component: OpenAiTranscriptionComponent },
  { path: 'billing', component: BillingComponent, canActivate: [AuthGuard] },
  { path: 'about', component: AboutBusinessComponent },
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]

})
export class AppRoutingModule {}
