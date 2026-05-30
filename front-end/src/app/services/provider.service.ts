import { Injectable } from "@angular/core";
import { BaseUrlService } from "./base-url.service";
import { HttpClient } from "@angular/common/http";
import { GenericResponse } from "app/models/GenericResponse";
import { Observable } from "rxjs";
import { FornecedorModel } from "app/models/provider/provider.model";

@Injectable({
  providedIn: "root",
})
export class ProviderService {
  constructor(private http: HttpClient, private url: BaseUrlService) {}

  public GetAll(
    pageSize: number = 50,
    page = 1
  ): Observable<GenericResponse<FornecedorModel[]>> {
    let params: string[] = [
      `pageSize=${pageSize}`,
      `page=${page}`
    ];

    let url = this.url.Providers.Get + "?" + params.join("&");

    return this.http.get<GenericResponse<FornecedorModel[]>>(url);
  }

  public GetProvider(
    providerId: number
  ): Observable<GenericResponse<FornecedorModel>> {
    let url = this.url.Providers.Get + "/" + providerId;

    return this.http.get<GenericResponse<FornecedorModel>>(url);
  }

  public GetProviderByDocumentNumber(
    documentNumber: string
  ): Observable<GenericResponse<FornecedorModel>> {
    let url = this.url.Providers.Find + "/" + documentNumber;

    return this.http.get<GenericResponse<FornecedorModel>>(url);
  }

}
