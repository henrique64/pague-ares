import { EnumRequestType } from "./EnumRequestType.model"

export interface PreviewKeyModel {
    previewKey: string,
    requestType: EnumRequestType,
    entityId: number
}