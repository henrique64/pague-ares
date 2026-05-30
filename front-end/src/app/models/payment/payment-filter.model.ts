import { EnumPaymentListViewMode } from "./enum-payment-list-view";

export class PaymentListFilter {
    documentNumber: string = "";
    requestStart: Date = undefined;
    requestEnd: Date = undefined;
    dueStart: Date = undefined;
    dueEnd: Date = undefined;
    requesterId: number = 0;
    managerStatus: number = 0;
    financeStatus: number = 0;
    accountingStatus: number = 0;
    assigneeId: number = 0;
    isAssigned: boolean | null = null;
    documentId: number;
    partnerName: string;
    partnerDocument: string;
    viewMode: EnumPaymentListViewMode | null;
    page: number = 1;
    pageSize: number = 50;
    sortField: string = "dataSolicitacao";
    sortOrder: string = "desc";
}