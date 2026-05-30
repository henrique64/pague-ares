import { Injectable } from "@angular/core";
import { BaseUrlService } from "./base-url.service";
import { HttpClient } from "@angular/common/http";
import { GenericResponse } from "app/models/GenericResponse";
import { Observable } from "rxjs";
import { TipoViagemModel } from "app/models/travel-type/travel-type.model";

@Injectable({
  providedIn: "root",
})
export class TravelTypeService {
  constructor(private http: HttpClient, private url: BaseUrlService) {}

  public GetAll(
    pageSize: number = 50,
    page = 1
  ): Observable<GenericResponse<TipoViagemModel[]>> {
    let params: string[] = [
      `pageSize=${pageSize}`,
      `page=${page}`
    ];

    let url = this.url.TravelType.Get + "?" + params.join("&");

    return this.http.get<GenericResponse<TipoViagemModel[]>>(url);
  }

  public GetTravelType(
    typeId: number
  ): Observable<GenericResponse<TipoViagemModel>> {
    let url = this.url.TravelType.Get + "/" + typeId;

    return this.http.get<GenericResponse<TipoViagemModel>>(url);
  }

  public UpsertTravelType(
    model: TipoViagemModel
  ): Observable<GenericResponse<TipoViagemModel>> {
    let url = this.url.TravelType.Upsert;

    return this.http.post<GenericResponse<TipoViagemModel>>(url, model);
  }

}
