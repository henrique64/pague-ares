import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { Subject, of, throwError } from 'rxjs';
import { PROVIDERS_DATA_PT_BR } from 'app/core/date/pt-br-date-adapter';
import { ExportacoesComponent } from './exportacoes.component';
import { ExportacaoService } from 'app/services/exportacao.service';
import { UsersService } from 'app/services/users.service';
import { DepartmentService } from 'app/services/department.service';
import { ExportacaoJobModel } from 'app/models/exportacao/exportacao-job.model';
import { EnumStatusExportacao } from 'app/models/exportacao/enum-status-exportacao';
import { EnumTipoTituloExportacao } from 'app/models/exportacao/enum-tipo-titulo-exportacao';

function envelope<T>(data: T) {
  return { success: true, message: '', data, records: 0, pages: 0, page: 1 };
}

function job(id: number, status: EnumStatusExportacao, extra: Partial<ExportacaoJobModel> = {}): ExportacaoJobModel {
  return {
    id,
    status,
    statusDescricao: 'x',
    dataCriacao: '2026-11-25T14:00:00',
    totalTitulos: 10,
    titulosProcessados: 0,
    percentual: 0,
    totalAnexos: 0,
    filtros: { tipoTitulo: EnumTipoTituloExportacao.Ambos, incluirAnexos: true },
    ...extra,
  };
}

describe('ExportacoesComponent', () => {
  let fixture: ComponentFixture<ExportacoesComponent>;
  let component: ExportacoesComponent;
  let exportacao: jasmine.SpyObj<ExportacaoService>;
  let snackbar: jasmine.SpyObj<MatSnackBar>;
  let jobsAtuais: ExportacaoJobModel[];

  beforeEach(async () => {
    jobsAtuais = [];
    exportacao = jasmine.createSpyObj('ExportacaoService', ['criar', 'listar', 'obter', 'gerarLinkDownload', 'urlDownload', 'cancelar']);
    exportacao.listar.and.callFake(() => of(envelope(jobsAtuais)));
    exportacao.urlDownload.and.callFake((t: string) => 'http://api/exportacao/download?token=' + t);

    const users = jasmine.createSpyObj('UsersService', ['GetList']);
    users.GetList.and.returnValue(of(envelope([])));
    const dept = jasmine.createSpyObj('DepartmentService', ['GetAll']);
    dept.GetAll.and.returnValue(of(envelope([])));
    snackbar = jasmine.createSpyObj('MatSnackBar', ['open']);

    await TestBed.configureTestingModule({
      declarations: [ExportacoesComponent],
      imports: [
        FormsModule, NoopAnimationsModule, MatFormFieldModule, MatInputModule, MatSelectModule,
        MatCheckboxModule, MatDatepickerModule, MatNativeDateModule,
      ],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        { provide: ExportacaoService, useValue: exportacao },
        { provide: UsersService, useValue: users },
        { provide: DepartmentService, useValue: dept },
        { provide: MatSnackBar, useValue: snackbar },
      ],
    }).compileComponents();
  });

  // Instância direta, para os testes que não precisam de DOM.
  function criar(): ExportacoesComponent {
    return new ExportacoesComponent(exportacao, TestBed.inject(UsersService), TestBed.inject(DepartmentService), snackbar);
  }

  function montarTela() {
    fixture = TestBed.createComponent(ExportacoesComponent);
    component = fixture.componentInstance;
    spyOn<any>(component, 'confirmar').and.returnValue(true);
    fixture.detectChanges();
  }

  describe('filtro montado a partir do formulário', () => {
    it('envia apenas os campos preenchidos, com datas em yyyy-MM-dd e anexos incluídos por padrão', () => {
      const c = criar();
      c.form.tipoTitulo = EnumTipoTituloExportacao.Ambos;
      c.form.codigo = 123;
      c.form.dataSolicitacaoInicio = new Date(2026, 10, 1);   // 01/11/2026 local
      c.form.dataSolicitacaoFim = new Date(2026, 10, 30);
      c.form.dataAprovacaoFinanceiroInicio = new Date(2026, 0, 5);
      c.form.idSolicitante = 7;
      c.form.idDepartamento = 3;
      c.form.statusFinanceiro = 2;
      c.form.nomeParceiro = '  Fulano  ';
      c.form.documentoParceiro = '123.456';
      c.form.tipoPagamento = 1;

      expect(c.montarFiltro()).toEqual({
        tipoTitulo: EnumTipoTituloExportacao.Ambos,
        incluirAnexos: true,
        codigo: 123,
        dataSolicitacaoInicio: '2026-11-01',
        dataSolicitacaoFim: '2026-11-30',
        dataAprovacaoFinanceiroInicio: '2026-01-05',
        idSolicitante: 7,
        idDepartamento: 3,
        statusFinanceiro: 2,
        nomeParceiro: 'Fulano',
        documentoParceiro: '123.456',
        tipoPagamento: 1,
      });
    });

    it('formulário vazio gera filtro só com tipo e incluirAnexos', () => {
      const c = criar();

      expect(c.montarFiltro()).toEqual({ tipoTitulo: EnumTipoTituloExportacao.Ambos, incluirAnexos: true });
    });

    it('com RDDV escolhido descarta campos exclusivos de Solicitação, mesmo preenchidos antes', () => {
      const c = criar();
      c.form.numeroDocumento = 'NF-9';
      c.form.tipoSolicitacao = 2;
      c.form.tipoPagamento = 1;
      c.form.formaPagamento = 2;
      c.form.tipoRelatorio = 3;
      c.form.tipoTitulo = EnumTipoTituloExportacao.Rddv;

      const f = c.montarFiltro();

      expect(f.tipoTitulo).toBe(EnumTipoTituloExportacao.Rddv);
      expect(f.tipoRelatorio).toBe(3);
      expect(f.numeroDocumento).toBeUndefined();
      expect(f.tipoSolicitacao).toBeUndefined();
      expect(f.tipoPagamento).toBeUndefined();
      expect(f.formaPagamento).toBeUndefined();
    });

    it('com Solicitação escolhida descarta o tipo de relatório', () => {
      const c = criar();
      c.form.tipoRelatorio = 2;
      c.form.tipoSolicitacao = 1;
      c.form.tipoTitulo = EnumTipoTituloExportacao.Solicitacao;

      const f = c.montarFiltro();

      expect(f.tipoRelatorio).toBeUndefined();
      expect(f.tipoSolicitacao).toBe(1);
    });

    it('desmarcar "Incluir anexos" vai para o filtro', () => {
      const c = criar();
      c.form.incluirAnexos = false;

      expect(c.montarFiltro().incluirAnexos).toBeFalse();
    });
  });

  describe('datas digitadas nos campos de período', () => {
    async function digitar(input: HTMLInputElement, texto: string) {
      input.value = texto;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    function inputsDoPeriodo(rotulo: string): { inicio: HTMLInputElement; fim: HTMLInputElement } {
      const campo = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('mat-form-field'))
        .find((f) => f.querySelector('mat-label')?.textContent?.includes(rotulo))!;
      return {
        inicio: campo.querySelector('input[matStartDate]') as HTMLInputElement,
        fim: campo.querySelector('input[matEndDate]') as HTMLInputElement,
      };
    }

    function botaoLimpar(): HTMLButtonElement {
      return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
        .find((b) => b.textContent?.includes('Limpar Filtros')) as HTMLButtonElement;
    }

    it('digitar 01/11/2025 no início do período da solicitação vale 1º de novembro, não 11 de janeiro', async () => {
      montarTela();
      await fixture.whenStable();
      const { inicio, fim } = inputsDoPeriodo('Período da Solicitação');

      await digitar(inicio, '01/11/2025');
      await digitar(fim, '31/12/2025');

      const d = component.form.dataSolicitacaoInicio!;
      expect(d.getMonth()).toBe(10);
      expect(d.getDate()).toBe(1);
      const filtro = component.montarFiltro();
      expect(filtro.dataSolicitacaoInicio).toBe('2025-11-01');
      expect(filtro.dataSolicitacaoFim).toBe('2025-12-31');
    });

    it('data impossível (31/02/2025) não entra no filtro', async () => {
      montarTela();
      await fixture.whenStable();
      const { inicio } = inputsDoPeriodo('Período de Vencimento');

      await digitar(inicio, '31/02/2025');

      expect(component.form.dataVencimentoInicio).toBeNull();
      expect(component.montarFiltro().dataVencimentoInicio).toBeUndefined();
    });

    it('Limpar Filtros esvazia os campos de data na tela e o filtro montado não traz datas', async () => {
      montarTela();
      await fixture.whenStable();
      const periodos = ['Período da Solicitação', 'Período de Vencimento', 'Período de Aprovação do Financeiro'].map(inputsDoPeriodo);
      for (const p of periodos) {
        await digitar(p.inicio, '01/11/2025');
        await digitar(p.fim, '31/12/2025');
      }
      expect(periodos[0].inicio.value).toBe('01/11/2025');
      expect(Object.keys(component.montarFiltro()).filter((k) => k.startsWith('data')).length).toBe(6);

      botaoLimpar().click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      for (const p of periodos) {
        expect(p.inicio.value).toBe('');
        expect(p.fim.value).toBe('');
      }
      expect(Object.keys(component.montarFiltro()).filter((k) => k.startsWith('data'))).toEqual([]);
    });

    it('Limpar Filtros também esvazia texto de data ainda inválido (que não chegou ao modelo)', async () => {
      montarTela();
      await fixture.whenStable();
      const { inicio } = inputsDoPeriodo('Período da Solicitação');
      await digitar(inicio, '31/02/2025');
      expect(inicio.value).toBe('31/02/2025');

      botaoLimpar().click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(inicio.value).toBe('');
    });

    it('Limpar Filtros esvazia os demais campos de texto da tela', async () => {
      montarTela();
      await fixture.whenStable();
      const el: HTMLElement = fixture.nativeElement;
      const campos = ['#codigo', '#numeroDocumento', '#nomeParceiro', '#documentoParceiro'].map((s) => el.querySelector(s) as HTMLInputElement);
      for (const c of campos) await digitar(c, '7');
      expect(component.form.nomeParceiro).toBe('7');

      botaoLimpar().click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      for (const c of campos) expect(c.value).toBe('');
    });
  });

  describe('campos desabilitados por tipo de título', () => {
    it('com RDDV escolhido, os campos só de Solicitação ficam desabilitados e os de RDDV não', async () => {
      montarTela();
      component.form.tipoTitulo = EnumTipoTituloExportacao.Rddv;
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const numeroDocumento = el.querySelector('#numeroDocumento') as HTMLInputElement;
      const tipoSolicitacao = el.querySelector('#tipoSolicitacao') as HTMLElement;
      const tipoPagamento = el.querySelector('#tipoPagamento') as HTMLElement;
      const formaPagamento = el.querySelector('#formaPagamento') as HTMLElement;
      const tipoRelatorio = el.querySelector('#tipoRelatorio') as HTMLElement;

      expect(component.camposSolicitacaoDesabilitados).toBeTrue();
      expect(component.camposRddvDesabilitados).toBeFalse();
      expect(numeroDocumento.disabled).toBeTrue();
      expect(tipoSolicitacao.getAttribute('aria-disabled')).toBe('true');
      expect(tipoPagamento.getAttribute('aria-disabled')).toBe('true');
      expect(formaPagamento.getAttribute('aria-disabled')).toBe('true');
      expect(tipoRelatorio.getAttribute('aria-disabled')).not.toBe('true');
    });

    it('com Solicitação escolhida, só o tipo de relatório fica desabilitado', async () => {
      montarTela();
      component.form.tipoTitulo = EnumTipoTituloExportacao.Solicitacao;
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;

      expect(component.camposRddvDesabilitados).toBeTrue();
      expect(component.camposSolicitacaoDesabilitados).toBeFalse();
      expect((el.querySelector('#numeroDocumento') as HTMLInputElement).disabled).toBeFalse();
      expect((el.querySelector('#tipoRelatorio') as HTMLElement).getAttribute('aria-disabled')).toBe('true');
    });

    it('com Ambos nada fica desabilitado', () => {
      const c = criar();

      expect(c.camposSolicitacaoDesabilitados).toBeFalse();
      expect(c.camposRddvDesabilitados).toBeFalse();
    });
  });

  describe('polling da lista', () => {
    it('liga com job Processando e continua consultando a cada 5 s', fakeAsync(() => {
      jobsAtuais = [job(1, EnumStatusExportacao.Processando)];
      montarTela();
      tick();

      expect(exportacao.listar).toHaveBeenCalledTimes(1);
      expect(component.pollingAtivo).toBeTrue();

      tick(5000);
      expect(exportacao.listar).toHaveBeenCalledTimes(2);

      tick(5000);
      expect(exportacao.listar).toHaveBeenCalledTimes(3);

      component.ngOnDestroy();
    }));

    it('desliga quando o job termina', fakeAsync(() => {
      jobsAtuais = [job(1, EnumStatusExportacao.Processando)];
      montarTela();
      tick();
      expect(component.pollingAtivo).toBeTrue();

      jobsAtuais = [job(1, EnumStatusExportacao.Concluido)];
      tick(5000);

      expect(component.pollingAtivo).toBeFalse();
      const chamadas = exportacao.listar.calls.count();
      tick(30000);
      expect(exportacao.listar.calls.count()).toBe(chamadas);
    }));

    it('não liga quando não há job Pendente nem Processando', fakeAsync(() => {
      jobsAtuais = [job(1, EnumStatusExportacao.Concluido), job(2, EnumStatusExportacao.Falhou)];
      montarTela();
      tick();

      expect(component.pollingAtivo).toBeFalse();
      tick(20000);
      expect(exportacao.listar).toHaveBeenCalledTimes(1);
    }));

    it('liga com job Pendente', fakeAsync(() => {
      jobsAtuais = [job(1, EnumStatusExportacao.Pendente)];
      montarTela();
      tick();

      expect(component.pollingAtivo).toBeTrue();
      component.ngOnDestroy();
    }));

    it('para no ngOnDestroy', fakeAsync(() => {
      jobsAtuais = [job(1, EnumStatusExportacao.Processando)];
      montarTela();
      tick();
      expect(component.pollingAtivo).toBeTrue();

      component.ngOnDestroy();
      const chamadas = exportacao.listar.calls.count();
      tick(30000);

      expect(component.pollingAtivo).toBeFalse();
      expect(exportacao.listar.calls.count()).toBe(chamadas);
    }));

    it('resposta de listar() que chega depois do ngOnDestroy com job Pendente não liga o polling', fakeAsync(() => {
      const resposta = new Subject<any>();
      exportacao.listar.and.returnValue(resposta);
      montarTela();

      component.ngOnDestroy();
      resposta.next(envelope([job(1, EnumStatusExportacao.Pendente)]));
      resposta.complete();

      try {
        expect(component.pollingAtivo).toBeFalse();
        exportacao.listar.calls.reset();
        tick(30000);
        expect(exportacao.listar).not.toHaveBeenCalled();
      } finally {
        discardPeriodicTasks();
      }
    }));

    it('criar() que responde depois do ngOnDestroy não recarrega a lista nem liga o polling', fakeAsync(() => {
      const resposta = new Subject<any>();
      exportacao.criar.and.returnValue(resposta);
      jobsAtuais = [job(1, EnumStatusExportacao.Pendente)];
      montarTela();
      tick();
      component.gerarExportacao();
      component.ngOnDestroy();
      exportacao.listar.calls.reset();

      resposta.next(envelope(job(2, EnumStatusExportacao.Pendente)));
      resposta.complete();

      try {
        expect(exportacao.listar).not.toHaveBeenCalled();
        tick(30000);
        expect(exportacao.listar).not.toHaveBeenCalled();
      } finally {
        discardPeriodicTasks();
      }
    }));

    it('erro de rede durante o polling não derruba a consulta seguinte', fakeAsync(() => {
      jobsAtuais = [job(1, EnumStatusExportacao.Processando)];
      montarTela();
      tick();

      exportacao.listar.and.returnValue(throwError(new HttpErrorResponse({ status: 500 })));
      tick(5000);
      expect(component.pollingAtivo).toBeTrue();

      exportacao.listar.and.callFake(() => of(envelope([job(1, EnumStatusExportacao.Concluido)])));
      tick(5000);
      expect(component.pollingAtivo).toBeFalse();
    }));
  });

  describe('ações', () => {
    describe('download sem sair da aplicação', () => {
      let frames: HTMLIFrameElement[];
      let janelaAberta: jasmine.Spy;

      beforeEach(() => {
        jobsAtuais = [job(5, EnumStatusExportacao.Concluido)];
        exportacao.gerarLinkDownload.and.returnValue(of(envelope({ token: 'tok-abc', expiraEm: '2026-11-25T15:00:00Z' })));
        montarTela();

        // Captura o iframe sem anexá-lo, para o teste não fazer requisição de rede.
        frames = [];
        const anexar = document.body.appendChild.bind(document.body);
        spyOn(document.body, 'appendChild').and.callFake(((el: Node) => {
          if (el instanceof HTMLIFrameElement) {
            frames.push(el);
            return el;
          }
          return anexar(el);
        }) as any);
        janelaAberta = spyOn(window, 'open');
      });

      it('Baixar cria um iframe oculto com a URL do token, sem navegar nem abrir janela', () => {
        component.baixar(jobsAtuais[0]);

        expect(exportacao.gerarLinkDownload).toHaveBeenCalledOnceWith(5);
        expect(janelaAberta).not.toHaveBeenCalled();
        expect(frames.length).toBe(1);
        expect(frames[0].src).toBe('http://api/exportacao/download?token=tok-abc');
        expect(frames[0].style.display).toBe('none');
      });

      it('se o servidor devolve um documento (token recusado), o usuário é orientado a gerar o link de novo', () => {
        component.baixar(jobsAtuais[0]);

        frames[0].dispatchEvent(new Event('load'));

        expect(snackbar.open).toHaveBeenCalledTimes(1);
        const texto = snackbar.open.calls.mostRecent().args[0];
        expect(texto).toContain('Gere o link de novo');
        expect(frames[0].isConnected).toBeFalse();
      });

      it('download que o navegador aceita como arquivo (sem load) não mostra erro', () => {
        component.baixar(jobsAtuais[0]);

        expect(snackbar.open).not.toHaveBeenCalled();
      });

      it('um novo Baixar descarta o iframe anterior', () => {
        component.baixar(jobsAtuais[0]);
        component.baixar(jobsAtuais[0]);

        expect(frames.length).toBe(2);
        expect(frames[0]).not.toBe(frames[1]);
      });
    });

    it('Baixar com falha ao gerar o link mostra a mensagem e não cria iframe', () => {
      jobsAtuais = [job(5, EnumStatusExportacao.Concluido)];
      exportacao.gerarLinkDownload.and.returnValue(throwError(new HttpErrorResponse({
        status: 410, error: { success: false, message: 'O arquivo expirou. Gere a exportação de novo.' },
      })));
      montarTela();

      component.baixar(jobsAtuais[0]);

      expect(document.querySelector('iframe')).toBeNull();
      expect(snackbar.open).toHaveBeenCalledWith('O arquivo expirou. Gere a exportação de novo.', 'OK', jasmine.anything());
    });

    it('Gerar exportação envia o filtro do formulário e recarrega a lista', () => {
      exportacao.criar.and.returnValue(of(envelope(job(9, EnumStatusExportacao.Pendente))));
      montarTela();
      component.form.tipoTitulo = EnumTipoTituloExportacao.Solicitacao;
      component.form.codigo = 42;
      exportacao.listar.calls.reset();

      component.gerarExportacao();

      expect(exportacao.criar).toHaveBeenCalledOnceWith({
        tipoTitulo: EnumTipoTituloExportacao.Solicitacao,
        incluirAnexos: true,
        codigo: 42,
      });
      expect(exportacao.listar).toHaveBeenCalledTimes(1);
      component.ngOnDestroy();
    });

    it('erro 400 ou 429 mostra a mensagem do servidor no snackbar', () => {
      exportacao.criar.and.returnValue(throwError(new HttpErrorResponse({
        status: 429, error: { success: false, message: 'Você já tem 2 exportações em andamento.' },
      })));
      montarTela();

      component.gerarExportacao();

      expect(snackbar.open).toHaveBeenCalledWith('Você já tem 2 exportações em andamento.', 'OK', jasmine.anything());
      expect(component.isBusy).toBeFalse();
    });

    it('erro sem mensagem do servidor mostra texto padrão, nunca o texto da exceção', () => {
      exportacao.criar.and.returnValue(throwError(new HttpErrorResponse({ status: 500, statusText: 'Boom' })));
      montarTela();

      component.gerarExportacao();

      const texto = snackbar.open.calls.mostRecent().args[0];
      expect(texto).toBe('Não foi possível solicitar a exportação. Tente novamente.');
    });

    it('Cancelar pede confirmação, chama o serviço e recarrega', () => {
      jobsAtuais = [job(3, EnumStatusExportacao.Pendente)];
      exportacao.cancelar.and.returnValue(of(undefined));
      montarTela();
      exportacao.listar.calls.reset();

      component.cancelar(jobsAtuais[0]);

      expect(exportacao.cancelar).toHaveBeenCalledOnceWith(3);
      expect(exportacao.listar).toHaveBeenCalledTimes(1);
      component.ngOnDestroy();
    });

    it('Cancelar sem confirmar não chama o serviço', () => {
      jobsAtuais = [job(3, EnumStatusExportacao.Pendente)];
      montarTela();
      (component as any).confirmar.and.returnValue(false);

      component.cancelar(jobsAtuais[0]);

      expect(exportacao.cancelar).not.toHaveBeenCalled();
      component.ngOnDestroy();
    });

    it('Baixar só aparece para Concluída, Cancelar para Pendente/Processando e Excluir para Concluída', () => {
      montarTela();
      const c = (s: EnumStatusExportacao) => job(1, s);

      expect(component.podeBaixar(c(EnumStatusExportacao.Concluido))).toBeTrue();
      expect(component.podeExcluir(c(EnumStatusExportacao.Concluido))).toBeTrue();
      expect(component.podeCancelar(c(EnumStatusExportacao.Concluido))).toBeFalse();

      for (const s of [EnumStatusExportacao.Pendente, EnumStatusExportacao.Processando]) {
        expect(component.podeCancelar(c(s))).toBeTrue();
        expect(component.podeBaixar(c(s))).toBeFalse();
        expect(component.podeExcluir(c(s))).toBeFalse();
      }

      for (const s of [EnumStatusExportacao.Falhou, EnumStatusExportacao.Expirado, EnumStatusExportacao.Cancelado]) {
        expect(component.podeBaixar(c(s))).toBeFalse();
        expect(component.podeCancelar(c(s))).toBeFalse();
        expect(component.podeExcluir(c(s))).toBeFalse();
      }
    });
  });

  describe('apresentação', () => {
    it('tamanho legível', () => {
      const c = criar();

      expect(c.tamanhoLegivel(null)).toBe('');
      expect(c.tamanhoLegivel(512)).toBe('512 B');
      expect(c.tamanhoLegivel(1536)).toBe('1,5 KB');
      expect(c.tamanhoLegivel(5 * 1024 * 1024)).toBe('5,0 MB');
      expect(c.tamanhoLegivel(2.5 * 1024 * 1024 * 1024)).toBe('2,5 GB');
    });

    it('datas sem fuso vindas do servidor são tratadas como UTC', () => {
      const c = criar();

      expect(c.paraData('2026-11-25T14:00:00')!.toISOString()).toBe('2026-11-25T14:00:00.000Z');
      expect(c.paraData('2026-11-25T14:00:00Z')!.toISOString()).toBe('2026-11-25T14:00:00.000Z');
      expect(c.paraData(null)).toBeNull();
    });
  });
});
