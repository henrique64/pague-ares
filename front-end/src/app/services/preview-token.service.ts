import { HttpClient } from "@angular/common/http";
import { Injectable } from "@angular/core";
import { BaseUrlService } from "./base-url.service";
import { EnumRequestType } from "app/models/authentication/EnumRequestType.model";
import { PreviewKeyModel } from "app/models/authentication/PreviewKey.model";
import { GenericResponse } from "app/models/GenericResponse";

@Injectable({
    providedIn: "root",
})
export class PreviewTokenService {
    constructor(
        private http: HttpClient,
        private url: BaseUrlService
    ) {}

    async GetPreviewToken(entityId: number, type: EnumRequestType): Promise<PreviewKeyModel | null> {
        try {
            const url = this.url.Payment.GetPreviewToken + `?entityId=${entityId}&requestType=${type}`;
            const response = await this.http.post<GenericResponse<PreviewKeyModel>>(url, {}).toPromise();

            if (response.success) {
                return response.data;
            }

            return null;
        }
        catch (e) {
            console.log("Ocorreu um erro ao gerar o token de visualização:", e);
            return null;
        }
    }
}