import { Injectable } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { Observable } from "rxjs";
import { BaseUrlService } from "./base-url.service";
import { GenericResponse } from "app/models/GenericResponse";
import { FiltroExportacaoModel } from "app/models/exportacao/filtro-exportacao.model";
import { ExportacaoJobModel, LinkDownloadExportacaoModel } from "app/models/exportacao/exportacao-job.model";

@Injectable({
  providedIn: "root",
})
export class ExportacaoService {
  constructor(private http: HttpClient, private url: BaseUrlService) {}

  public criar(filtro: FiltroExportacaoModel): Observable<GenericResponse<ExportacaoJobModel>> {
    return this.http.post<GenericResponse<ExportacaoJobModel>>(this.url.Exportacao.Base, filtro);
  }

  public listar(): Observable<GenericResponse<ExportacaoJobModel[]>> {
    return this.http.get<GenericResponse<ExportacaoJobModel[]>>(this.url.Exportacao.Base);
  }

  public obter(id: number): Observable<GenericResponse<ExportacaoJobModel>> {
    return this.http.get<GenericResponse<ExportacaoJobModel>>(this.url.Exportacao.Base + "/" + id);
  }

  public gerarLinkDownload(id: number): Observable<GenericResponse<LinkDownloadExportacaoModel>> {
    return this.http.post<GenericResponse<LinkDownloadExportacaoModel>>(
      this.url.Exportacao.Base + "/" + id + "/link-download", null);
  }

  public urlDownload(token: string): string {
    return this.url.Exportacao.Download + "?token=" + encodeURIComponent(token);
  }

  public cancelar(id: number): Observable<void> {
    return this.http.delete<void>(this.url.Exportacao.Base + "/" + id);
  }

  // Tira da lista do usuário uma exportação encerrada (Falhou, Cancelada, Expirada ou Excluída).
  public removerDaLista(id: number): Observable<void> {
    return this.http.delete<void>(this.url.Exportacao.Base + "/" + id + "/lista");
  }
}
