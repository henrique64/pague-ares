import { Component, OnInit } from "@angular/core";
import { MatSnackBar } from "@angular/material/snack-bar";
import { Router, ActivatedRoute } from "@angular/router";
import { UsuarioDto } from "app/models/authentication/UserDto.model";
import { UserListModel } from "app/models/authentication/UserList.model";
import { AttachedDocumentModel } from "app/models/payment/attached-document.model";
import { PaymentRequestModel } from "app/models/payment/payment-request.model";
import { AuthService } from "app/services/auth.service";
import { BaseUrlService } from "app/services/base-url.service";
import { PaymentService } from "app/services/payment.service";
import { UsersService } from "app/services/users.service";

@Component({
  selector: "payment-report",
  templateUrl: "./payment-report.component.html",
  styleUrls: ["./payment-report.component.css"],
})
export class PaymentReportComponent implements OnInit {
  model: PaymentRequestModel = null;
  requestId: number = 0;

  now: Date = new Date();

  bool: any = {
    true: "Sim",
    false: "Não",
    undefined: " ",
    null: " "
  };

  authTypes: any = {
    1: "Por Anexo",
    2: "Por Sistema"
  };

  paymentTypes: any = {
    1: "Boleto",
    2: "Transferência Bancária"
  }

  documentTypes: any = {
    1: "Adiantamento",
    2: "Pagamento",
    3: "Movimento Almoxarifado"
  }

  attachmentTypes: any = {
    1: "Nota Fiscal",
    2: "Fatura",
    3: "Nota de Débito",
    4: "Boleto",
    5: "RDDV",
    6: "Comprovantes",
    7: "Arquivo Zipado",
    8: "Outros Documentos",
    9: "Autorização"
  };

  statusGestor: any = {
    1: "Aguardando Autorização",
    2: "Autorizado",
    3: "Negado",
  };

  statusContabil: any = {
    1: "Aguardando Lançamento",
    2: "Lançado",
    3: "Negado",
  };

  statusFinanceiro: any = {
    1: "Aguardando Autorização",
    2: "Autorizado",
    3: "Negado",
  };

  departmentList: any = {
    1: "TIN",
    2: "ALM",
    3: "CPS",
    4: "CTB",
    5: "DIR",
    6: "ENG",
    7: "FIN",
    8: "ILS",
    9: "INF",
    10: "MKT",
    11: "MTG",
    12: "PRD",
    13: "PJT",
    14: "QLD",
    15: "RHU",
  };

  userList: UserListModel[] = [];

  constructor(
    private auth: AuthService,
    private router: Router,
    private thisRoute: ActivatedRoute,
    private payment: PaymentService,
    private users: UsersService,
    private url: BaseUrlService
  ) {}

  ngOnInit() {
    this.thisRoute.params.subscribe((p) => {
      if (p["id"]) {
        this.requestId = p["id"];

        console.log(this.requestId);
      }

      this.getUserList();
      this.initForm();
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

  async initForm() {
    await this.getRequest();
  }

  async getRequest(): Promise<boolean> {
    try {
      let req = await this.payment.GetRequest(this.requestId).toPromise();

      if (req.success) {
        this.model = req.data as PaymentRequestModel;

        if (!this.model.documentos) this.model.documentos = [];
      } else {
        return false;
      }
    } catch (ex) {
      console.log(ex);
      return false;
    }

    return true;
  }

  get documentos(): AttachedDocumentModel[] {
    return this.model.documentos?.filter(d => d.idTipoDocumento !== 9);
  }

  get autorizacoes(): AttachedDocumentModel[] {
    return this.model.documentos?.filter(d => d.idTipoDocumento === 9);
  }
}
