import { bootstrapApplication } from '@angular/platform-browser';
import { AppComponent } from './app/app.component';
import { config } from './app/app.config.server';
import { startServer } from './server';

const bootstrap = () => bootstrapApplication(AppComponent, config);

startServer();

export default bootstrap;
