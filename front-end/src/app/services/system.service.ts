import { Injectable } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { BaseUrlService } from "./base-url.service";
import { AppConfig } from '../models/app-config';

@Injectable({
  providedIn: "root",
})
export class SystemService {

  LocalSettings: AppConfig;

  constructor(
    private http: HttpClient,
    private url: BaseUrlService
  ) {
    
  }

  /**
   * Carrega as configurações locais a partir de um arquivo de configurações externo.
   */
   loadConfig() {
    return this.http
      .get<AppConfig>("./assets/config.json")
      .toPromise()
      .then(async (config) => {
        this.LocalSettings = config;

        this.url.BaseUrl = this.LocalSettings.BaseUrl;
      });
  }
  
}
