import { Component, OnDestroy, OnInit, QueryList, ViewChildren } from "@angular/core";
import { NgModel } from "@angular/forms";
import { HttpErrorResponse } from "@angular/common/http";
import { MatSnackBar } from "@angular/material/snack-bar";
import { MatEndDate, MatStartDate } from "@angular/material/datepicker";
import { Subject, Subscription, interval, of } from "rxjs";
import { catchError, exhaustMap, takeUntil } from "rxjs/operators";
import { ExportacaoService } from "app/services/exportacao.service";
import { UsersService } from "app/services/users.service";
import { DepartmentService } from "app/services/department.service";
import { UserListModel } from "app/models/authentication/UserList.model";
import { DepartamentoModel } from "app/models/department/departamento.model";
import { GenericResponse } from "app/models/GenericResponse";
import { ExportacaoJobModel } from "app/models/exportacao/exportacao-job.model";
import { FiltroExportacaoModel } from "app/models/exportacao/filtro-exportacao.model";
import { EnumStatusExportacao } from "app/models/exportacao/enum-status-exportacao";
import { EnumTipoTituloExportacao } from "app/models/exportacao/enum-tipo-titulo-exportacao";

// Valores do formulário. Datas são Date (seletor do Material); 0 ou vazio significa "sem filtro".
export interface FormularioExportacao {
  tipoTitulo: EnumTipoTituloExportacao;
  codigo: number | null;
  numeroDocumento: string;
  dataSolicitacaoInicio: Date | null;
  dataSolicitacaoFim: Date | null;
  dataVencimentoInicio: Date | null;
  dataVencimentoFim: Date | null;
  dataAprovacaoFinanceiroInicio: Date | null;
  dataAprovacaoFinanceiroFim: Date | null;
  idSolicitante: number;
  idAtribuido: number;
  idDepartamento: number;
  statusGestor: number;
  statusFinanceiro: number;
  statusContabil: number;
  nomeParceiro: string;
  documentoParceiro: string;
  tipoSolicitacao: number;
  tipoPagamento: number;
  formaPagamento: number;
  tipoRelatorio: number;
  incluirAnexos: boolean;
}

@Component({
  selector: "app-exportacoes",
  templateUrl: "./exportacoes.component.html",
  styleUrls: ["./exportacoes.component.css"],
  standalone: false,
})
export class ExportacoesComponent implements OnInit, OnDestroy {
  static readonly INTERVALO_POLLING_MS = 5000;

  readonly EnumStatus = EnumStatusExportacao;
  readonly EnumTipoTitulo = EnumTipoTituloExportacao;

  isBusy = false;
  isLoadingLista = false;
  form: FormularioExportacao = ExportacoesComponent.formularioVazio();
  jobs: ExportacaoJobModel[] = [];
  userList: UserListModel[] = [];
  departamentos: DepartamentoModel[] = [];

  // Todos os campos ligados por ngModel; serve para esvaziar o texto dos campos de data ao limpar o formulário.
  @ViewChildren(NgModel) private modelos?: QueryList<NgModel>;

  private pollingSub: Subscription | null = null;
  // Emite uma vez ao destruir o componente: nenhuma resposta tardia pode mexer na tela nem ligar o polling.
  private readonly destroy$ = new Subject<void>();
  private destruido = false;
  private iframeDownload: HTMLIFrameElement | null = null;

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

  // Mesmas opções da tela de solicitação (create-request).
  listTipoSolicitacao: any[] = [
    { id: 1, name: "Adiantamento" },
    { id: 2, name: "Pagamento" },
    { id: 3, name: "Movimento Almoxarifado" },
  ];

  listTipoPagamento: any[] = [
    { id: 1, name: "Nacional" },
    { id: 2, name: "Internacional" },
  ];

  listFormaPagamento: any[] = [
    { id: 1, name: "Boleto" },
    { id: 2, name: "Transferência Bancária" },
  ];

  listTipoRelatorio: any[] = [
    { id: 1, name: "Adiantamento" },
    { id: 2, name: "Prestação de Contas" },
    { id: 3, name: "Reembolso" },
  ];

  constructor(
    private exportacao: ExportacaoService,
    private users: UsersService,
    private department: DepartmentService,
    private snackbar: MatSnackBar
  ) {}

  static formularioVazio(): FormularioExportacao {
    return {
      tipoTitulo: EnumTipoTituloExportacao.Ambos,
      codigo: null,
      numeroDocumento: "",
      dataSolicitacaoInicio: null,
      dataSolicitacaoFim: null,
      dataVencimentoInicio: null,
      dataVencimentoFim: null,
      dataAprovacaoFinanceiroInicio: null,
      dataAprovacaoFinanceiroFim: null,
      idSolicitante: 0,
      idAtribuido: 0,
      idDepartamento: 0,
      statusGestor: 0,
      statusFinanceiro: 0,
      statusContabil: 0,
      nomeParceiro: "",
      documentoParceiro: "",
      tipoSolicitacao: 0,
      tipoPagamento: 0,
      formaPagamento: 0,
      tipoRelatorio: 0,
      incluirAnexos: true,
    };
  }

  ngOnInit() {
    this.carregarCombos();
    this.carregarLista();
  }

  ngOnDestroy() {
    this.destruido = true;
    this.destroy$.next();
    this.destroy$.complete();
    this.pararPolling();
    this.removerIframeDownload();
  }

  // ---------- Formulário ----------

  // Campos que só existem em Solicitação ficam desabilitados quando o tipo escolhido é RDDV.
  get camposSolicitacaoDesabilitados(): boolean {
    return this.form.tipoTitulo === EnumTipoTituloExportacao.Rddv;
  }

  // Campos que só existem em RDDV ficam desabilitados quando o tipo escolhido é Solicitação.
  get camposRddvDesabilitados(): boolean {
    return this.form.tipoTitulo === EnumTipoTituloExportacao.Solicitacao;
  }

  montarFiltro(): FiltroExportacaoModel {
    const f = this.form;
    const filtro: FiltroExportacaoModel = {
      tipoTitulo: f.tipoTitulo,
      incluirAnexos: f.incluirAnexos,
    };

    const comoInteiro = (v: any): number | null => {
      const n = Number(v);
      return v !== null && v !== undefined && v !== "" && Number.isInteger(n) && n > 0 ? n : null;
    };
    const comoTexto = (v: string | null | undefined): string | null => {
      const t = (v ?? "").trim();
      return t.length > 0 ? t : null;
    };

    const codigo = comoInteiro(f.codigo);
    if (codigo !== null) filtro.codigo = codigo;

    const inteiros: [keyof FiltroExportacaoModel, any][] = [
      ["idSolicitante", f.idSolicitante],
      ["idAtribuido", f.idAtribuido],
      ["idDepartamento", f.idDepartamento],
      ["statusGestor", f.statusGestor],
      ["statusFinanceiro", f.statusFinanceiro],
      ["statusContabil", f.statusContabil],
    ];

    if (!this.camposSolicitacaoDesabilitados) {
      inteiros.push(
        ["tipoSolicitacao", f.tipoSolicitacao],
        ["tipoPagamento", f.tipoPagamento],
        ["formaPagamento", f.formaPagamento]
      );

      const numeroDocumento = comoTexto(f.numeroDocumento);
      if (numeroDocumento !== null) filtro.numeroDocumento = numeroDocumento;
    }

    if (!this.camposRddvDesabilitados) {
      inteiros.push(["tipoRelatorio", f.tipoRelatorio]);
    }

    for (const [campo, valor] of inteiros) {
      const n = comoInteiro(valor);
      if (n !== null) (filtro as any)[campo] = n;
    }

    const nomeParceiro = comoTexto(f.nomeParceiro);
    if (nomeParceiro !== null) filtro.nomeParceiro = nomeParceiro;

    const documentoParceiro = comoTexto(f.documentoParceiro);
    if (documentoParceiro !== null) filtro.documentoParceiro = documentoParceiro;

    const datas: [keyof FiltroExportacaoModel, Date | null][] = [
      ["dataSolicitacaoInicio", f.dataSolicitacaoInicio],
      ["dataSolicitacaoFim", f.dataSolicitacaoFim],
      ["dataVencimentoInicio", f.dataVencimentoInicio],
      ["dataVencimentoFim", f.dataVencimentoFim],
      ["dataAprovacaoFinanceiroInicio", f.dataAprovacaoFinanceiroInicio],
      ["dataAprovacaoFinanceiroFim", f.dataAprovacaoFinanceiroFim],
    ];

    for (const [campo, data] of datas) {
      const texto = ExportacoesComponent.formatarData(data);
      if (texto !== null) (filtro as any)[campo] = texto;
    }

    return filtro;
  }

  // Data do calendário (hora local) para "yyyy-MM-dd", sem passar por UTC.
  static formatarData(data: Date | string | null | undefined): string | null {
    if (!data) return null;

    const d = data instanceof Date ? data : new Date(data);
    if (isNaN(d.getTime())) return null;

    const mm = String(d.getMonth() + 1).padStart(2, "0");
    const dd = String(d.getDate()).padStart(2, "0");
    return `${d.getFullYear()}-${mm}-${dd}`;
  }

  limparFormulario() {
    this.form = ExportacoesComponent.formularioVazio();
    this.limparTextoDosCamposDeData();
  }

  // Zerar o modelo não basta para os campos de data: o texto digitado que não virou data válida (ex.: 31/02/2025)
  // nunca chegou ao modelo, então o ngModel não enxerga mudança e deixa o texto no campo. Os campos são esvaziados
  // direto, e o erro de "data inválida" que esse texto deixou é recalculado.
  private limparTextoDosCamposDeData() {
    this.modelos?.forEach((modelo) => {
      const campo = modelo.valueAccessor;
      if (campo instanceof MatStartDate || campo instanceof MatEndDate) {
        campo.writeValue(null);
        modelo.control.updateValueAndValidity({ emitEvent: false });
      }
    });
  }

  gerarExportacao() {
    if (this.isBusy) return;

    this.isBusy = true;

    this.exportacao.criar(this.montarFiltro()).pipe(takeUntil(this.destroy$)).subscribe({
      next: (res) => {
        this.isBusy = false;

        if (res?.success) {
          this.snackbar.open(
            "Exportação solicitada. Ela aparece na lista abaixo e você pode baixar quando ficar pronta.",
            "OK",
            { duration: 5000 }
          );
          this.carregarLista();
        } else {
          this.snackbar.open(res?.message || "Não foi possível solicitar a exportação.", "OK", { duration: 6000 });
        }
      },
      error: (err) => {
        this.isBusy = false;
        this.snackbar.open(
          this.mensagemDeErro(err, "Não foi possível solicitar a exportação. Tente novamente."),
          "OK",
          { duration: 6000 }
        );
      },
    });
  }

  // ---------- Lista e polling ----------

  carregarLista() {
    this.isLoadingLista = true;

    this.exportacao
      .listar()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          this.isLoadingLista = false;
          this.aplicarLista(res);
        },
        error: () => {
          this.isLoadingLista = false;
          this.snackbar.open("Não foi possível carregar as exportações. Tente novamente.", "OK", { duration: 4000 });
        },
      });
  }

  private aplicarLista(res: GenericResponse<ExportacaoJobModel[]> | null) {
    if (res?.success && Array.isArray(res.data)) {
      this.jobs = res.data;
    }

    this.atualizarPolling();
  }

  get temJobAtivo(): boolean {
    return this.jobs.some(
      (j) => j.status === EnumStatusExportacao.Pendente || j.status === EnumStatusExportacao.Processando
    );
  }

  get pollingAtivo(): boolean {
    return this.pollingSub !== null;
  }

  private atualizarPolling() {
    if (this.temJobAtivo) {
      this.iniciarPolling();
    } else {
      this.pararPolling();
    }
  }

  private iniciarPolling() {
    if (this.pollingSub || this.destruido) return;

    // exhaustMap: se uma consulta ainda não voltou, o tique seguinte é ignorado.
    this.pollingSub = interval(ExportacoesComponent.INTERVALO_POLLING_MS)
      .pipe(
        exhaustMap(() => this.exportacao.listar().pipe(catchError(() => of(null)))),
        takeUntil(this.destroy$)
      )
      .subscribe((res) => this.aplicarLista(res));
  }

  private pararPolling() {
    this.pollingSub?.unsubscribe();
    this.pollingSub = null;
  }

  // ---------- Ações da lista ----------

  podeBaixar(job: ExportacaoJobModel): boolean {
    return job.status === EnumStatusExportacao.Concluido;
  }

  podeCancelar(job: ExportacaoJobModel): boolean {
    return job.status === EnumStatusExportacao.Pendente || job.status === EnumStatusExportacao.Processando;
  }

  podeExcluir(job: ExportacaoJobModel): boolean {
    return job.status === EnumStatusExportacao.Concluido;
  }

  baixar(job: ExportacaoJobModel) {
    if (this.isBusy) return;

    this.isBusy = true;

    this.exportacao.gerarLinkDownload(job.id).pipe(takeUntil(this.destroy$)).subscribe({
      next: (res) => {
        this.isBusy = false;

        if (res?.success && res.data?.token) {
          this.baixarArquivo(this.exportacao.urlDownload(res.data.token));
        } else {
          this.snackbar.open(res?.message || "Não foi possível iniciar o download.", "OK", { duration: 5000 });
        }
      },
      error: (err) => {
        this.isBusy = false;
        this.snackbar.open(this.mensagemDeErro(err, "Não foi possível iniciar o download. Tente novamente."), "OK", {
          duration: 5000,
        });
        // O arquivo pode ter expirado: atualiza a lista para refletir o estado real.
        this.carregarLista();
      },
    });
  }

  cancelar(job: ExportacaoJobModel) {
    this.removerJob(
      job,
      "Cancelar esta exportação?",
      "Cancelamento solicitado.",
      "Não foi possível cancelar a exportação."
    );
  }

  excluir(job: ExportacaoJobModel) {
    this.removerJob(
      job,
      "Excluir o arquivo desta exportação? Depois disso não será possível baixá-lo.",
      "Arquivo excluído.",
      "Não foi possível excluir a exportação."
    );
  }

  private removerJob(job: ExportacaoJobModel, pergunta: string, sucesso: string, falha: string) {
    if (this.isBusy || !this.confirmar(pergunta)) return;

    this.isBusy = true;

    this.exportacao.cancelar(job.id).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.isBusy = false;
        this.snackbar.open(sucesso, "OK", { duration: 3000 });
        this.carregarLista();
      },
      error: (err) => {
        this.isBusy = false;
        this.snackbar.open(this.mensagemDeErro(err, falha), "OK", { duration: 5000 });
        this.carregarLista();
      },
    });
  }

  // O download vai por um iframe oculto, e não por navegação da aba: se o token for recusado
  // (reciclagem do pool, 401/410), o JSON de erro aparece dentro do iframe e a tela segue intacta.
  // Um arquivo aceito (Content-Disposition: attachment) vira download e não dispara "load"; já um
  // documento devolvido pelo servidor dispara, e é assim que a falha é detectada. Nada passa pela
  // memória do app: o navegador baixa em streaming.
  protected baixarArquivo(url: string) {
    this.removerIframeDownload();

    const frame = document.createElement("iframe");
    frame.style.display = "none";
    frame.tabIndex = -1;
    frame.title = "Download da exportação";
    frame.setAttribute("aria-hidden", "true");
    frame.src = url;
    frame.addEventListener("load", () => {
      if (this.iframeDownload !== frame || ExportacoesComponent.ehPaginaEmBranco(frame)) return;

      this.removerIframeDownload();
      this.snackbar.open(
        "Não foi possível baixar o arquivo. O link expira rápido e só vale uma vez: Gere o link de novo clicando em Baixar.",
        "OK",
        { duration: 8000 }
      );
    });

    this.iframeDownload = frame;
    document.body.appendChild(frame);
  }

  // Alguns navegadores disparam um "load" inicial do about:blank; ele não é resposta do servidor.
  private static ehPaginaEmBranco(frame: HTMLIFrameElement): boolean {
    try {
      return frame.contentWindow?.location.href === "about:blank";
    } catch {
      return false; // origem diferente: é resposta do servidor
    }
  }

  private removerIframeDownload() {
    this.iframeDownload?.remove();
    this.iframeDownload = null;
  }

  protected confirmar(pergunta: string): boolean {
    return window.confirm(pergunta);
  }

  // ---------- Apresentação ----------

  statusClasse(job: ExportacaoJobModel): string {
    switch (job.status) {
      case EnumStatusExportacao.Concluido:
        return "status-concluida";
      case EnumStatusExportacao.Falhou:
        return "status-falhou";
      case EnumStatusExportacao.Pendente:
      case EnumStatusExportacao.Processando:
        return "status-andamento";
      default:
        return "status-inativa";
    }
  }

  // O servidor grava datas em UTC; a serialização pode vir sem o "Z", então ele é acrescentado.
  paraData(valor?: string | null): Date | null {
    if (!valor) return null;

    const temFuso = /(Z|[+-]\d{2}:?\d{2})$/i.test(valor);
    const d = new Date(temFuso ? valor : valor + "Z");
    return isNaN(d.getTime()) ? null : d;
  }

  tamanhoLegivel(bytes?: number | null): string {
    if (bytes === null || bytes === undefined) return "";

    const unidades = ["B", "KB", "MB", "GB", "TB"];
    let valor = bytes;
    let i = 0;

    while (valor >= 1024 && i < unidades.length - 1) {
      valor /= 1024;
      i++;
    }

    const texto = i === 0 ? String(Math.round(valor)) : valor.toFixed(1).replace(".", ",");
    return `${texto} ${unidades[i]}`;
  }

  resumoFiltros(job: ExportacaoJobModel): string {
    const f = job.filtros;
    if (!f) return "";

    const partes: string[] = [];
    const tipo = f.tipoTitulo === EnumTipoTituloExportacao.Solicitacao ? "Solicitações"
      : f.tipoTitulo === EnumTipoTituloExportacao.Rddv ? "RDDVs" : "Solicitações e RDDVs";
    partes.push(tipo);

    if (f.codigo) partes.push(`Código ${f.codigo}`);
    if (f.numeroDocumento) partes.push(`Documento ${f.numeroDocumento}`);

    const periodo = (rotulo: string, ini?: string | null, fim?: string | null) => {
      if (!ini && !fim) return;
      partes.push(`${rotulo}: ${this.dataCurta(ini) || "…"} a ${this.dataCurta(fim) || "…"}`);
    };
    periodo("Solicitação", f.dataSolicitacaoInicio, f.dataSolicitacaoFim);
    periodo("Vencimento", f.dataVencimentoInicio, f.dataVencimentoFim);
    periodo("Aprovação financeiro", f.dataAprovacaoFinanceiroInicio, f.dataAprovacaoFinanceiroFim);

    if (f.idSolicitante) partes.push(`Solicitante: ${this.nomeUsuario(f.idSolicitante)}`);
    if (f.idAtribuido) partes.push(`Atribuído a: ${this.nomeUsuario(f.idAtribuido)}`);
    if (f.idDepartamento) {
      const dep = this.departamentos.find((d) => d.idDepartamento === f.idDepartamento);
      partes.push(`Departamento: ${dep?.nome ?? f.idDepartamento}`);
    }
    if (f.statusGestor) partes.push(`Gestor: ${this.rotulo(this.listStatusGestor, f.statusGestor)}`);
    if (f.statusFinanceiro) partes.push(`Financeiro: ${this.rotulo(this.listStatusFinanceiro, f.statusFinanceiro)}`);
    if (f.statusContabil) partes.push(`Contabilidade: ${this.rotulo(this.listStatusContabil, f.statusContabil)}`);
    if (f.nomeParceiro) partes.push(`Nome: ${f.nomeParceiro}`);
    if (f.documentoParceiro) partes.push(`CPF/CNPJ: ${f.documentoParceiro}`);
    if (f.tipoSolicitacao) partes.push(`Tipo: ${this.rotulo(this.listTipoSolicitacao, f.tipoSolicitacao)}`);
    if (f.tipoPagamento) partes.push(`Pagamento: ${this.rotulo(this.listTipoPagamento, f.tipoPagamento)}`);
    if (f.formaPagamento) partes.push(`Forma: ${this.rotulo(this.listFormaPagamento, f.formaPagamento)}`);
    if (f.tipoRelatorio) partes.push(`Relatório: ${this.rotulo(this.listTipoRelatorio, f.tipoRelatorio)}`);
    partes.push(f.incluirAnexos ? "Com anexos" : "Sem anexos");

    return partes.join(" · ");
  }

  private rotulo(lista: any[], id: number): string {
    return lista.find((i) => i.id === id)?.name ?? String(id);
  }

  private nomeUsuario(id: number): string {
    return this.userList.find((u) => u.idUsuario === id)?.nome ?? String(id);
  }

  private dataCurta(iso?: string | null): string {
    if (!iso) return "";

    const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
    return m ? `${m[3]}/${m[2]}/${m[1]}` : iso;
  }

  // Usa a mensagem amigável do servidor (envelope GenericResponse) quando existir.
  private mensagemDeErro(err: any, padrao: string): string {
    if (err instanceof HttpErrorResponse) {
      const msg = err.error?.message;
      if (typeof msg === "string" && msg.trim().length > 0) return msg;
    }

    return padrao;
  }

  private async carregarCombos() {
    try {
      const res = await this.users.GetList().toPromise();
      if (res?.success) this.userList = res.data ?? [];
    } catch (ex) {
      console.log(ex);
    }

    try {
      const res = await this.department.GetAll().toPromise();
      if (res?.success) this.departamentos = res.data ?? [];
    } catch (ex) {
      console.log(ex);
    }
  }
}
