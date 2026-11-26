import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { PaymentsComponent } from './payments.component';
import { AuthService } from 'app/services/auth.service';
import { PaymentService } from 'app/services/payment.service';
import { UsersService } from 'app/services/users.service';
import { ExportService } from 'app/services/export.service';
import { PreviewTokenService } from 'app/services/preview-token.service';
import { EnumPaymentListViewMode } from 'app/models/payment/enum-payment-list-view';

describe('PaymentsComponent', () => {
  let component: PaymentsComponent;
  let fixture: ComponentFixture<PaymentsComponent>;

  beforeEach(async () => {
    const authSpy = jasmine.createSpyObj('AuthService', ['IsInRole'], { CurrentUser: { idUsuario: 6 } });
    authSpy.IsInRole.and.returnValue(false);
    const paymentSpy = jasmine.createSpyObj('PaymentService', ['GetFilteredList']);
    const usersSpy = jasmine.createSpyObj('UsersService', ['GetList']);
    usersSpy.GetList.and.returnValue(of({ success: true, message: '', data: [], records: 0, pages: 0, page: 1 }));

    await TestBed.configureTestingModule({
      declarations: [PaymentsComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        { provide: AuthService, useValue: authSpy },
        { provide: PaymentService, useValue: paymentSpy },
        { provide: UsersService, useValue: usersSpy },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate', 'navigateByUrl']) },
        { provide: ActivatedRoute, useValue: { params: of({}), queryParamMap: of({ get: () => null }) } },
        { provide: ExportService, useValue: {} },
        { provide: PreviewTokenService, useValue: {} },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj('MatSnackBar', ['open']) }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(PaymentsComponent);
    component = fixture.componentInstance;
  });

  it('solicitante só pode usar MyView', () => {
    component.isAdmin = false;
    component.isManager = false;
    component.isFinancial = false;
    component.isAccounting = false;

    expect(component.isViewModeAllowed(EnumPaymentListViewMode.MyView)).toBeTrue();
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.ManagerView)).toBeFalse();
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.FinancialView)).toBeFalse();
  });

  it('gestor pode usar ManagerView', () => {
    component.isManager = true;
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.ManagerView)).toBeTrue();
  });

  it('financeiro pode usar FinancialView', () => {
    component.isFinancial = true;
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.FinancialView)).toBeTrue();
  });

  it('contabilidade pode usar FinancialView', () => {
    component.isAccounting = true;
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.FinancialView)).toBeTrue();
  });

  it('admin pode usar todas as visões', () => {
    component.isAdmin = true;
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.MyView)).toBeTrue();
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.ManagerView)).toBeTrue();
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.FinancialView)).toBeTrue();
  });

  it('allowExport: contabilidade na visão financeira não exporta', () => {
    component.isAccounting = true;
    component.isFinancial = false;
    component.isAdmin = false;
    component.viewMode = EnumPaymentListViewMode.FinancialView;
    expect(component.allowExport).toBeFalse();
  });

  it('allowExport: financeiro na visão financeira exporta', () => {
    component.isFinancial = true;
    component.viewMode = EnumPaymentListViewMode.FinancialView;
    expect(component.allowExport).toBeTrue();
  });

  it('allowExport: administrador na visão financeira exporta', () => {
    component.isAdmin = true;
    component.viewMode = EnumPaymentListViewMode.FinancialView;
    expect(component.allowExport).toBeTrue();
  });

  it('allowExport: financeiro fora da visão financeira não exporta', () => {
    component.isFinancial = true;
    component.viewMode = EnumPaymentListViewMode.MyView;
    expect(component.allowExport).toBeFalse();
  });

  describe("tela renderizada: rótulos, ordenação e Exportar planilha (achados 6, 7 e 8)", () => {
    const abas = ["Minhas Solicitações", "Gestor", "Financeiro/Contábil"];

    function aba(rotulo: string): HTMLElement {
      const el = fixture.nativeElement.querySelector(`mat-tab[label="${rotulo}"]`);
      expect(el).withContext("aba " + rotulo).not.toBeNull();
      return el as HTMLElement;
    }

    function cabecalhos(rotulo: string): string[] {
      return Array.from(aba(rotulo).querySelectorAll("thead th")).map(th =>
        (th.textContent ?? "").replace(/\s+/g, " ").trim());
    }

    function rotulosDosFiltros(): string[] {
      const filtros = fixture.nativeElement.querySelector("mat-card") as HTMLElement;
      return Array.from(filtros.querySelectorAll("mat-label")).map(l => (l.textContent ?? "").trim());
    }

    beforeEach(() => {
      localStorage.clear();
      TestBed.inject(AuthService).IsInRole = jasmine.createSpy("IsInRole").and.returnValue(true);
      (TestBed.inject(PaymentService).GetFilteredList as jasmine.Spy).and.returnValue(
        of({ success: true, message: "", data: [], records: 0, pages: 1, page: 1 }));
      component.viewMode = EnumPaymentListViewMode.FinancialView;
    });

    afterEach(() => localStorage.clear());

    it("com ordenação por data de inclusão, a seta aparece no cabeçalho \"Data de inclusão\" e não no de vencimento, em todas as abas", async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      component.myFilter.sortField = "dataSolicitacao";
      component.myFilter.sortOrder = "asc";
      fixture.detectChanges();

      for (const rotulo of abas) {
        const ths = Array.from(aba(rotulo).querySelectorAll("thead th")) as HTMLElement[];
        const inclusao = ths.find(th => /^Data (de )?inclus/i.test((th.textContent ?? "").trim()));
        const vencimento = ths.find(th => /^Data (de )?vencimento/i.test((th.textContent ?? "").trim()));
        expect(inclusao).withContext(rotulo + ": coluna Data de inclusão").toBeDefined();
        expect(vencimento).withContext(rotulo + ": coluna Data de vencimento").toBeDefined();
        expect(inclusao!.querySelector(".fa-sort-asc")).withContext(rotulo + ": seta em Data de inclusão").not.toBeNull();
        expect(vencimento!.querySelector(".fa-sort-asc, .fa-sort-desc")).withContext(rotulo + ": sem seta em Data de vencimento").toBeNull();
      }

      component.myFilter.sortField = "dataVencimento";
      component.myFilter.sortOrder = "desc";
      fixture.detectChanges();

      for (const rotulo of abas) {
        const ths = Array.from(aba(rotulo).querySelectorAll("thead th")) as HTMLElement[];
        const inclusao = ths.find(th => /^Data (de )?inclus/i.test((th.textContent ?? "").trim()));
        const vencimento = ths.find(th => /^Data (de )?vencimento/i.test((th.textContent ?? "").trim()));
        expect(vencimento!.querySelector(".fa-sort-desc")).withContext(rotulo + ": seta em Data de vencimento").not.toBeNull();
        expect(inclusao!.querySelector(".fa-sort-asc, .fa-sort-desc")).withContext(rotulo + ": sem seta em Data de inclusão").toBeNull();
      }
    });

    it("o botão \"Exportar planilha\" tem a dica dizendo que os anexos ficam em Exportações", async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      const botoes = Array.from(fixture.nativeElement.querySelectorAll("button")) as HTMLElement[];
      const exportar = botoes.filter(b => (b.textContent ?? "").trim().startsWith("Exportar"));

      expect(exportar.length).toBe(1);
      expect((exportar[0].textContent ?? "").trim()).toBe("Exportar planilha");
      expect(exportar[0].getAttribute("matTooltip")).toBe(
        "Baixa a lista desta aba em Excel (Pagamentos.xlsx), sem anexos. Os anexos ficam no menu Exportações.");
    });

    it("cabeçalhos da aba Financeiro/Contábil em caixa de frase, com os três status pelo nome da convenção", async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(cabecalhos("Financeiro/Contábil")).toEqual([
        "Ações", "Código", "Tipo", "Data de inclusão", "Data de vencimento", "Nº do documento",
        "Solicitante", "Parceiro", "Valor", "Status gestor", "Status contábil", "Status financeiro"]);
    });

    it("cabeçalhos das abas Minhas Solicitações e Gestor pela convenção de rótulos", async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(cabecalhos("Minhas Solicitações")).toEqual([
        "Ações", "Código", "Tipo", "Data de inclusão", "Data de vencimento", "Status gestor",
        "Status financeiro", "Status contábil", "Solicitante", "Parceiro", "Valor", "Descrição"]);
      expect(cabecalhos("Gestor")).toEqual([
        "Ações", "Código", "Tipo", "Data de inclusão", "Data de vencimento", "Nº do documento",
        "Solicitante", "Descrição", "Valor bruto", "Status gestor", "Status financeiro", "Obs. gestor"]);
    });

    it("linhas de RDDV: Nº do documento vazio e Parceiro = solicitante, sem o \"#\" da view, nas três abas; Solicitação não muda", async () => {
      const base = {
        dataSolicitacao: new Date(2026, 10, 1), dataVencimento: null, usuarioAtribuido: null, idUsuarioAtribuido: null,
        valor: 10, observacaoGestor: "", idStatusGestor: null, idStatusFinanceiro: null, idStatusContabilidade: null,
        statusGestor: "", statusFinanceiro: "", statusContabilidade: "", idGestor: null, aprovadoSetor: null,
        aprovadoGestor: null, rascunho: false, cancelado: false
      };
      const sol = { ...base, codigo: 11, idTipo: 0, tipo: "Pagamento", numeroDocumento: "NF-77", nomeSolicitante: "Maria Solicitante",
        idSolicitante: 6, nomeParceiro: "Fornecedor X", descricao: "Compra" };
      const rddv = { ...base, codigo: 45, idTipo: 1, tipo: "RDDV - Adiantamento", numeroDocumento: "#", nomeSolicitante: "João Viajante",
        idSolicitante: 7, nomeParceiro: "#", descricao: "#" };
      (TestBed.inject(PaymentService).GetFilteredList as jasmine.Spy).and.returnValue(
        of({ success: true, message: "", data: [sol, rddv], records: 2, pages: 1, page: 1 }));

      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      for (const rotulo of abas) {
        const ths = cabecalhos(rotulo);
        const linhas = Array.from(aba(rotulo).querySelectorAll("tbody tr")).filter(tr => tr.querySelector("td")) as HTMLElement[];
        const celula = (codigo: number, coluna: string) => {
          const tr = linhas.find(l => (l.querySelectorAll("td")[1].textContent ?? "").trim() === String(codigo))!;
          return (tr.querySelectorAll("td")[ths.indexOf(coluna)].textContent ?? "").trim();
        };
        expect(linhas.length).withContext(rotulo + ": linhas").toBe(2);
        if (ths.includes("Nº do documento")) {
          expect(celula(45, "Nº do documento")).withContext(rotulo + ": RDDV, Nº do documento").toBe("");
          expect(celula(11, "Nº do documento")).withContext(rotulo + ": SOL, Nº do documento").toBe("NF-77");
        }
        if (ths.includes("Parceiro")) {
          expect(celula(45, "Parceiro")).withContext(rotulo + ": RDDV, Parceiro").toBe("João Viajante");
          expect(celula(11, "Parceiro")).withContext(rotulo + ": SOL, Parceiro").toBe("Fornecedor X");
        }
        if (ths.includes("Descrição")) {
          expect(celula(45, "Descrição")).withContext(rotulo + ": RDDV, Descrição").toBe("");
          expect(celula(11, "Descrição")).withContext(rotulo + ": SOL, Descrição").toBe("Compra");
        }
      }
    });

    it("rótulos dos filtros e botões em caixa de frase, sem placeholder em Title Case", async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(rotulosDosFiltros()).toEqual([
        "Nº do documento", "Código", "Parceiro", "Solicitante", "Atribuído a", "Período da solicitação",
        "Período de vencimento", "Status gestor", "Status financeiro", "Status contábil"]);

      const marcados = Array.from(fixture.nativeElement.querySelectorAll("mat-card [placeholder]"))
        .map(e => (e as HTMLElement).getAttribute("placeholder"));
      expect(marcados).not.toContain("Status Gestor");
      expect(marcados).not.toContain("Status Financeiro");
      expect(marcados).not.toContain("Status Contabilidade");
      expect(marcados).toContain("Status gestor");
      expect(marcados).toContain("Status financeiro");
      expect(marcados).toContain("Status contábil");

      const textos = Array.from(fixture.nativeElement.querySelectorAll("mat-card button"))
        .map(b => ((b as HTMLElement).textContent ?? "").trim());
      expect(textos).toContain("Limpar filtros");
      expect(textos).toContain("Solicitar pagamento");
      expect(textos).toContain("Solicitar RDDV");
      expect(textos).not.toContain("Limpar Filtros");
    });
  });
});
