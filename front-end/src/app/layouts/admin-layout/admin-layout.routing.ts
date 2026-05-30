import { Routes } from "@angular/router";
import { HomeComponent } from "../../home/home.component";
import { PaymentsComponent } from "../../payments/payments.component";
import { IconsComponent } from "../../icons/icons.component";
import { RddvComponent } from "app/rddv/rddv.component";
import { CreateRequestComponent } from "app/create-request/create-request.component";
import { CreateUserComponent } from "app/create-user/create-user.component";
import { CreateRddvComponent } from "app/create-rddv/create-rddv.component";
import { ConfigurationComponent } from "app/configuration/configuration.component";

export const AdminLayoutRoutes: Routes = [
  { path: "home", component: HomeComponent },
  { path: "payments", component: PaymentsComponent },
  { path: "config", component: ConfigurationComponent },
  { path: "rddv", component: RddvComponent },
  { path: "icons", component: IconsComponent },
  { path: "create-request", component: CreateRequestComponent },
  { path: "create-request/:id", component: CreateRequestComponent },
  { path: "create-rddv", component: CreateRddvComponent },
  { path: "create-rddv/:id", component: CreateRddvComponent },
  { path: "create-user", component: CreateUserComponent },
  { path: "create-user/:id", component: CreateUserComponent },
];
