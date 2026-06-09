import { Component, OnDestroy, OnInit } from "@angular/core";
import { Subscription } from "rxjs";
import { MatSnackBar, MatSnackBarConfig } from "@angular/material/snack-bar";
import { ActivatedRoute, Router } from "@angular/router";
import { DialogService } from "app/components/dialog/dialog.service";
import { EnumDialogResult } from "app/components/dialog/dialogresult.enum";
import { EnumFuncao } from "app/models/authentication/EnumFuncao.model";
import { EnumRequestType } from "app/models/authentication/EnumRequestType.model";
import { UsuarioDto } from "app/models/authentication/UserDto.model";
import { UserListModel } from "app/models/authentication/UserList.model";
import { ConfiguracaoModel } from "app/models/config/configuracao.model";
import { RddvConfigModel } from "app/models/config/rddv-config.model";
import { RddvAttachmentModel } from "app/models/rddv/rddv-attachment.model";
import { RddvExpenseModel } from "app/models/rddv/rddv-expense.model";
import { RddvModel } from "app/models/rddv/rddv.model";
import { TipoViagemModel } from "app/models/travel-type/travel-type.model";
import { AuthService } from "app/services/auth.service";
import { BaseUrlService } from "app/services/base-url.service";
import { ConfigService } from "app/services/config.service";
import { ExportService } from "app/services/export.service";
import { PreviewTokenService } from "app/services/preview-token.service";
import { RddvService } from "app/services/rddv.service";
import { TravelTypeService } from "app/services/travel-type.service";
import { UsersService } from "app/services/users.service";
import { DepartmentService } from "app/services/department.service";
import { DepartamentoModel } from "app/models/department/departamento.model";
import { UtilsService } from "app/services/utils.service";

import moment from "moment";

@Component({
    selector: "create-rddv",
    templateUrl: "./create-rddv.component.html",
    styleUrls: ["./create-rddv.component.css"],
    standalone: false
})
export class CreateRddvComponent implements OnInit, OnDestroy {
  showAdm: boolean = false;

  reportId: number = 0;
  model: RddvModel;
  isEdit: boolean = false;
  isClone: boolean = false;
  isFinal: boolean = false;

  isUserView: boolean = false;
  isReadOnly: boolean = false;
  isManagerReadOnly: boolean = false;
  isFinanceReadOnly: boolean = false;
  isAccountingReadOnly: boolean = false;
  isAccounting: boolean = false;

  imageError: string = "";

  isBusy: boolean = false;

  private subs = new Subscription();

  isEditingExpense: boolean = false;
  isNewExpense: boolean = false;
  expenseModel: RddvExpenseModel = null;
  sourceExpense: RddvExpenseModel = null;

  attachmentTypes: any[] = [
    { id: 1, name: "Nota Fiscal" },
    { id: 2, name: "Fatura" },
    { id: 3, name: "Nota de Débito" },
    { id: 4, name: "Boleto" },
    { id: 5, name: "RDDV" },
    { id: 6, name: "Comprovantes" },
    { id: 7, name: "Arquivo Zipado" },
    { id: 8, name: "Outros Documentos" },
    { id: 9, name: "Autorização" },
  ];

  selectedAttachmentType: number;

  tiposRelatorio: any[] = [
    { id: 1, name: "Adiantamento" },
    { id: 2, name: "Prestação de Contas" },
    { id: 3, name: "Reembolso" },
  ];

  tiposMoeda: any[] = [
    { id: 1, name: "Real" },
    { id: 2, name: "Dólar" },
    { id: 3, name: "Euro" },
  ];

  tiposViagem: any[] = [
    { id: 1, name: "Nacional" },
    { id: 2, name: "Internacional" },
    { id: 3, name: "Internacional Israel" },
    { id: 4, name: "Internacional Euro" },
  ];

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


  labelTipoDespesa(tipo: number) {
    return this.tiposDespesa.find((x) => x.id === tipo).name;
  }

  userList: UserListModel[] = [];
  managersList: UserListModel[] = [];
  travelTypes: TipoViagemModel[] = [];
  departmentList: DepartamentoModel[] = [];

  /* Parâmetros de Configuração */

  configuration: ConfiguracaoModel[] = [];
  rddvConfig: RddvConfigModel = null;

  liberarEdicaoDepartamento: boolean = false;

  returnTo: string = "0";

  constructor(
    private auth: AuthService,
    private router: Router,
    private thisRoute: ActivatedRoute,
    private rddv: RddvService,
    private travel: TravelTypeService,
    private snack: MatSnackBar,
    private users: UsersService,
    private url: BaseUrlService,
    private config: ConfigService,
    private exportService: ExportService,
    private dialog: DialogService,
    private previewTokenService: PreviewTokenService,
    private departmentService: DepartmentService
  ) {}

  ngOnDestroy() {
    this.subs.unsubscribe();
  }

  ngOnInit() {
    this.subs.add(this.thisRoute.params.subscribe((routeParams) => {
      this.subs.add(this.thisRoute.queryParamMap.subscribe((queryParams) => {
        if (routeParams["id"]) {
          this.reportId = routeParams["id"];
          this.isEdit = true;
        }

        if (queryParams.get("clone")) {
          this.isEdit = false;
          this.isClone = true;
        }

        if(queryParams.get("returnTo")) {
          this.returnTo = queryParams.get("returnTo");
        }

        this.getConfig();
        this.getUserList();
        this.getManagersList();
        this.getTravelTypes();
        this.getDepartmentList();
        this.initForm();
      }));
    }));
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

  async getManagersList() {
    try {
      let res = await this.users.GetList("GES").toPromise();

      if (res.success) {
        this.managersList = res.data;
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

  async getDepartmentList() {
    try {
      let res = await this.departmentService.GetAll().toPromise();

      if (res.success) {
        this.departmentList = res.data;
      }
    } catch (ex) {
      console.log(ex);
    }
  }

  async initForm() {
    if (!this.isEdit && !this.isClone) {
      this.model = new RddvModel();

      this.model.dataCadastro = new Date();
      this.model.documentos = [];
      this.model.despesas = [];
      this.model.rascunho = true;
      this.model.cancelado = false;
      
      this.model.idUsuario = this.auth.CurrentUser.idUsuario;
      this.isUserView = true;

      this.model.nomeFuncionario = this.auth.CurrentUser.nome;
      this.model.idDepartamento = this.auth.CurrentUser.idDepartamento;

      if (!this.model.idDepartamento) {
        this.liberarEdicaoDepartamento = true;
      }
    } else {
      await this.getReport();

      if (this.isClone) {
        this.clone();
      } else {
        this.isUserView =
          this.model.idUsuario === this.auth.CurrentUser.idUsuario;
        this.isReadOnly = !(this.model.statusGestor === null || this.model.statusGestor === 1) || (!this.model.rascunho && this.model.cancelado) || !this.isUserView;
        this.isManagerReadOnly = !this.auth.IsInRole(EnumFuncao.Gestor); // || this.model.statusGestor !== 1;
        this.isFinanceReadOnly = !this.auth.IsInRole(EnumFuncao.Financeiro); // || this.model.statusFinanceiro !== 1;
        this.isAccountingReadOnly = !this.auth.IsInRole(EnumFuncao.Contabilidade); // || this.model.statusContabilidade !== 1;
        this.isFinal = this.model.statusGestor === 3 || this.model.statusFinanceiro === 3 || this.model.statusContabilidade === 3 || this.model.statusContabilidade === 2;
        this.isAccounting = this.auth.IsInRole(EnumFuncao.Contabilidade);
      }
    }
  }

  async getReport(): Promise<boolean> {
    try {
      let req = await this.rddv.GetRddv(this.reportId).toPromise();

      if (req.success) {
        this.model = req.data as RddvModel;

        if (!this.model.documentos) this.model.documentos = [];
      } else {
        this.alert(req.message);
        return false;
      }
    } catch (ex) {
      console.log(ex);
      return false;
    }

    return true;
  }

  clone() {
    this.model.idRelatorio = 0;
    this.model.dataCadastro = new Date();
    this.model.documentos = [];
    this.model.despesas = [];
    this.model.rascunho = true;
    this.model.cancelado = false;

    this.model.observacaoGestor = "";
    this.model.observacaoSetor = "";
    this.model.statusContabilidade = null;
    this.model.statusFinanceiro = null;
    this.model.statusGestor = null;
    this.model.dataAtribuicao = null;
    this.model.idUsuarioAtribuido = null;
    this.model.idUsuarioAtribuidor = null;
    this.model.aprovadoGestor = null;
    this.model.aprovadoSetor = null;
    this.model.idGestor = null;

    this.model.idUsuario = this.auth.CurrentUser.idUsuario;
    this.isUserView = true;
  }

  alert(message): void {
    var cfg = new MatSnackBarConfig();
    cfg.duration = 5000;

    this.snack.open(message, "OK", cfg);
  }

  validate(): string[] {
    let errors: string[] = [];

    if (!this.model?.nomeFuncionario?.trim()) {
      errors.push("O campo 'Nome do Funcionário' é obrigatório.");
    }

    if (!this.model?.cpf?.trim()) {
      errors.push("O campo 'CPF/CNPJ' é obrigatório.");
    } else {
      if (this.model.cpf && !UtilsService.validarDocumento(this.model.cpf)) {
        errors.push("O CPF/CNPJ informado é inválido.");
      }
    }

    if (!this.model?.idDepartamento) {
      errors.push("O campo 'Departamento' é obrigatório.");
    }

    if (!this.model?.banco?.trim()) {
      errors.push("O campo 'Banco' é obrigatório.");
    }

    if (!this.model?.agencia?.trim()) {
      errors.push("O campo 'Agência' é obrigatório.");
    }

    if (!this.model?.conta?.trim()) {
      errors.push("O campo 'Conta' é obrigatório.");
    }

    if (!this.model?.finalidade?.trim()) {
      errors.push("O campo 'Finalidade' é obrigatório.");
    }

    if (!this.model?.tipoRelatorio) {
      errors.push("O campo 'Tipo de Relatório' é obrigatório.");
    }

    if (!this.model?.moeda) {
      errors.push("O campo 'Moeda' é obrigatório.");
    }

    if (!this.model?.dataInicio) {
      errors.push("O campo 'Início da Viagem' é obrigatório.");
    }

    if (!this.model?.dataFim) {
      errors.push("O campo 'Fim da Viagem' é obrigatório.");
    }

    if (!this.model?.localViagem?.trim()) {
      errors.push("O campo 'Local' é obrigatório.");
    }

    if (!this.model?.centroCusto?.trim()) {
      errors.push("O campo 'Centro de Custo' é obrigatório.");
    }

    if (!this.model?.projeto?.trim()) {
      errors.push("O campo 'Projeto' é obrigatório.");
    }

    if (!this.model?.pca?.trim()) {
      errors.push("O campo 'PCA' é obrigatório.");
    }

    if (!this.model?.diariaViagem === undefined) {
      errors.push("O campo 'Diária de Viagem?' é obrigatório.");
    }

    if (!this.model?.adiantamento === undefined) {
      errors.push("O campo 'Pediu Adiantamento?' é obrigatório.");
    }

    if (this.model?.adiantamento) {
      if (!this.model?.valorAdiantamento) {
        errors.push("O campo 'Valor do Adiantamento' é obrigatório.");
      }

      if (this.model.tipoViagem > 1 && !this.model.saldoCartaoVtm) {
        errors.push("O campo 'Saldo Cartão VTM' é obrigatório.");
      }
    }

    if (this.model?.diariaViagem) {
      if (!this.model?.diarias) {
        errors.push("O campo 'Diárias' é obrigatório.");
      }
    }

    if (!this.model?.tipoAutorizacao) {
      errors.push("O campo 'Tipo de Autorização' é obrigatório.");
    }

    if (
      this.model?.tipoAutorizacao === 1 &&
      this.model?.documentos.filter((d) => d.idTipoDocumento === 9).length === 0
    ) {
      errors.push(
        "É obrigatório anexar uma autorização quando o tipo de autorização é 'Por Anexo'."
      );
    }

    if (this.model?.tipoAutorizacao === 2 && !this.model?.idGestor) {
      errors.push(
        "É obrigatório informar o gestor quando o tipo de autorização é 'Por Sistema'."
      );
    }

    return errors;
  }

  async save(isDraft: boolean, cancel: boolean = false) {
    let errors = this.validate();

    if (errors.length > 0) {
      this.alert(errors.join("\r\n"));
      return false;
    }

    if (this.isBusy) return;

    this.isBusy = true;

    // Definir valores de Km e Diária
    this.model.valorKm = this.rddvConfig.ValorKm;
    this.model.valorDiaria = this.travelTypes.find(
      (t) => t.idTipoViagem == this.model.tipoViagem
    ).valorDiaria;

    if(this.model.rascunho && !isDraft) {
      this.model.rascunho = false;
    }

    if(!this.model.cancelado && cancel) {
      this.model.cancelado = true;
    }

    try {
      const res = await this.rddv.UpsertRddv(this.model).toPromise();

      if (!res.success) {
        this.snack.open(res.message || "Ocorreu um erro ao salvar este relatório.", "OK");
        return;
      }

      this.router.navigateByUrl("/payments?view=" + this.returnTo);
    } catch (ex) {
      this.snack.open("Ocorreu um erro ao salvar este relatório.", "OK");
    } finally {
      this.isBusy = false;
    }
  }

  /*///////////////////////////////////////////////////////////////////*/

  showClone() {
    window.open(`/#/create-rddv/${this.reportId}?clone=true&returnTo=${this.returnTo}`, "_blank");
  }

  /*///////////////////////////////////////////////////////////////////*/

  fileChangeEvent(fileInput: any) {
    this.imageError = null;
    if (fileInput.target.files && fileInput.target.files[0]) {
      // Size Filter Bytes
      const max_size = 20971520;
      const allowed_types = ["image/png", "image/jpeg"];
      const max_height = 15200;
      const max_width = 25600;

      if (fileInput.target.files[0].size > max_size) {
        this.imageError = "Maximum size allowed is " + max_size / 1000 + "Mb";

        return false;
      }

      /*if (!allowed_types.find((f) => fileInput.target.files[0].type)) {
        this.imageError = "Only Images are allowed ( JPG | PNG )";
        return false;
      }*/
      const reader = new FileReader();
      reader.onload = (e: any) => {
        /*const image = new Image();
        image.src = e.target.result;
        image.onload = (rs) => {
          const img_height = rs.currentTarget["height"];
          const img_width = rs.currentTarget["width"];

          console.log(img_height, img_width);

          if (img_height > max_height && img_width > max_width) {
            this.imageError =
              "Maximum dimentions allowed " +
              max_height +
              "*" +
              max_width +
              "px";
            return false;
          } else {
            const imgBase64Path = e.target.result;

            let document = new RddvAttachmentModel();
            document.idUsuario = this.auth.CurrentUser.idUsuario;
            document.arquivo = imgBase64Path;
            document.dataCriacao = new Date();
            document.idRelatorio = this.reportId;
            document.indice = this.model.documentos.length + 1;
            document.nomeArquivo = fileInput.target.files[0].name;
            document.idTipoDocumento = this.selectedAttachmentType;
            document.caminhoArquivo = fileInput.target.files[0].type;

            this.model.documentos.push(document);

            fileInput.value = null;
            this.selectedAttachmentType = null;
            //this.cardImageBase64 = imgBase64Path;
            //this.isImageSaved = true;
            // this.previewImagePath = imgBase64Path;
          }
        };*/

        const imgBase64Path = e.target.result;

        let document = new RddvAttachmentModel();
        document.idUsuario = this.auth.CurrentUser.idUsuario;
        document.arquivo = imgBase64Path;
        document.dataCriacao = new Date();
        document.idRelatorio = this.reportId;
        document.indice = this.model.documentos.length + 1;
        document.nomeArquivo = fileInput.target.files[0].name;
        document.idTipoDocumento = this.selectedAttachmentType;
        document.caminhoArquivo = fileInput.target.files[0].type;

        this.model.documentos.push(document);

        fileInput.value = null;
        this.selectedAttachmentType = null;
      };

      reader.readAsDataURL(fileInput.target.files[0]);
    }
  }

  get validAttachments(): RddvAttachmentModel[] {
    return this.model.documentos.filter((x) => !x.excluido);
  }

  attachmentTypeName(id: number): string {
    let type = this.attachmentTypes.find((t) => t.id === id);

    if (type) return type.name;

    return "";
  }

  async launchPreview(file: RddvAttachmentModel) {
    const token = await this.previewTokenService.GetPreviewToken(file.idRelatorio, EnumRequestType.Rddv);
    
    if (!token) {
      this.snack.open("Não foi possível gerar o token de visualização.", "OK", { duration: 3000 });
      return;
    }

    const url = `${this.url.Rddv.PreviewAttachment}/${file.idRelatorio}/${file.idDocumentoRddv}?key=${token.previewKey}`;

    window.open(url, "_blank");
  }

  delete(file: RddvAttachmentModel) {
    if (file.idDocumentoRddv > 0) {
      file.excluido = true;
    } else {
      let ix = this.model.documentos.indexOf(file);
      this.model.documentos.splice(ix, 1);
    }
  }

  /*///////////////////////////////////////////////////////////////////*/

  newExpense() {
    if (this.isEditingExpense) return;

    this.isEditingExpense = true;
    this.isNewExpense = true;
    this.sourceExpense = null;

    let temp = new RddvExpenseModel();
    temp.idRelatorio = this.reportId;
    temp.dataDespesa = new Date();

    this.expenseModel = temp;
  }

  editExpense(expense: RddvExpenseModel) {
    if (this.isEditingExpense) return;

    this.isEditingExpense = true;
    this.isNewExpense = false;
    this.sourceExpense = expense;

    let temp = new RddvExpenseModel();
    Object.assign(temp, expense);

    this.expenseModel = temp;
  }

  cancelEditExpense() {
    if (!this.isEditingExpense) return;

    this.isEditingExpense = false;
    this.isNewExpense = false;
    this.sourceExpense = null;
    this.expenseModel = null;
  }

  saveExpense() {
    if (!this.isEditingExpense) return;

    if (this.isNewExpense) {
      this.model.despesas.push(this.expenseModel);
    } else {
      Object.assign(this.expenseModel, this.sourceExpense);
    }

    this.isEditingExpense = false;
    this.isNewExpense = false;
    this.sourceExpense = null;
    this.expenseModel = null;
  }

  removeExpense(expense: RddvExpenseModel) {
    if (!expense) return;

    let ix = this.model.despesas.indexOf(expense);

    if (ix >= 0) this.model.despesas.splice(ix, 1);
  }

  cloneExpense(expense: RddvExpenseModel) {
    if (!expense) return;

    let clone = {} as any;
    Object.assign(clone, expense);

    clone.idDespesaRddv = 0;

    this.model.despesas.push(clone as RddvExpenseModel);
  }

  async getConfig() {
    try {
      let res = await this.config.GetAll().toPromise();

      if (res.success) {
        this.configuration = res.data;

        this.rddvConfig = RddvConfigModel.FromConfiguracaoModel(
          this.configuration
        );
      }
    } catch (ex) {
      console.log(ex);
    }
  }

  calcularKm(expense: RddvExpenseModel) {
    expense.valor = expense.quantidade * this.rddvConfig.ValorKm;
  }

  ajustarDiarias() {
    if (!this.model?.dataInicio || !this.model?.dataFim) {
      return;
    }

    let diff = moment(this.model.dataFim).diff(this.model.dataInicio, "days");

    this.model.diarias = parseFloat(diff.toFixed(1)) + 1;
  }

  /* ================= Totalizadores ================= */

  getTotal(tipo: number): number {
    return this.model?.despesas
      .filter((d) => d.tipoDespesa === tipo)
      .map((x) => x.valor)
      .reduce((prev, curr) => prev + curr, 0);
  }

  get totalDiaria(): number {
    if (!this.model.diariaViagem) return 0;

    let valorDiaria;

    if (this.isEdit) {
      valorDiaria = this.model.valorDiaria;
    } else {
      let tipoViagem = this.travelTypes.find(
        (t) => t.idTipoViagem === this.model?.tipoViagem
      );

      if (!tipoViagem || !this.model.diariaViagem) return 0;

      valorDiaria = tipoViagem.valorDiaria;
    }

    let total = valorDiaria * this.model?.diarias;

    if (isNaN(total) || total < 0) {
      total = 0;
    }

    return total;
  }

  get subTotal(): number {
    return (
      this.model?.despesas
        .map((x) => x.valor)
        .reduce((prev, curr) => prev + curr, 0) + this.totalDiaria
    );
  }

  get totalAdiantamento(): number {
    let valor = this.model?.adiantamento ? this.model.valorAdiantamento : 0;

    if (!valor) return 0;
    else return valor;
  }

  get totalVtm(): number {
    let valor =
      this.model?.adiantamento && this.model?.tipoViagem > 1
        ? this.model.saldoCartaoVtm
        : 0;

    if (!valor) return 0;
    else return valor;
  }

  get totalAres(): number {
    return (this.subTotal - this.totalAdiantamento) + this.totalVtm;
  }

  /* ================= Totalizadores ================= */

  get assignee(): string {
    let userId = this.model?.idUsuarioAtribuido;

    let user = this.userList?.find(u => u.idUsuario === userId);

    if(user)
      return "Atribuído a " + user?.nome;
    else
      return "";
  }

  diariaViagemChanged() {
    if(!this.model.diariaViagem) {
      this.model.diarias = 0;
    }
    else {
      this.ajustarDiarias();
    }
  }

  adiantamentoChanged() {
    if(!this.model.adiantamento) {
      this.model.valorAdiantamento = 0;
    }
  }

  get isDownloadAvailable() {
    return this.model.idRelatorio > 0 && this.model.documentos?.filter(d => d.idDocumentoRddv > 0).length > 0;
  }

  async downloadAttachments() {
    const token = await this.previewTokenService.GetPreviewToken(this.model.idRelatorio, EnumRequestType.Rddv);

    if (!token) {
      this.snack.open("Não foi possível gerar o token de visualização.", "OK", { duration: 3000 });
      return;
    }

    const url = `${this.exportService.GetRddvAttachmentsUrl(this.model.idRelatorio)}?key=${token.previewKey}`;

    window.open(url, "_blank");
  }

  get isDraft() {
    return this.model.rascunho;
  }

  async cancel() {
    var res = await this.dialog.confirm("Tem certeza de que deseja cancelar este RDDV? Esta ação não pode ser desfeita.", "Cancelar RDDV").toPromise();
    
    if(res != EnumDialogResult.Yes) {
      return;
    }

    await this.save(false, true);
  }
}
