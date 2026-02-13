import { Routes } from '@angular/router';
import { YoutubeDownloaderComponent } from './youtube-downloader/youtube-downloader.component';
import { RecognitionTasksComponent } from './recognition-tasks/recognition-tasks.component';
import { SubtitlesTasksComponent } from './subtitles-task/subtitles-tasks.component';
import { MarkdownConverterComponent } from './Markdown-converter/markdown-converter.component';
import { PngToWebpComponent } from './png-to-webp/png-to-webp.component';
import { PngToWebpBatchComponent } from './png-to-webp-batch/png-to-webp-batch.component';
import { OpenAiTranscriptionComponent } from './openai-transcription/openai-transcription.component';
import { BillingComponent } from './billing/billing.component';
import { AuthGuard } from './services/auth.guard';
import { AboutBusinessComponent } from './about-business/about-business.component';
import { About1Component } from './about1/about1.component';
import { About2Component } from './about2/about2.component';
import { About3Component } from './about3/about3.component';
import { RoleGuard } from './services/role.guard';
import { AdminUsersComponent } from './admin-users/admin-users.component';
import { AdminRecognitionProfilesComponent } from './admin-recognition-profiles/admin-recognition-profiles.component';
import { AdminPaymentsComponent } from './admin-payments/admin-payments.component';
import { AdminBillingPlansComponent } from './admin-billing-plans/admin-billing-plans.component';
import { RecognitionControlComponent } from './app-recognition-control/recognition-control.component';
import { TaskPageComponent } from './task-page/task-page.component';
import { ServiceNewsComponent } from './service-news/service-news.component';
import { AudioFilesComponent } from './audio-file/audio-files.component';
import { AuthCallbackComponent } from './AuthCallbackComponent/auth-callback.component';
import { LoginComponent } from './LoginComponent/login.component';
import { EditorPageComponent } from './editor-pade/editor-page.component';
import { BlogFeedComponent } from './blog-feed/blog-feed.component';
import { BlogTopicCreateComponent } from './blog-topic-create/blog-topic-create.component';
import { BlogTopicDetailComponent } from './blog-topic-detail/blog-topic-detail.component';
import { ProfileComponent } from './profile/profile.component';
import { TranscriptionEditorComponent } from './transcription-editor/transcription-editor.component';

export const appRoutes: Routes = [
  { path: 'auth/callback', component: AuthCallbackComponent },
  { path: 'audio', component: AudioFilesComponent },
  { path: 'transcriptions', component: OpenAiTranscriptionComponent, canActivate: [AuthGuard] },
  { path: 'transcriptions/:id/edit', component: TranscriptionEditorComponent, canActivate: [AuthGuard] },
  { path: 'about', component: AboutBusinessComponent },
  { path: 'about1', component: About1Component },
  { path: 'about2', component: About2Component },
  { path: 'about3', component: About3Component },
  { path: 'down', component: YoutubeDownloaderComponent },
  { path: 'youtube-downloader', component: YoutubeDownloaderComponent },
  { path: '', component: About3Component },
  { path: 'recognition', component: RecognitionControlComponent },
  { path: 'recognition-tasks', component: RecognitionTasksComponent },
  { path: 'y', component: YoutubeDownloaderComponent },
  { path: 'recognized/:id', component: TaskPageComponent },
  { path: 'markdown-converter/:id', component: MarkdownConverterComponent },
  { path: 'markdown-converter', component: MarkdownConverterComponent },
  { path: 'png-to-webp/batch', component: PngToWebpBatchComponent },
  { path: 'png-to-webp', component: PngToWebpComponent },
  { path: 'edit/:id', component: EditorPageComponent },
  { path: 'ServiceNews/Content/:id', component: TaskPageComponent },
  { path: 'tasks', component: SubtitlesTasksComponent },
  { path: 'Scriptorium', component: SubtitlesTasksComponent },
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
  { path: 'blog/:slug', component: BlogTopicDetailComponent },
  { path: 'ServiceNews', component: ServiceNewsComponent },
  { path: 'blog', component: BlogFeedComponent },
  { path: 'billing', component: BillingComponent, canActivate: [AuthGuard] },
  {
    path: 'admin/users',
    component: AdminUsersComponent,
    canActivate: [RoleGuard],
    data: { roles: ['Admin'] }
  },
  {
    path: 'admin/payments',
    component: AdminPaymentsComponent,
    canActivate: [RoleGuard],
    data: { roles: ['Admin'] }
  },
  {
    path: 'admin/billing-plans',
    component: AdminBillingPlansComponent,
    canActivate: [RoleGuard],
    data: { roles: ['Admin'] }
  },
  {
    path: 'admin/recognition-profiles',
    component: AdminRecognitionProfilesComponent,
    canActivate: [RoleGuard],
    data: { roles: ['Admin'] }
  },
  { path: 'profile', component: ProfileComponent, canActivate: [AuthGuard] },
  { path: 'login', component: LoginComponent },
  { path: '**', redirectTo: '', pathMatch: 'full' }
];
