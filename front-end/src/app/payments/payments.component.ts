import { ChangeDetectorRef, Component, OnInit } from "@angular/core";
import { ActivatedRoute } from "@angular/router";
import { EnumFuncao } from "app/models/authentication/EnumFuncao.model";
import { AuthService } from "app/services/auth.service";
import { ExportService } from "app/services/export.service";
import { PaymentService } from "app/services/payment.service";
import { UsersService } from "app/services/users.service";
import { PaymentListFilter } from "../models/payment/payment-filter.model";
import { UserListModel } from "app/models/authentication/UserList.model";
import { PaymentListModel } from "app/models/payment/payment-list.model";
import { EnumPaymentListViewMode } from "app/models/payment/enum-payment-list-view";
import { PageEvent } from "@angular/material/paginator";
import { MatTabChangeEvent } from "@angular/material/tabs";
import { EnumRequestType } from "app/models/authentication/EnumRequestType.model";
import { PreviewTokenService } from "app/services/preview-token.service";
import { MatSnackBar } from "@angular/material/snack-bar";

@Component({
    selector: "app-payments",
    templateUrl: "./payments.component.html",
    styleUrls: ["./payments.component.css"],
    standalone: false
})
export class PaymentsComponent implements OnInit {

  isBusy: boolean = false;
  isReadOnly: boolean = false;
  isAdmin: boolean = false;
  isManager: boolean = false;
  isRequester: boolean = false;
  isFinancial: boolean = false;
  isAccounting: boolean = false;

  constructor(
    private payment: PaymentService,
    private auth: AuthService,
    private users: UsersService,
    private thisRoute: ActivatedRoute,
    private change: ChangeDetectorRef,
    private exportService: ExportService,
    private previewTokenService: PreviewTokenService,
    private snackbar: MatSnackBar
  ) {}

  requests: PaymentListModel[] = [];
  totalPages: number = 1;
  totalRecords: number = 0;
  isLoadingRequests = false;

  documentTypes: any = {
    1: "Adiantamento",
    2: "Pagamento",
    3: "Movimento Almoxarifado"
  }

  listStatusGestor: any[] = [
    { id: 1, name: "Aguardando Autorização" },
    { id: 2, name: "Autorizado" },
    { id: 3, name: "Negado" },
  ];

  listStatusFinanceiro: any[] = [
    { id: 1, name: "Aguardando Autorização" },
    { id: 2, name: "Autorizado" },
    { id: 3, name: "Negado" },
  ];

  listStatusContabil: any[] = [
    { id: 1, name: "Aguardando Lançamento" },
    { id: 2, name: "Lançado" },
    { id: 3, name: "Negado" },
  ];

  currentTab: number = 0;
  myFilter: PaymentListFilter;
  viewMode: EnumPaymentListViewMode = EnumPaymentListViewMode.MyView;

  userList: UserListModel[] = [];

  ngOnInit() {
    this.loadFilter();

    if(!this.myFilter)
      this.myFilter = new PaymentListFilter();

    this.isAdmin = this.auth.IsInRole(EnumFuncao.Administrador);
    this.isManager = this.auth.IsInRole(EnumFuncao.Gestor);
    this.isRequester = this.auth.IsInRole(EnumFuncao.Solicitante);
    this.isFinancial = this.auth.IsInRole(EnumFuncao.Financeiro);
    this.isAccounting = this.auth.IsInRole(EnumFuncao.Contabilidade);

    this.getUserList();

    this.thisRoute.queryParamMap.subscribe(queryParams => {
      let view = queryParams.get("view");

      if(view) {
        this.currentTab = parseInt(view);

        if((this.currentTab < 0 && this.currentTab > 2) || isNaN(this.currentTab))
          this.currentTab = 0;

        this.viewMode = <EnumPaymentListViewMode>(this.currentTab + 1);
        
        this.change.detectChanges();
      }

      this.loadPayments();
    });
    
  }

  async getUserList() {
    try {
      let res = await this.users.GetList().toPromise();

      if (res.success) {
        this.userList = res.data;
      }
    } catch (ex) {
      console.log(ex);
    }
  }

  async loadPayments() {
    if(this.isLoadingRequests) return;

    this.requests = [];
    this.isLoadingRequests = true;
    this.change.detectChanges();

    this.myFilter.viewMode = this.viewMode;

    try {
      let res = await this.payment.GetFilteredList(this.myFilter).toPromise();

      if (res.success) {
        this.requests = res.data;
        this.totalPages = res.pages;
        this.totalRecords = res.records;
      }
    } catch (ex) {
      console.log(ex);
      this.snackbar.open("Erro ao carregar as solicitações. Tente novamente.", "OK", { duration: 4000 });
    }
    finally {
      this.isLoadingRequests = false;
      this.change.detectChanges();
    }
  }

  setView(event: MatTabChangeEvent) {
    event.tab.textLabel.indexOf("Minhas Solicitações") >= 0 ?
      this.viewMode = EnumPaymentListViewMode.MyView :
    event.tab.textLabel.indexOf("Gestor") >= 0 ?
      this.viewMode = EnumPaymentListViewMode.ManagerView :
      this.viewMode = EnumPaymentListViewMode.FinancialView;

    this.loadPayments();
    this.saveFilter();
  }

  loadFilter() {
    let data: any = localStorage.getItem("PaymentsFilter");

    if(data) {
      this.myFilter = JSON.parse(data) as PaymentListFilter;

      if(this.myFilter.viewMode)
        this.viewMode = this.myFilter.viewMode;

      this.currentTab = this.viewMode - 1;
    }
  }

  saveFilter() {
    localStorage.setItem("PaymentsFilter", JSON.stringify(this.myFilter));
  }

  clearFilters() {
    this.myFilter = new PaymentListFilter();
    localStorage.removeItem("PaymentsFilter");
  }

  resetPage() {
    this.myFilter.page = 1;
    this.myFilter.pageSize = 100;
  }

  sortField(field: string) {
    if(this.myFilter.sortField === field) {
      this.myFilter.sortOrder = this.myFilter.sortOrder === "asc" ? "desc" : "asc";
    }
    else {
      this.myFilter.sortField = field;
      this.myFilter.sortOrder = "asc";
    }

    this.resetPage();
    this.saveFilter();
    this.loadPayments();
  }
  
  isSortAsc(field: string): boolean {
    return this.myFilter.sortField === field && 
      this.myFilter.sortOrder === "asc";
  }

  isSortDesc(field: string): boolean {
    return this.myFilter.sortField === field && 
          this.myFilter.sortOrder === "desc";
  }

  print(req: PaymentListModel) {
    if(req.idTipo === 0) {
      window.open("/#/payment-report/" + req.codigo);
      return;
    }

    window.open("/#/rddv-report/" + req.codigo);
  }

  edit(req: PaymentListModel) {
    if(req.idTipo === 0) {
      window.open("/#/create-request/" + req.codigo + "?returnTo=" + this.currentTab);
      return;
    }
    
    window.open("/#/create-rddv/" + req.codigo + "?returnTo=" + this.currentTab);
  }

  newRequest() {
    window.open("/#/create-request?returnTo=" + this.currentTab, "_blank");
  }

  newRddv() {
    window.open("/#/create-rddv?returnTo=" + this.currentTab, "_blank");
  }

  clone(req: PaymentListModel) {
    if(req.idTipo === 0) {
      window.open(`/#/create-request/${req.codigo}?clone=true&returnTo=` + this.currentTab, "_blank");
      return;
    }

    window.open(`/#/create-rddv/${req.codigo}?clone=true&returnTo=` + this.currentTab, "_blank");
  }

  get allowExport(): boolean {
    return this.viewMode == EnumPaymentListViewMode.FinancialView;
  }

  async exportFinancialView() {
    const token = await this.previewTokenService.GetPreviewToken(0, EnumRequestType.Exportacao);

    if (!token) {
      this.snackbar.open("Não foi possível gerar o token de visualização.", "OK", { duration: 3000 });
      return;
    }

    var url = this.exportService.GetPaymentListUrl();
    var filter = JSON.stringify(this.myFilter);
    var userId = this.auth.CurrentUser.idUsuario;

    window.open(`${url}?sFilter=${encodeURIComponent(filter)}&userId=${userId}&key=${token.previewKey}`);
  }

  get length(): number {
    return this.totalRecords;
  }

  get pageSize(): number {
    return this.myFilter?.pageSize ?? 50;
  }

  set pageSize(value: number) {
    this.myFilter.pageSize = value;
  }

  get pageIndex(): number {
    return (this.myFilter?.page ?? 1) - 1;
  }

  set pageIndex(value: number) {
    this.myFilter.page = value + 1;
  }

  pageSizeOptions = [1, 10, 25, 50];

  hidePageSize = false;
  showPageSizeOptions = true;
  showFirstLastButtons = false;
  disabled = false;

  pageEvent: PageEvent;

  handlePageEvent(e: PageEvent) {
    this.pageEvent = e;
    this.pageSize = e.pageSize;
    this.pageIndex = e.pageIndex;

    this.saveFilter();
    this.loadPayments();
  }

  setPageSizeOptions(setPageSizeOptionsInput: string) {
    if (setPageSizeOptionsInput) {
      this.pageSizeOptions = setPageSizeOptionsInput.split(',').map(str => +str);
    }
  }

}
