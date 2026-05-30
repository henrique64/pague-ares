import { NgModule } from "@angular/core";
import { RouterModule } from "@angular/router";
import { CommonModule } from "@angular/common";
import { FormsModule, ReactiveFormsModule } from "@angular/forms";
import { AdminLayoutRoutes } from "./admin-layout.routing";
import { HomeComponent } from "../../home/home.component";
import { PaymentsComponent } from "../../payments/payments.component";
import { IconsComponent } from "../../icons/icons.component";
import { MatButtonModule } from "@angular/material/button";
import { MatInputModule } from "@angular/material/input";
import { MatNativeDateModule, MatRippleModule } from "@angular/material/core";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatTooltipModule } from "@angular/material/tooltip";
import { MatCheckboxModule } from "@angular/material/checkbox";
import { MatSelectModule } from "@angular/material/select";
import { MatTabsModule } from "@angular/material/tabs";
import { MatDatepickerModule } from "@angular/material/datepicker";
import { MatSnackBarModule } from "@angular/material/snack-bar";
import { MatCard, MatCardModule } from "@angular/material/card";
import { RddvComponent } from "app/rddv/rddv.component";
import { RefundsComponent } from "app/refunds/refunds.component";
import { CreateRequestComponent } from "app/create-request/create-request.component";
import { CreateUserComponent } from "app/create-user/create-user.component";
import { NgxMaskModule } from "ngx-mask";
import { CurrencyMaskModule } from "ng2-currency-mask";
import { CreateRddvComponent } from "app/create-rddv/create-rddv.component";
import { ConfigurationComponent } from "app/configuration/configuration.component";
import { TravelTypeListComponent } from "app/configuration/travel-type-list/travel-type-list.component";
import { MatProgressSpinnerModule } from "@angular/material/progress-spinner";
import { MatPaginatorIntl, MatPaginatorModule } from "@angular/material/paginator";

@NgModule({
  imports: [
    CommonModule,
    RouterModule.forChild(AdminLayoutRoutes),
    FormsModule,
    ReactiveFormsModule,
    MatButtonModule,
    MatRippleModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatTooltipModule,
    MatCheckboxModule,
    MatTabsModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatSnackBarModule,
    MatCardModule,
    NgxMaskModule.forRoot(),
    CurrencyMaskModule,
    MatProgressSpinnerModule,
    MatPaginatorModule
  ],
  declarations: [
    HomeComponent,
    PaymentsComponent,
    RddvComponent,
    RefundsComponent,
    ConfigurationComponent,
    IconsComponent,
    CreateRequestComponent,
    CreateRddvComponent,
    CreateUserComponent,
    TravelTypeListComponent
  ]
})
export class AdminLayoutModule {}
