import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { YoutubeDownloaderComponent } from './youtube-downloader/youtube-downloader.component';
import { RecognitionTasksComponent } from './recognition-tasks/recognition-tasks.component';
import { OpenAiTranscriptionComponent } from './openai-transcription/openai-transcription.component';
import { BillingComponent } from './billing/billing.component';
import { AuthGuard } from './services/auth.guard';
import { RoleGuard } from './services/role.guard';
import { AdminUsersComponent } from './admin-users/admin-users.component';
import { AboutBusinessComponent } from './about-business/about-business.component';
import { About1Component } from './about1/about1.component';
import { About2Component } from './about2/about2.component';
import { About3Component } from './about3/about3.component';

const routes: Routes = [
  { path: '', component: YoutubeDownloaderComponent },
  { path: 'youtube-downloader', component: YoutubeDownloaderComponent },
  { path: 'recognition-tasks', component: RecognitionTasksComponent },
  { path: 'transcriptions', component: OpenAiTranscriptionComponent },
  { path: 'billing', component: BillingComponent, canActivate: [AuthGuard] },
  {
    path: 'admin/users',
    component: AdminUsersComponent,
    canActivate: [RoleGuard],
    data: { roles: ['Admin'] }
  },
  { path: 'about', component: AboutBusinessComponent },
  { path: 'about1', component: About1Component },
  { path: 'about2', component: About2Component },
  { path: 'about3', component: About3Component },
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]

})
export class AppRoutingModule {}
