import { Injectable } from "@angular/core";
import { BaseUrlService } from "./base-url.service";

@Injectable({
  providedIn: "root",
})
export class ExportService {
  constructor(private url: BaseUrlService) {}

  public GetPreviewToken() {
    
  }

  public GetRddvAttachmentsUrl(id: number) {
    return this.url.Export.DownloadRddvAttachments + "/" + id;
  }

  public GetPaymentAttachmentsUrl(id: number) {
    return this.url.Export.DownloadRequestAttachments + "/" + id;
  }

  public GetPaymentListUrl() {
    return this.url.Export.FinanceAccountingPaymentList;
  }

}
