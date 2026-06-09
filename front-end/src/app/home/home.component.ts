import { ChangeDetectorRef, Component, OnDestroy, OnInit } from "@angular/core";
import { PageEvent } from "@angular/material/paginator";
import { MatSnackBar, MatSnackBarConfig } from "@angular/material/snack-bar";
import { Router } from "@angular/router";
import { EnumFuncao } from "app/models/authentication/EnumFuncao.model";
import { UserListModel } from "app/models/authentication/UserList.model";
import { GenericResponse } from "app/models/GenericResponse";
import { DashboardDto } from "app/models/payment/dashboard-model";
import { EnumPaymentListViewMode } from "app/models/payment/enum-payment-list-view";
import { PaymentListFilter } from "app/models/payment/payment-filter.model";
import { PaymentListModel } from "app/models/payment/payment-list.model";
import { AuthService } from "app/services/auth.service";
import { PaymentService } from "app/services/payment.service";
import { RddvService } from "app/services/rddv.service";
import { UsersService } from "app/services/users.service";
import moment from "moment";

@Component({
    selector: "app-dashboard",
    templateUrl: "./home.component.html",
    styleUrls: ["./home.component.css"],
    standalone: false
})
export class HomeComponent implements OnInit, OnDestroy {
  showAdm: boolean = false;

  dashboardData: DashboardDto = null;
  intervalHandle: any = null;
  timerHandle: any = null;
  lastPoll: Date = null;
  pollString: string = "";

  requests: PaymentListModel[] = [];
  userList: UserListModel[] = [];
  totalPages: number = 1;
  totalRecords: number = 0;

  isSettingUser: boolean = false;
  canAssign: boolean = false;
  myFilter: PaymentListFilter = {
    documentNumber: null,
    requestStart: null,
    requestEnd: null,
    dueStart: null,
    dueEnd: null,
    requesterId: null,
    managerStatus: null,
    financeStatus: null,
    accountingStatus: null,
    assigneeId: null,
    isAssigned: false,
    documentId: null,
    partnerName: null,
    partnerDocument: null,
    viewMode: EnumPaymentListViewMode.Dashboard,
    page: 1,
    pageSize: 50,
    sortField: "",
    sortOrder: ""
  }

  constructor(
    private auth: AuthService,
    private router: Router,
    private payment: PaymentService,
    private rddv: RddvService,
    private users: UsersService,
    private snack: MatSnackBar,
    private change: ChangeDetectorRef
  ) {}

  ngOnDestroy(): void {
    if (this.intervalHandle) clearInterval(this.intervalHandle);
    if (this.timerHandle) clearInterval(this.timerHandle);
  }

  ngOnInit() {
    if (
      this.auth.IsInRole(EnumFuncao.Administrador) ||
      this.auth.IsInRole(EnumFuncao.Financeiro) ||
      this.auth.IsInRole(EnumFuncao.Contabilidade)
    )
      this.showAdm = true;

    if(!this.showAdm) {
      this.router.navigateByUrl("/payments")
    }

    this.canAssign = this.auth.IsInRole(EnumFuncao.Financeiro);

    this.updateDashboard();
    this.initTimer();
    this.getUserList();
  }

  async getUserList() {
    try {
      let res = await this.users.GetList("FIN,CON").toPromise();

      if (res.success) {
        this.userList = res.data;
      }
    } catch (ex) {
      console.log(ex);
    }
  }

  initTimer() {
    this.intervalHandle = setInterval(this.updateDashboard.bind(this), 30000);
    this.timerHandle = setInterval(this.updatePoll.bind(this), 1000);
  }

  async updateDashboard() {
    try {
      let res = await this.payment.GetDashboard().toPromise();

      if (res.success) {
        this.dashboardData = res.data;

        this.lastPoll = new Date();
      }
    } catch (ex) {
      console.log(ex);
    }

    this.getRequestList();
  }

  updatePoll() {
    if (!this.lastPoll) return;

    let mLast = moment(this.lastPoll);
    let mCurrent = moment(new Date());

    let diffSeconds = mCurrent.diff(mLast, "s");

    if (diffSeconds < 60) {
      this.pollString = "Atualizado há " + diffSeconds.toFixed(0) + " segundos";
    } else {
      let diffMinute = mCurrent.diff(mLast, "minute");

      this.pollString = "Atualizado há " + diffMinute.toFixed(0) + " minutos";
    }
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

    this.getRequestList();
  }

  setPageSizeOptions(setPageSizeOptionsInput: string) {
    if (setPageSizeOptionsInput) {
      this.pageSizeOptions = setPageSizeOptionsInput.split(',').map(str => +str);
    }
  }

  async getRequestList() {
    if (this.isSettingUser) return;

    try {
        let res = await this.payment.GetFilteredList(this.myFilter)
          .toPromise();

      if (res.success) {
        this.requests = res.data;
        this.totalRecords = res.records;
        this.totalPages = res.pages;

        this.change.detectChanges();
      }
    } catch (ex) {
      console.log(ex);
    }
  }

  async setUser(req: PaymentListModel) {
    if (this.isSettingUser) return;

    this.isSettingUser = true;

    try {
      let userId = req.idUsuarioAtribuido;
      let requestId = req.codigo;

      let ret: GenericResponse<any>;

      if(req.idTipo === 0) {
        ret = await this.payment.SetUser(requestId, userId).toPromise();
      }
      else {
        ret = await this.rddv.SetUser(requestId, userId).toPromise();
      }

      this.isSettingUser = false;

      if (ret.success) {
        this.snack.open("Solicitação atribuída com sucesso.", "OK", {
          duration: 1500,
        } as MatSnackBarConfig);

        this.getRequestList();
      } else {
        this.snack.open(
          "Ocorreu um erro ao atribuir o usuário a esta solicitação. Tente novamente.",
          "OK",
          {
            duration: 1500,
          } as MatSnackBarConfig
        );
      }
    } catch (ex) {
      this.isSettingUser = false;

      this.snack.open(
        "Ocorreu um erro ao atribuir o usuário a esta solicitação. Tente novamente.",
        "OK",
        {
          duration: 1500,
        } as MatSnackBarConfig
      );
    }
    finally {
      this.isSettingUser = false;
      this.change.detectChanges();
    }
  }

  print(req: PaymentListModel) {
    if(req.idTipo === 0) {
      window.open("/#/payment-report/" + req.codigo)
    }
    else {
      window.open("/#/rddv-report/" + req.codigo)
    }
  }

  details(req: PaymentListModel) {
    if(req.idTipo === 0) {
      this.router.navigateByUrl("/create-request/" + req.codigo);
    }
    else {
      this.router.navigateByUrl("/create-rddv/" + req.codigo);
    }
  }

}
