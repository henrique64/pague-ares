import { Injectable } from "@angular/core";
import { BaseUrlService } from "./base-url.service";
import { HttpClient } from "@angular/common/http";
import { GenericResponse } from "app/models/GenericResponse";
import { Observable } from "rxjs";
import { ConfiguracaoModel } from "app/models/config/configuracao.model";

@Injectable({
  providedIn: "root",
})
export class ConfigService {
  constructor(private http: HttpClient, private url: BaseUrlService) {}

  public GetAll(): Observable<GenericResponse<ConfiguracaoModel[]>> {
    let url = this.url.Config.Get;

    return this.http.get<GenericResponse<ConfiguracaoModel[]>>(url);
  }

  public Upsert(config: ConfiguracaoModel[]): Observable<GenericResponse<ConfiguracaoModel[]>> {
    let url = this.url.Config.Post;

    return this.http.post<GenericResponse<ConfiguracaoModel[]>>(url, config);
  }

}
