import { BrowserAnimationsModule } from "@angular/platform-browser/animations";
import { APP_INITIALIZER, NgModule } from "@angular/core";
import { FormsModule, ReactiveFormsModule } from "@angular/forms";
import { HTTP_INTERCEPTORS, HttpClientModule } from "@angular/common/http";
import { RouterModule } from "@angular/router";
import { AppRoutingModule } from "./app.routing";
import { ComponentsModule } from "./components/components.module";
import { AppComponent } from "./app.component";
import { AdminLayoutComponent } from "./layouts/admin-layout/admin-layout.component";
import { LoginComponent } from "./login/login.component";
import { AuthService } from "./services/auth.service";
import { LOCALE_ID } from "@angular/core";
import localePt from "@angular/common/locales/pt";
import { registerLocaleData } from "@angular/common";
import { PaymentReportComponent } from "./payment-report/payment-report.component";
import { RddvReportComponent } from "./rddv-report/rddv-report.component";
import { SystemService } from "./services/system.service";
import { MatTooltipModule } from "@angular/material/tooltip";
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthInterceptor } from "./core/interceptors/ApiAuthenticationInterceptor";
import { MatPaginatorIntl } from "@angular/material/paginator";
import { getPortuguesePaginatorIntl } from "./core/intl/mat-paginator-intl";

registerLocaleData(localePt);

export const configFactory = (system: SystemService) => {
  return () => system.loadConfig();
};

@NgModule({
  imports: [
    BrowserAnimationsModule,
    FormsModule,
    ReactiveFormsModule,
    HttpClientModule,
    ComponentsModule,
    RouterModule,
    HttpClientModule,
    AppRoutingModule,
    MatTooltipModule,
    MatProgressSpinnerModule
  ],
  exports: [],
  declarations: [
    AppComponent,
    AdminLayoutComponent,
    LoginComponent,
    PaymentReportComponent,
    RddvReportComponent
  ],
  providers: [
    AuthService,
    { provide: LOCALE_ID, useValue: "pt-BR" },
    {
      provide: APP_INITIALIZER,
      useFactory: configFactory,
      deps: [SystemService],
      multi: true,
    },
    {
      provide: HTTP_INTERCEPTORS,
      useClass: AuthInterceptor,
      multi: true
    },
    { provide: MatPaginatorIntl, useValue: getPortuguesePaginatorIntl() }
  ],
  bootstrap: [AppComponent],
})
export class AppModule {}
