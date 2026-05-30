import { Injectable } from "@angular/core";
import { BaseUrlService } from "./base-url.service";
import { HttpClient } from "@angular/common/http";
import { GenericResponse } from "app/models/GenericResponse";
import { Observable } from "rxjs";
import { DashboardDto } from "app/models/payment/dashboard-model";
import { RddvModel } from "app/models/rddv/rddv.model";

const USER_VAR: string = "User";

@Injectable({
  providedIn: "root",
})
export class RddvService {
  constructor(private http: HttpClient, private url: BaseUrlService) {}

  public GetAll(
    startDate: Date | null,
    endDate: Date | null,
    type: number | null,
    number: number | null,
    pageSize: number = 50,
    page = 1
  ): Observable<GenericResponse<RddvModel[]>> {
    let params: string[] = [];

    let url = this.url.Rddv.Get + "?" + params.join("&");

    return this.http.get<GenericResponse<RddvModel[]>>(url);
  }

  public GetRddv(
    requestId: number
  ): Observable<GenericResponse<RddvModel>> {
    let url = this.url.Rddv.Get + "/" + requestId;

    return this.http.get<GenericResponse<RddvModel>>(url);
  }

  public UpsertRddv(
    rddv: RddvModel
  ): Observable<GenericResponse<RddvModel>> {
    let url = this.url.Rddv.Upsert;

    return this.http.post<GenericResponse<RddvModel>>(url, rddv);
  }

  public GetDashboard(): Observable<GenericResponse<DashboardDto>> {
    let url = this.url.Rddv.Dashboard;

    return this.http.get<GenericResponse<DashboardDto>>(url);
  }

  public SetUser(reportId: number, userId: number): Observable<GenericResponse<any>> {
    let url = this.url.Rddv.Get + "/" + reportId + "/user?userId=" + userId;

    return this.http.get<GenericResponse<any>>(url);
  }

}
