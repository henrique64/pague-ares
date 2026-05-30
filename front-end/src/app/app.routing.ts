import { NgModule } from "@angular/core";
import { CommonModule } from "@angular/common";
import { BrowserModule } from "@angular/platform-browser";
import { Routes, RouterModule } from "@angular/router";
import { AdminLayoutComponent } from "./layouts/admin-layout/admin-layout.component";
import { LoginComponent } from "./login/login.component";
import { PaymentReportComponent } from "./payment-report/payment-report.component";
import { RddvReportComponent } from "./rddv-report/rddv-report.component";

const routes: Routes = [
  /*{
    path: "",
    redirectTo: "login",
    pathMatch: "full",
  },*/
  {
    path: "login",
    component: LoginComponent /*,
    children: [
      {
        path: "",
        //loadChildren: () => import('./layouts/admin-layout/admin-layout.module').then(m => m.AdminLayoutModule)
      },
    ],*/,
  },
  {
    path: "payment-report/:id",
    component: PaymentReportComponent,
  },
  {
    path: "rddv-report/:id",
    component: RddvReportComponent,
  },
  {
    path: "",
    component: AdminLayoutComponent,
    children: [
      {
        path: "",
        loadChildren: () =>
          import("./layouts/admin-layout/admin-layout.module").then(
            (m) => m.AdminLayoutModule
          ),
      },
    ],
  },
];

@NgModule({
  imports: [
    CommonModule,
    BrowserModule,
    RouterModule.forRoot(routes, {
      useHash: true,
    }),
  ],
  exports: [],
})
export class AppRoutingModule {}
