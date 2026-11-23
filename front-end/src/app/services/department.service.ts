import { Injectable } from "@angular/core";
import { BaseUrlService } from "./base-url.service";
import { HttpClient } from "@angular/common/http";
import { GenericResponse } from "app/models/GenericResponse";
import { Observable } from "rxjs";
import { DepartamentoModel } from "app/models/department/departamento.model";

@Injectable({
  providedIn: "root",
})
export class DepartmentService {
  constructor(private http: HttpClient, private url: BaseUrlService) {}

  public GetAll(): Observable<GenericResponse<DepartamentoModel[]>> {
    return this.http.get<GenericResponse<DepartamentoModel[]>>(this.url.Department.Get);
  }
}
