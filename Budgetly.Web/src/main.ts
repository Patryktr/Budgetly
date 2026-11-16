import { registerLocaleData } from '@angular/common';
import localePl from '@angular/common/locales/pl';
import { LOCALE_ID } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { AppComponent } from './app/app.component';

registerLocaleData(localePl);
bootstrapApplication(AppComponent, { providers: [{ provide: LOCALE_ID, useValue: 'pl' }] })
  .catch((error: unknown) => console.error(error));
