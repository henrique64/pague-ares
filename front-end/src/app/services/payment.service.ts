import { Injectable } from "@angular/core";
import { BaseUrlService } from "./base-url.service";
import { HttpClient } from "@angular/common/http";
import { GenericResponse } from "app/models/GenericResponse";
import { PaymentRequestModel } from "app/models/payment/payment-request.model";
import { Observable } from "rxjs";
import { DashboardDto } from "app/models/payment/dashboard-model";
import { PaymentListModel } from "app/models/payment/payment-list.model";
import { PaymentListFilter } from "app/models/payment/payment-filter.model";

const USER_VAR: string = "User";

@Injectable({
  providedIn: "root",
})
export class PaymentService {
  constructor(private http: HttpClient, private url: BaseUrlService) {}

  public GetAll(
    startDate: Date | null,
    endDate: Date | null,
    type: number | null,
    number: number | null,
    pageSize: number = 50,
    page = 1
  ): Observable<GenericResponse<PaymentRequestModel[]>> {
    let params: string[] = [];

    let url = this.url.Payment.Get + "?" + params.join("&");

    return this.http.get<GenericResponse<PaymentRequestModel[]>>(url);
  }

  public GetRequest(
    requestId: number
  ): Observable<GenericResponse<PaymentRequestModel>> {
    let url = this.url.Payment.Get + "/" + requestId;

    return this.http.get<GenericResponse<PaymentRequestModel>>(url);
  }

  public UpsertRequest(
    request: PaymentRequestModel
  ): Observable<GenericResponse<PaymentRequestModel>> {
    let url = this.url.Payment.Upsert;

    return this.http.post<GenericResponse<PaymentRequestModel>>(url, request);
  }

  public GetDashboard(): Observable<GenericResponse<DashboardDto>> {
    let url = this.url.Payment.Dashboard;

    return this.http.get<GenericResponse<DashboardDto>>(url);
  }

  public SetUser(requestId: number, userId: number): Observable<GenericResponse<any>> {
    let url = this.url.Payment.Get + "/" + requestId + "/user?userId=" + userId;

    return this.http.get<GenericResponse<any>>(url);
  }

  public GetFilteredList(filter: PaymentListFilter): Observable<GenericResponse<PaymentListModel[]>> {
    let url = this.url.ListagemSolicitacoes.Get + "?" + this.toQueryString(filter);

    return this.http.get<GenericResponse<PaymentListModel[]>>(url);
  }

  toQueryString(params: PaymentListFilter): string {
    const searchParams = new URLSearchParams();

    for (const key in params) {
      if (Object.prototype.hasOwnProperty.call(params, key)) {
        const value = params[key];

        if (value === undefined || value === null || value === '') {
          continue; // Skip undefined, null, or empty string values
        }

        if (Array.isArray(value)) {
          value.forEach(item => {
            if (item !== undefined && item !== null && item !== '') {
              searchParams.append(key, String(item));
            }
          });
        } 
        else if (typeof value?.getMonth === 'function') {
          searchParams.append(key, (value as Date).toISOString());
        }
        else {
          searchParams.append(key, String(value));
        }
      }
    }
    return searchParams.toString();
  }

}
