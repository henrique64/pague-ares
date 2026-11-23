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
import { AttachedDocumentModel } from "app/models/payment/attached-document.model";
import { PaymentRequestModel } from "app/models/payment/payment-request.model";
import { AuthService } from "app/services/auth.service";
import { BaseUrlService } from "app/services/base-url.service";
import { ExportService } from "app/services/export.service";
import { PaymentService } from "app/services/payment.service";
import { PreviewTokenService } from "app/services/preview-token.service";
import { ProviderService } from "app/services/provider.service";
import { UsersService } from "app/services/users.service";
import { UtilsService } from "app/services/utils.service";

@Component({
    selector: "create-request",
    templateUrl: "./create-request.component.html",
    styleUrls: ["./create-request.component.css"],
    standalone: false
})
export class CreateRequestComponent implements OnInit, OnDestroy {
  showAdm: boolean = false;

  requestId: number = 0;
  model: PaymentRequestModel;
  isEdit: boolean = false;
  isClone: boolean = false;

  isUserView: boolean = false;
  isReadOnly: boolean = false;
  isManagerReadOnly: boolean = false;
  isFinanceReadOnly: boolean = false;
  isAccountingReadOnly: boolean = false;
  isAccounting: boolean = false;

  imageError: string = "";

  isBusy: boolean = false;

  private subs = new Subscription();

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

  userList: UserListModel[] = [];
  managerList: UserListModel[] = [];

  returnTo: string = "0";

  constructor(
    private auth: AuthService,
    private router: Router,
    private thisRoute: ActivatedRoute,
    private payment: PaymentService,
    private snack: MatSnackBar,
    private users: UsersService,
    private url: BaseUrlService,
    private prov: ProviderService,
    private exportService: ExportService,
    private dialog: DialogService,
    private previewTokenService: PreviewTokenService
  ) {}

  ngOnDestroy() {
    this.subs.unsubscribe();
  }

  ngOnInit() {
    this.subs.add(this.thisRoute.params.subscribe(routeParams => {
      this.subs.add(this.thisRoute.queryParamMap.subscribe(queryParams => {
        if (routeParams["id"]) {
          this.requestId = routeParams["id"];
          this.isEdit = true;
        }
    
        if (queryParams.get("clone")) {
          this.isEdit = false;
          this.isClone = true;
        }

        if(queryParams.get("returnTo")) {
          this.returnTo = queryParams.get("returnTo");
        }
    
        this.getUserList();
        this.getManagerList();
        this.initForm();
      }))
    }))
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

  async getManagerList() {
    try {
      let res = await this.users.GetList("GES").toPromise();

      if (res.success) {
        this.managerList = res.data;
      }
    } catch (ex) {
      console.log(ex);
    }
  }

  async initForm() {
    if (!this.isEdit && !this.isClone) {
      this.model = new PaymentRequestModel();

      this.model.dataDocumento = new Date();
      this.model.dataVencimento = new Date();
      this.model.dataSolicitacao = new Date();
      this.model.documentos = [];
      this.model.tipoSolicitacao = 1;
      this.model.rascunho = true;
      this.model.cancelado = false;

      this.model.observacaoGestor = "";
      this.model.observacaoSetor = "";

      this.model.idUsuario = this.auth.CurrentUser.idUsuario;
      this.isUserView = true;
    } else {
      await this.getRequest();

      if (this.isClone) {
        this.clone();
      } else {
        this.isUserView =
          this.model.idUsuario === this.auth.CurrentUser.idUsuario;
        this.isReadOnly =
          !this.isUserView ||
          this.model.cancelado ||
          !(this.model.rascunho || this.model.statusGestor === null || this.model.statusGestor === 1);
        // Cada etapa só é editável enquanto NÃO decidida (status null ou 1). Uma vez
        // decidida (aprovada/negada = 2/3), trava — uma etapa anterior não pode ser
        // alterada depois que a seguinte já agiu.
        this.isManagerReadOnly =
          !this.auth.IsInRole(EnumFuncao.Gestor) ||
          !(this.model.statusGestor === null || this.model.statusGestor === 1);
        this.isFinanceReadOnly =
          !this.auth.IsInRole(EnumFuncao.Financeiro) ||
          !(this.model.statusFinanceiro === null || this.model.statusFinanceiro === 1);
        this.isAccountingReadOnly =
          !this.auth.IsInRole(EnumFuncao.Contabilidade) ||
          !(this.model.statusContabilidade === null || this.model.statusContabilidade === 1);
        this.isAccounting = this.auth.IsInRole(EnumFuncao.Contabilidade);
      }
    }
  }

  async getRequest(): Promise<boolean> {
    try {
      let req = await this.payment.GetRequest(this.requestId).toPromise();

      if (req.success) {
        this.model = req.data as PaymentRequestModel;

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
    this.model.idSolicitacao = 0;
    this.model.dataDocumento = new Date();
    this.model.dataVencimento = new Date();
    this.model.dataSolicitacao = new Date();
    this.model.documentos = [];
    this.model.tipoSolicitacao = 1;
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

    if (!this.model?.descricao?.trim()) {
      errors.push("O campo 'Descrição' é obrigatório.");
    }

    if (!this.model?.valor?.toString().trim()) {
      errors.push("O campo 'Valor' é obrigatório.");
    }

    if (!this.model?.centroCusto?.toString().trim()) {
      errors.push("O campo 'Centro de Custo' é obrigatório.");
    }

    if (!this.model?.projeto?.toString().trim()) {
      errors.push("O campo 'Projeto' é obrigatório.");
    }

    if (!this.model?.pca?.toString().trim()) {
      errors.push("O campo 'PCA' é obrigatório.");
    }

    if (!this.model?.numDocParceiro?.toString().trim()) {
      errors.push("O campo 'CPF/CNPJ' é obrigatório.");
    }

    if (
      this.model.numDocParceiro &&
      !UtilsService.validarDocumento(this.model.numDocParceiro)
    ) {
      errors.push("O CPF/CNPJ informado é inválido.");
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
    if (!isDraft && !cancel) {
      let errors = this.validate();

      if (errors.length > 0) {
        this.alert(errors.join("\r\n"));
        return false;
      }
    }

    if (this.isBusy) return;

    this.isBusy = true;

    if (this.model.idGestor) {
      let gestor = this.userList.find(
        (u) => u.idUsuario == this.model.idGestor
      );

      if (gestor)
        this.model.nomeGestor = gestor.nome;
    }

    if(this.model.rascunho && !isDraft && !cancel) {
      this.model.rascunho = false;
    }

    if(!this.model.cancelado && cancel) {
      this.model.cancelado = true;
    }

    try {
      const res = await this.payment.UpsertRequest(this.model).toPromise();

      if (!res.success) {
        this.snack.open(res.message || "Ocorreu um erro ao salvar esta solicitação.", "OK");
        return;
      }

      this.router.navigateByUrl("/payments?view=" + this.returnTo);
    } catch (ex) {
      this.snack.open("Ocorreu um erro ao salvar esta solicitação.", "OK");
    } finally {
      this.isBusy = false;
    }
  }

  /*///////////////////////////////////////////////////////////////////*/

  showClone() {
    window.open(`/#/create-request/${this.requestId}?clone=true&returnTo=${this.returnTo}`, "_blank");
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

      const reader = new FileReader();
      reader.onload = (e: any) => {
        const imgBase64Path = e.target.result;

        let document = new AttachedDocumentModel();
        document.idUsuario = this.auth.CurrentUser.idUsuario;
        document.arquivo = imgBase64Path;
        document.dataCriacao = new Date();
        document.idSolicitacao = this.requestId;
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

  get validAttachments(): AttachedDocumentModel[] {
    return this.model.documentos.filter((x) => !x.excluido);
  }

  attachmentTypeName(id: number): string {
    let type = this.attachmentTypes.find((t) => t.id === id);

    if (type) return type.name;

    return "";
  }

  async launchPreview(file: AttachedDocumentModel) {
    const token = await this.previewTokenService.GetPreviewToken(file.idSolicitacao, EnumRequestType.Solicitacao);

    if (!token) {
      this.snack.open("Não foi possível gerar o token de visualização.", "OK", { duration: 3000 });
      return;
    }

    const url = `${this.url.Payment.PreviewAttachment}/${file.idSolicitacao}/${file.idDocumentoSolicitacao}?key=${token.previewKey}`;

    window.open(url, "_blank");
  }

  delete(file: AttachedDocumentModel) {
    if (file.idDocumentoSolicitacao > 0) {
      file.excluido = true;
    } else {
      let ix = this.model.documentos.indexOf(file);
      this.model.documentos.splice(ix, 1);
    }
  }

  /*///////////////////////////////////////////////////////////////////*/

  get valueConfig(): any {
    return {
      prefix: this.model?.tipoPagamento === 1 ? "R$ " : "",
      thousands: ".",
      decimal: ",",
      align: "left",
    };
  }

  /*///////////////////////////////////////////////////////////////////*/

  async buscarFornecedor() {
    if (
      this.model.numDocParceiro &&
      UtilsService.validarDocumento(this.model.numDocParceiro)
    ) {
      try {
        let res = await this.prov
          .GetProviderByDocumentNumber(this.model.numDocParceiro)
          .toPromise();

        if (res.success) {
          let provider = res.data;

          if (provider) {
            this.model.nomeParceiro = provider.nome;
          }
        }
      } catch (ex) {
        this.snack.open("Ocorreu um erro ao buscar o fornecedor.", "OK", {
          duration: 3000,
        });
      }
    }
  }

  get assignee(): string {
    let userId = this.model?.idUsuarioAtribuido;

    let user = this.userList?.find(u => u.idUsuario === userId);

    if(user)
      return "Atribuído a " + user?.nome;
    else
      return "";
  }

  get isDraft() {
    return this.model.rascunho;
  }

  get isDownloadAvailable() {
    return this.model.idSolicitacao > 0 && this.model.documentos?.filter(d => d.idDocumentoSolicitacao > 0).length > 0;
  }

  async downloadAttachments() {
    const token = await this.previewTokenService.GetPreviewToken(this.model.idSolicitacao, EnumRequestType.Solicitacao);

    if (!token) {
      this.snack.open("Não foi possível gerar o token de visualização.", "OK", { duration: 3000 });
      return;
    }

    const url = `${this.exportService.GetPaymentAttachmentsUrl(this.model.idSolicitacao)}?key=${token.previewKey}`;

    window.open(url, "_blank");
  }

  async cancel() {
    var res = await this.dialog.confirm("Tem certeza de que deseja cancelar esta solicitação? Esta ação não pode ser desfeita.", "Cancelar Solicitação").toPromise();
    
    if(res != EnumDialogResult.Yes) {
      return;
    }

    await this.save(this.model.rascunho, true);
  }

  get isFinal(): boolean {
    return this.model?.statusGestor === 3 ||
           this.model?.statusFinanceiro === 3 ||
           this.model?.statusContabilidade === 3 ||
           this.model?.statusContabilidade === 2;
  }
}
