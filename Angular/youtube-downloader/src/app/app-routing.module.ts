import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { YoutubeDownloaderComponent } from './youtube-downloader/youtube-downloader.component';
import { OpenAiTranscriptionComponent } from './openai-transcription/openai-transcription.component';
import { BillingComponent } from './billing/billing.component';
import { AuthGuard } from './services/auth.guard';
import { RoleGuard } from './services/role.guard';
import { AdminUsersComponent } from './admin-users/admin-users.component';
import { AdminRecognitionProfilesComponent } from './admin-recognition-profiles/admin-recognition-profiles.component';
import { About3Component } from './about3/about3.component';
import { PngToWebpComponent } from './png-to-webp/png-to-webp.component';

const routes: Routes = [
  { path: '', component: YoutubeDownloaderComponent },
  { path: 'youtube-downloader', component: YoutubeDownloaderComponent },
  { path: 'down', component: YoutubeDownloaderComponent },
  { path: 'transcriptions', component: OpenAiTranscriptionComponent },
  { path: 'png-to-webp', component: PngToWebpComponent },
  { path: 'billing', component: BillingComponent, canActivate: [AuthGuard] },
  {
    path: 'admin/users',
    component: AdminUsersComponent,
    canActivate: [RoleGuard],
    data: { roles: ['Admin'] }
  },
  {
    path: 'admin/recognition-profiles',
    component: AdminRecognitionProfilesComponent,
    canActivate: [RoleGuard],
    data: { roles: ['Admin'] }
  },
  { path: 'about3', component: About3Component },
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]

})
export class AppRoutingModule {}
