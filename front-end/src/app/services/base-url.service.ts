import { Injectable } from "@angular/core";

@Injectable({
  providedIn: "root",
})
export class BaseUrlService {
  BaseUrl: string = "";

  constructor() {}

  get Authentication() {
    return { Login: this.BaseUrl + "/api/Auth" };
  }

  get Payment() {
    return {
      Get: this.BaseUrl + "/api/Request",
      Upsert: this.BaseUrl + "/api/Request",
      PreviewAttachment: this.BaseUrl + "/api/Preview/payment",
      Dashboard: this.BaseUrl + "/api/Request/Dashboard",
      GetPreviewToken: this.BaseUrl + "/api/Preview/generate-preview-key",
    };
  }

  get Rddv() {
    return {
      Get: this.BaseUrl + "/api/Rddv",
      Upsert: this.BaseUrl + "/api/Rddv",
      PreviewAttachment: this.BaseUrl + "/api/Preview/rddv",
      Dashboard: this.BaseUrl + "/api/Rddv/Dashboard",
      GetPreviewToken: this.BaseUrl + "/api/Preview/generate-preview-key",
    };
  }

  get Users() {
    return { Get: this.BaseUrl + "/api/Users" };
  }

  get ListagemSolicitacoes() {
    return { Get: this.BaseUrl + "/api/Listing" };
  }

  get Config() {
    return {
      Get: this.BaseUrl + "/api/Config",
      Post: this.BaseUrl + "/api/Config",
    };
  }

  get Providers() {
    return {
      Get: this.BaseUrl + "/api/Provider",
      Upsert: this.BaseUrl + "/api/Provider",
      Find: this.BaseUrl + "/api/Provider/find"
    };
  }

  get TravelType() {
    return {
      Get: this.BaseUrl + "/api/TravelType",
      Upsert: this.BaseUrl + "/api/TravelType"
    };
  }

  get Department() {
    return { Get: this.BaseUrl + "/api/Department" };
  }

  get Export() {
    return {
      DownloadRddvAttachments: this.BaseUrl + "/api/Export/rddv/attachment",
      DownloadRequestAttachments: this.BaseUrl + "/api/Export/request/attachment",
      FinanceAccountingPaymentList: this.BaseUrl + "/api/Export/payments"
    }
  }
}
