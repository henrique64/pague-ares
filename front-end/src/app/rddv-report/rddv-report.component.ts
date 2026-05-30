import { Component, OnInit } from "@angular/core";
import { MatSnackBar } from "@angular/material/snack-bar";
import { Router, ActivatedRoute } from "@angular/router";
import { UsuarioDto } from "app/models/authentication/UserDto.model";
import { UserListModel } from "app/models/authentication/UserList.model";
import { PaymentRequestModel } from "app/models/payment/payment-request.model";
import { RddvModel } from "app/models/rddv/rddv.model";
import { TipoViagemModel } from "app/models/travel-type/travel-type.model";
import { AuthService } from "app/services/auth.service";
import { BaseUrlService } from "app/services/base-url.service";
import { PaymentService } from "app/services/payment.service";
import { RddvService } from "app/services/rddv.service";
import { TravelTypeService } from "app/services/travel-type.service";
import { UsersService } from "app/services/users.service";

@Component({
  selector: "rddv-report",
  templateUrl: "./rddv-report.component.html",
  styleUrls: ["./rddv-report.component.css"],
})
export class RddvReportComponent implements OnInit {
  model: RddvModel = null;
  requestId: number = 0;

  now: Date = new Date();

  bool: any = {
    true: "Sim",
    false: "Não"
  };

  authTypes: any = {
    1: "Por Anexo",
    2: "Por Digitação"
  };

  paymentTypes: any = {
    1: "Boleto",
    2: "Transferência Bancária"
  }

  documentTypes: any = {
    1: "Pagamento",
    2: "Adiantamento"
  }

  tiposRelatorio: any = {
    1: "Adiantamento" ,
    2: "Prestação de Contas" ,
    3: "Reembolso",
  };

  attachmentTypes: any = {
    1: "Nota Fiscal",
    2: "Fatura",
    3: "Nota de Débito",
    4: "Boleto",
    5: "RDDV",
    6: "Comprovantes",
    7: "Arquivo Zipado",
    8: "Outros Documentos",
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

  tiposDespesa: any[] = [
    { id: 1, name: "Alimentação" },
    { id: 2, name: "Hospedagem" },
    { id: 3, name: "Passagens" },
    { id: 4, name: "Táxi" },
    { id: 5, name: "Kilometragem" },
    { id: 6, name: "Estacionamento" },
    { id: 7, name: "Pedágio" },
    { id: 8, name: "Combustível" },
    { id: 9, name: "Aluguel de Automóveis" },
    { id: 10, name: "Comunicações" },
    { id: 11, name: "Bagagens" },
    { id: 12, name: "Outros" },
  ];

  userList: UserListModel[] = [];
  travelTypes: TipoViagemModel[] = [];

  constructor(
    private auth: AuthService,
    private router: Router,
    private thisRoute: ActivatedRoute,
    private payment: PaymentService,
    private travel: TravelTypeService,
    private rddv: RddvService,
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

  async getTravelTypes() {
    try {
      let res = await this.travel.GetAll().toPromise();

      if (res.success) {
        this.travelTypes = res.data;
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
      let req = await this.rddv.GetRddv(this.requestId).toPromise();

      if (req.success) {
        this.model = req.data as RddvModel;

        if (!this.model.documentos) this.model.documentos = [];
        if (!this.model.despesas) this.model.despesas = [];
      } else {
        return false;
      }
    } catch (ex) {
      console.log(ex);
      return false;
    }

    return true;
  }

  labelTipoDespesa(tipo: number) {
    return this.tiposDespesa.find(x => x.id === tipo).name;
  }

  /* ================= Totalizadores ================= */

  getTotal(tipo: number): number {
    return this.model?.despesas
        .filter(d => d.tipoDespesa === tipo)
        .map(x => x.valor)
        .reduce((prev, curr) => prev + curr, 0)
  }

  get totalDiaria(): number {
    if(!this.model?.diariaViagem) {
      return 0;
    }
    
    let total = this.model.valorDiaria * this.model?.diarias;
    
    if(isNaN(total) || total < 0) {
      total = 0;
    }

    return total;
  }

  get subTotal(): number {
    return this.model?.despesas
        .map(x => x.valor)
        .reduce((prev, curr) => prev + curr, 0) + this.totalDiaria;
  }

  get totalAdiantamento(): number {
    let valor = this.model?.adiantamento ? this.model.valorAdiantamento : 0;

    if(!valor)
      return 0;
    else
      return valor;
  }

  get totalVtm(): number {
    let valor = this.model?.adiantamento && this.model?.tipoViagem > 1 ? this.model.saldoCartaoVtm : 0;

    if(!valor)
      return 0;
    else
      return valor;
  }

  get totalAres(): number {
    return (this.subTotal - this.totalAdiantamento) + this.totalVtm;
  }

  /* ================= Totalizadores ================= */

  get nomeGestor(): string {
    if(!this.model)
      return "";

    if(this.model?.tipoAutorizacao === 1)
      return "Por Anexo";

    if(!this.userList)
      return "";
    
    let user = this.userList.find(u => u.idUsuario === this.model.idGestor);

    if(user === null)
      return "";

    return user.nome;
  }

}
