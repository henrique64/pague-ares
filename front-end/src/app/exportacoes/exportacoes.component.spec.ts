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
    exportacao = jasmine.createSpyObj('ExportacaoService', ['criar', 'listar', 'obter', 'gerarLinkDownload', 'urlDownload', 'cancelar', 'removerDaLista']);
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
        ...PROVIDERS_DATA_PT_BR,
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
      c.form.codigo = '123';
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
        .find((b) => b.textContent?.includes('Limpar filtros')) as HTMLButtonElement;
    }

    it('digitar 01/11/2025 no início do período da solicitação vale 1º de novembro, não 11 de janeiro', async () => {
      montarTela();
      await fixture.whenStable();
      const { inicio, fim } = inputsDoPeriodo('Período da solicitação');

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
      const { inicio } = inputsDoPeriodo('Período de vencimento');

      await digitar(inicio, '31/02/2025');

      expect(component.form.dataVencimentoInicio).toBeNull();
      expect(component.montarFiltro().dataVencimentoInicio).toBeUndefined();
    });

    it('Limpar filtros esvazia os campos de data na tela e o filtro montado não traz datas', async () => {
      montarTela();
      await fixture.whenStable();
      const periodos = ['Período da solicitação', 'Período de vencimento', 'Período de aprovação do financeiro'].map(inputsDoPeriodo);
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

    it('Limpar filtros também esvazia texto de data ainda inválido (que não chegou ao modelo)', async () => {
      montarTela();
      await fixture.whenStable();
      const { inicio } = inputsDoPeriodo('Período da solicitação');
      await digitar(inicio, '31/02/2025');
      expect(inicio.value).toBe('31/02/2025');

      botaoLimpar().click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(inicio.value).toBe('');
    });

    it('Limpar filtros esvazia os demais campos de texto da tela', async () => {
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

    describe('com Ambos e código com prefixo, vale o tipo do prefixo', () => {
      async function renderizar(): Promise<HTMLElement> {
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
        return fixture.nativeElement;
      }

      it('RDDV-45 desabilita na tela os campos só de Solicitação, mantém o valor digitado e deixa o de RDDV habilitado', async () => {
        montarTela();
        component.form.tipoTitulo = EnumTipoTituloExportacao.Ambos;
        component.form.codigo = 'RDDV-45';
        component.form.numeroDocumento = 'NF-99';
        component.form.tipoSolicitacao = 1;
        component.form.tipoRelatorio = 2;
        const el = await renderizar();

        expect(component.camposSolicitacaoDesabilitados).toBeTrue();
        expect(component.camposRddvDesabilitados).toBeFalse();
        const numeroDocumento = el.querySelector('#numeroDocumento') as HTMLInputElement;
        expect(numeroDocumento.disabled).toBeTrue();
        expect(numeroDocumento.value).toBe('NF-99');
        expect((el.querySelector('#tipoSolicitacao') as HTMLElement).getAttribute('aria-disabled')).toBe('true');
        expect((el.querySelector('#tipoPagamento') as HTMLElement).getAttribute('aria-disabled')).toBe('true');
        expect((el.querySelector('#formaPagamento') as HTMLElement).getAttribute('aria-disabled')).toBe('true');
        expect((el.querySelector('#tipoRelatorio') as HTMLElement).getAttribute('aria-disabled')).not.toBe('true');
        expect(component.form.numeroDocumento).toBe('NF-99');
        expect(component.form.tipoSolicitacao).toBe(1);
      });

      it('SOL-12 desabilita na tela o tipo de relatório e deixa os campos de Solicitação habilitados', async () => {
        montarTela();
        component.form.tipoTitulo = EnumTipoTituloExportacao.Ambos;
        component.form.codigo = 'SOL-12';
        component.form.tipoRelatorio = 3;
        const el = await renderizar();

        expect(component.camposRddvDesabilitados).toBeTrue();
        expect(component.camposSolicitacaoDesabilitados).toBeFalse();
        expect((el.querySelector('#tipoRelatorio') as HTMLElement).getAttribute('aria-disabled')).toBe('true');
        expect((el.querySelector('#numeroDocumento') as HTMLInputElement).disabled).toBeFalse();
        expect((el.querySelector('#tipoSolicitacao') as HTMLElement).getAttribute('aria-disabled')).not.toBe('true');
        expect(component.form.tipoRelatorio).toBe(3);
      });

      it('o aviso do prefixo continua aparecendo junto com os campos desabilitados', async () => {
        montarTela();
        component.form.codigo = 'RDDV-45';
        const el = await renderizar();

        expect(el.querySelector('.aviso-ambos')?.textContent)
          .toContain('O código RDDV-000045 é de um RDDV: as solicitações ficam de fora.');
      });

      it('os campos voltam a ficar habilitados quando o código vira número puro, e o valor digitado segue lá', async () => {
        montarTela();
        component.form.codigo = 'RDDV-45';
        component.form.numeroDocumento = 'NF-99';
        await renderizar();
        expect(component.camposSolicitacaoDesabilitados).toBeTrue();

        component.form.codigo = '45';
        const el = await renderizar();

        expect(component.camposSolicitacaoDesabilitados).toBeFalse();
        expect(component.camposRddvDesabilitados).toBeFalse();
        const numeroDocumento = el.querySelector('#numeroDocumento') as HTMLInputElement;
        expect(numeroDocumento.disabled).toBeFalse();
        expect(numeroDocumento.value).toBe('NF-99');
      });

      it('com tipo escolhido diferente do prefixo (código recusado) vale o tipo escolhido', () => {
        const c = criar();
        c.form.tipoTitulo = EnumTipoTituloExportacao.Solicitacao;
        c.form.codigo = 'RDDV-45';

        expect(c.camposRddvDesabilitados).toBeTrue();
        expect(c.camposSolicitacaoDesabilitados).toBeFalse();
      });
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

      // O iframe da tela fica fora do DOM nestes testes; o documento que o servidor devolveria é simulado.
      function documentoDoIframe(frame: HTMLIFrameElement, leitura: () => unknown) {
        Object.defineProperty(frame, 'contentDocument', { get: leitura, configurable: true });
      }

      it('iframe ilegível (origem diferente): mostra o aviso fixo, com "g" minúsculo depois dos dois-pontos', () => {
        component.baixar(jobsAtuais[0]);
        documentoDoIframe(frames[0], () => { throw new DOMException('Blocked a frame', 'SecurityError'); });

        frames[0].dispatchEvent(new Event('load'));

        expect(snackbar.open).toHaveBeenCalledTimes(1);
        const texto = snackbar.open.calls.mostRecent().args[0];
        expect(texto).toBe(
          'Não foi possível baixar o arquivo. O link expira rápido e só vale uma vez: gere o link de novo clicando em Baixar.');
        expect(frames[0].isConnected).toBeFalse();
      });

      it('iframe sem acesso ao documento (null) mostra o aviso fixo', () => {
        component.baixar(jobsAtuais[0]);

        frames[0].dispatchEvent(new Event('load'));

        expect(snackbar.open.calls.mostRecent().args[0]).toContain(': gere o link de novo');
      });

      it('iframe com o envelope JSON de erro da API: mostra a mensagem do servidor', () => {
        component.baixar(jobsAtuais[0]);
        const corpo = JSON.stringify({ success: false, message: 'O arquivo desta exportação foi excluído. Gere a exportação de novo.', data: null });
        documentoDoIframe(frames[0], () => ({ body: { textContent: corpo } }));

        frames[0].dispatchEvent(new Event('load'));

        expect(snackbar.open).toHaveBeenCalledTimes(1);
        expect(snackbar.open.calls.mostRecent().args[0]).toBe(
          'O arquivo desta exportação foi excluído. Gere a exportação de novo.');
        expect(frames[0].isConnected).toBeFalse();
      });

      it('iframe com texto que não é o envelope (HTML, JSON sem message): mostra o aviso fixo', () => {
        for (const corpo of ['<h1>Bad Gateway</h1>', '{"title":"x"}', '{"message":"   "}', '', '[1]']) {
          snackbar.open.calls.reset();
          component.baixar(jobsAtuais[0]);
          const frame = frames[frames.length - 1];
          documentoDoIframe(frame, () => ({ body: { textContent: corpo } }));

          frame.dispatchEvent(new Event('load'));

          expect(snackbar.open.calls.mostRecent().args[0]).withContext('corpo: ' + corpo).toContain(': gere o link de novo');
        }
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
      component.form.codigo = '42';
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

      expect(component.podeBaixar(c(EnumStatusExportacao.Excluido))).toBeFalse();
      expect(component.podeExcluir(c(EnumStatusExportacao.Excluido))).toBeFalse();
      expect(component.podeCancelar(c(EnumStatusExportacao.Excluido))).toBeFalse();

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

  describe('Remover da lista e situação Excluída', () => {
    const encerradas = [
      EnumStatusExportacao.Falhou, EnumStatusExportacao.Cancelado,
      EnumStatusExportacao.Expirado, EnumStatusExportacao.Excluido,
    ];
    const outras = [EnumStatusExportacao.Pendente, EnumStatusExportacao.Processando, EnumStatusExportacao.Concluido];

    it('podeRemoverDaLista é verdadeiro só para Falhou, Cancelada, Expirada e Excluída', () => {
      const c = criar();

      for (const s of encerradas) expect(c.podeRemoverDaLista(job(1, s))).withContext(String(s)).toBeTrue();
      for (const s of outras) expect(c.podeRemoverDaLista(job(1, s))).withContext(String(s)).toBeFalse();
    });

    it('Remover da lista pede confirmação, chama o serviço, avisa e recarrega a lista', () => {
      jobsAtuais = [job(7, EnumStatusExportacao.Excluido)];
      exportacao.removerDaLista.and.returnValue(of(undefined));
      montarTela();
      exportacao.listar.calls.reset();

      component.removerDaLista(jobsAtuais[0]);

      expect((component as any).confirmar).toHaveBeenCalledOnceWith('Remover esta exportação da lista?');
      expect(exportacao.removerDaLista).toHaveBeenCalledOnceWith(7);
      expect(snackbar.open).toHaveBeenCalledWith('Exportação removida da lista.', 'OK', jasmine.anything());
      expect(exportacao.listar).toHaveBeenCalledTimes(1);
      component.ngOnDestroy();
    });

    it('Remover da lista sem confirmar não chama o serviço', () => {
      jobsAtuais = [job(7, EnumStatusExportacao.Falhou)];
      montarTela();
      (component as any).confirmar.and.returnValue(false);

      component.removerDaLista(jobsAtuais[0]);

      expect(exportacao.removerDaLista).not.toHaveBeenCalled();
      component.ngOnDestroy();
    });

    it('Remover da lista com erro mostra a mensagem do servidor e recarrega a lista', () => {
      jobsAtuais = [job(7, EnumStatusExportacao.Falhou)];
      exportacao.removerDaLista.and.returnValue(throwError(new HttpErrorResponse({
        status: 409, error: { success: false, message: 'Só exportações encerradas podem ser removidas da lista.' },
      })));
      montarTela();
      exportacao.listar.calls.reset();

      component.removerDaLista(jobsAtuais[0]);

      expect(snackbar.open).toHaveBeenCalledWith('Só exportações encerradas podem ser removidas da lista.', 'OK', jasmine.anything());
      expect(exportacao.listar).toHaveBeenCalledTimes(1);
      expect(component.isBusy).toBeFalse();
      component.ngOnDestroy();
    });

    describe('na tela', () => {
      function linhas(): HTMLElement[] {
        return Array.from(fixture.nativeElement.querySelectorAll('tbody tr'));
      }

      it('o botão aparece nas quatro situações encerradas e em nenhuma outra', () => {
        jobsAtuais = [...encerradas, ...outras].map((s, i) => job(i + 1, s));
        montarTela();

        const linhasTela = linhas();
        expect(linhasTela.length).toBe(7);
        linhasTela.forEach((tr, i) => {
          const tem = tr.querySelector('button.acao-remover') !== null;
          expect(tem).withContext('situação ' + jobsAtuais[i].status).toBe(i < encerradas.length);
        });
        const botao = linhasTela[0].querySelector('button.acao-remover') as HTMLElement;
        expect(botao.textContent!.trim()).toBe('Remover da lista');
        component.ngOnDestroy();
      });

      it('clicar no botão remove o job da lista', () => {
        jobsAtuais = [job(7, EnumStatusExportacao.Expirado)];
        exportacao.removerDaLista.and.returnValue(of(undefined));
        montarTela();

        (linhas()[0].querySelector('button.acao-remover') as HTMLElement).click();

        expect(exportacao.removerDaLista).toHaveBeenCalledOnceWith(7);
        component.ngOnDestroy();
      });

      it('Excluída não tem Baixar nem Excluir, e a situação aparece como inativa', () => {
        jobsAtuais = [job(7, EnumStatusExportacao.Excluido, { statusDescricao: 'Excluída' })];
        montarTela();

        const tr = linhas()[0];
        expect(tr.querySelector('button.acao-baixar')).toBeNull();
        expect(tr.querySelector('button.acao-excluir')).toBeNull();
        expect(tr.querySelector('button.acao-cancelar')).toBeNull();
        const situacao = tr.querySelector('span.status') as HTMLElement;
        expect(situacao.textContent!.trim()).toBe('Excluída');
        expect(situacao.classList).toContain('status-inativa');
        component.ngOnDestroy();
      });

      it('Tamanho: Excluída mostra "—" (arquivo apagado); as demais situações mostram o tamanho', () => {
        jobsAtuais = [
          job(7, EnumStatusExportacao.Excluido, { tamanhoBytes: 1536 }),
          job(8, EnumStatusExportacao.Concluido, { tamanhoBytes: 1536 }),
          job(9, EnumStatusExportacao.Expirado, { tamanhoBytes: 512 }),
        ];
        montarTela();

        const cabecalhos = Array.from(fixture.nativeElement.querySelectorAll('thead th')).map((th: any) => th.textContent.trim());
        const col = cabecalhos.indexOf('Tamanho');
        expect(col).toBeGreaterThanOrEqual(0);
        const tamanhoDa = (tr: Element) => (tr.querySelectorAll('td')[col].textContent ?? '').trim();
        const trs = linhas();
        const porId = (id: number) => trs.find(tr => (tr.querySelectorAll('td')[1].textContent ?? '').trim() === String(id))!;

        expect(tamanhoDa(porId(7))).toBe('—');
        expect(tamanhoDa(porId(8))).toBe('1,5 KB');
        expect(tamanhoDa(porId(9))).toBe('512 B');
        component.ngOnDestroy();
      });

      it('a coluna "Nº" vem logo depois de "Ações" e mostra o número da exportação', () => {
        jobsAtuais = [job(1234, EnumStatusExportacao.Concluido)];
        montarTela();

        const cabecalhos = Array.from(fixture.nativeElement.querySelectorAll('thead th')).map((th: any) => th.textContent.trim());
        expect(cabecalhos.slice(0, 3)).toEqual(['Ações', 'Nº', 'Solicitada em']);
        const celulas = linhas()[0].querySelectorAll('td');
        expect(celulas[1].textContent!.trim()).toBe('1234');
        component.ngOnDestroy();
      });

      it('lista vazia diz que não há exportações para mostrar nos últimos 30 dias', () => {
        jobsAtuais = [];
        montarTela();

        const vazio = fixture.nativeElement.querySelector('p.vazio') as HTMLElement;
        expect(vazio.textContent!.trim()).toBe('Nenhuma exportação para mostrar nos últimos 30 dias.');
      });
    });
  });

  describe('filtro Código (123, SOL-000123, RDDV-45)', () => {
    const Sol = EnumTipoTituloExportacao.Solicitacao;
    const Rddv = EnumTipoTituloExportacao.Rddv;
    const Ambos = EnumTipoTituloExportacao.Ambos;
    const MSG_INVALIDO =
      'Informe o código como número inteiro maior que zero (ex.: 123) ou com o prefixo (SOL-000123, RDDV-000045).';

    function comCodigo(texto: string, tipo = Ambos) {
      const c = criar();
      c.form.tipoTitulo = tipo;
      c.form.codigo = texto;
      return c;
    }

    const aceitos: [string, EnumTipoTituloExportacao, number, EnumTipoTituloExportacao][] = [
      ['123', Ambos, 123, Ambos],
      ['000123', Ambos, 123, Ambos],
      ['123', Sol, 123, Sol],
      ['sol-000123', Ambos, 123, Sol],
      ['SOL123', Ambos, 123, Sol],
      ['  Sol-000123  ', Sol, 123, Sol],
      ['RDDV-45', Ambos, 45, Rddv],
      ['rddv45', Rddv, 45, Rddv],
    ];

    for (const [texto, tipoEscolhido, numero, tipoEnviado] of aceitos) {
      it(`aceita "${texto}" com ${EnumTipoTituloExportacao[tipoEscolhido]}: envia código ${numero} e tipo ${EnumTipoTituloExportacao[tipoEnviado]}`, () => {
        const c = comCodigo(texto, tipoEscolhido);

        expect(c.erroCodigo).toBeNull();
        expect(c.montarFiltro().codigo).toBe(numero);
        expect(c.montarFiltro().tipoTitulo).toBe(tipoEnviado);
      });
    }

    for (const texto of ['0', '1,5', '1.5', '-3', 'ABC-1', 'SOL-']) {
      it(`recusa "${texto}" com a mensagem de validação e não gera a exportação`, () => {
        const c = comCodigo(texto);

        expect(c.erroCodigo).toBe(MSG_INVALIDO);

        c.gerarExportacao();

        expect(exportacao.criar).not.toHaveBeenCalled();
        expect(snackbar.open).toHaveBeenCalledWith(MSG_INVALIDO, 'OK', jasmine.anything());
      });
    }

    it('código vazio ou só com espaços não filtra nem recusa', () => {
      for (const texto of ['', '   ']) {
        const c = comCodigo(texto);

        expect(c.erroCodigo).toBeNull();
        expect(c.montarFiltro().codigo).toBeUndefined();
      }
    });

    it('prefixo em conflito com o tipo escolhido é recusado e não gera a exportação', () => {
      const c = comCodigo('SOL-000123', Rddv);

      expect(c.erroCodigo).toBe('O código SOL-000123 é de solicitação, mas o tipo de título escolhido é RDDV.');

      const d = comCodigo('rddv-45', Sol);

      expect(d.erroCodigo).toBe('O código RDDV-000045 é de RDDV, mas o tipo de título escolhido é Solicitação.');

      c.gerarExportacao();

      expect(exportacao.criar).not.toHaveBeenCalled();
      expect(snackbar.open).toHaveBeenCalledWith(
        'O código SOL-000123 é de solicitação, mas o tipo de título escolhido é RDDV.', 'OK', jasmine.anything());
    });

    it('com "Ambos" e prefixo, o filtro enviado leva o tipo do prefixo', () => {
      exportacao.criar.and.returnValue(of(envelope(job(9, EnumStatusExportacao.Pendente))));
      const c = comCodigo('SOL-000123');

      c.gerarExportacao();

      expect(exportacao.criar).toHaveBeenCalledOnceWith({ tipoTitulo: Sol, incluirAnexos: true, codigo: 123 });
      c.ngOnDestroy();
    });

    it('com o tipo do prefixo, os campos só do outro tipo não vão no filtro (como se o tipo tivesse sido escolhido)', () => {
      const c = comCodigo('SOL-000123');
      c.form.tipoRelatorio = 2;
      c.form.numeroDocumento = 'NF-1';

      const f = c.montarFiltro();

      expect(f.tipoRelatorio).toBeUndefined();
      expect(f.numeroDocumento).toBe('NF-1');
    });

    it('na tela: campo de texto, com dica; o erro aparece depois de tentar gerar', async () => {
      montarTela();
      await fixture.whenStable();
      const el: HTMLElement = fixture.nativeElement;
      const campo = el.querySelector('#codigo') as HTMLInputElement;

      expect(campo.type).toBe('text');
      const formField = campo.closest('mat-form-field')!;
      expect(formField.querySelector('mat-hint')?.textContent?.trim())
        .toBe('Ex.: 123, SOL-000123 ou RDDV-000045');

      campo.value = '1,5';
      campo.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      component.gerarExportacao();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(exportacao.criar).not.toHaveBeenCalled();
      expect(formField.querySelector('mat-error')?.textContent?.trim()).toBe(MSG_INVALIDO);
    });

    it('Limpar filtros tira o erro do código', async () => {
      montarTela();
      component.form.codigo = '0';
      component.gerarExportacao();
      fixture.detectChanges();

      component.limparFormulario();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(component.erroCodigo).toBeNull();
      expect((fixture.nativeElement as HTMLElement).querySelector('#codigo')!.closest('mat-form-field')!.querySelector('mat-error')).toBeNull();
    });
  });

  describe('avisos do tipo "Ambos"', () => {
    const AV_SOLICITACAO =
      'Nº do documento, Tipo de solicitação, Tipo de pagamento e Forma de pagamento só existem em solicitações: com eles preenchidos, os RDDVs ficam de fora.';
    const AV_RELATORIO = 'Tipo de relatório só existe em RDDVs: com ele preenchido, as solicitações ficam de fora.';

    function avisos(): string[] {
      const area = (fixture.nativeElement as HTMLElement).querySelector('[role="status"]');
      return area ? Array.from(area.querySelectorAll('p')).map((p) => p.textContent!.trim()) : [];
    }

    function atualizar() {
      fixture.detectChanges();
    }

    it('formulário vazio não mostra aviso', () => {
      montarTela();

      expect(avisos()).toEqual([]);
      expect((fixture.nativeElement as HTMLElement).querySelector('[role="status"]')).toBeNull();
    });

    it('cada campo só de Solicitação preenchido mostra o aviso e ele some quando o campo é esvaziado', () => {
      montarTela();
      const campos: [string, () => void, () => void][] = [
        ['numeroDocumento', () => (component.form.numeroDocumento = 'NF-1'), () => (component.form.numeroDocumento = '  ')],
        ['tipoSolicitacao', () => (component.form.tipoSolicitacao = 1), () => (component.form.tipoSolicitacao = 0)],
        ['tipoPagamento', () => (component.form.tipoPagamento = 2), () => (component.form.tipoPagamento = 0)],
        ['formaPagamento', () => (component.form.formaPagamento = 1), () => (component.form.formaPagamento = 0)],
      ];

      for (const [nome, preencher, esvaziar] of campos) {
        preencher();
        atualizar();
        expect(avisos()).withContext(nome + ' preenchido').toEqual([AV_SOLICITACAO]);

        esvaziar();
        atualizar();
        expect(avisos()).withContext(nome + ' esvaziado').toEqual([]);
      }
    });

    it('Tipo de relatório preenchido mostra o aviso das solicitações e ele some com o campo', () => {
      montarTela();

      component.form.tipoRelatorio = 3;
      atualizar();
      expect(avisos()).toEqual([AV_RELATORIO]);

      component.form.tipoRelatorio = 0;
      atualizar();
      expect(avisos()).toEqual([]);
    });

    it('os dois casos juntos mostram os dois avisos', () => {
      montarTela();
      component.form.numeroDocumento = 'NF-1';
      component.form.tipoRelatorio = 1;
      atualizar();

      expect(avisos()).toEqual([AV_SOLICITACAO, AV_RELATORIO]);
    });

    it('código com prefixo mostra o aviso do prefixo e some quando o código vira número puro', () => {
      montarTela();

      component.form.codigo = 'SOL-000123';
      atualizar();
      expect(avisos()).toEqual(['O código SOL-000123 é de uma solicitação: os RDDVs ficam de fora.']);

      component.form.codigo = 'rddv45';
      atualizar();
      expect(avisos()).toEqual(['O código RDDV-000045 é de um RDDV: as solicitações ficam de fora.']);

      component.form.codigo = '45';
      atualizar();
      expect(avisos()).toEqual([]);
    });

    it('com Solicitação ou RDDV escolhido não há aviso (os campos do outro tipo já ficam desabilitados)', () => {
      montarTela();
      component.form.numeroDocumento = 'NF-1';
      component.form.tipoRelatorio = 1;
      component.form.codigo = '123';

      for (const tipo of [EnumTipoTituloExportacao.Solicitacao, EnumTipoTituloExportacao.Rddv]) {
        component.form.tipoTitulo = tipo;
        atualizar();
        expect(avisos()).withContext(EnumTipoTituloExportacao[tipo]).toEqual([]);
      }
    });

    it('o aviso não bloqueia a exportação', () => {
      exportacao.criar.and.returnValue(of(envelope(job(9, EnumStatusExportacao.Pendente))));
      montarTela();
      component.form.numeroDocumento = 'NF-1';
      component.form.tipoRelatorio = 1;
      atualizar();

      component.gerarExportacao();

      expect(exportacao.criar).toHaveBeenCalledTimes(1);
      component.ngOnDestroy();
    });
  });

  describe('rótulos e layout do formulário', () => {
    function rotulos(): string[] {
      return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('mat-card mat-label'))
        .map((l) => l.textContent!.trim());
    }

    it('rótulos em caixa de frase, pela convenção', () => {
      montarTela();

      expect(rotulos()).toEqual([
        'Tipo de título',
        'Código',
        'Departamento',
        'Nome do parceiro (RDDV: funcionário)',
        'CPF/CNPJ do parceiro (RDDV: CPF do funcionário)',
        'Solicitante',
        'Atribuído a',
        'Nº do documento (só Solicitação)',
        'Tipo de solicitação (só Solicitação)',
        'Tipo de pagamento (só Solicitação)',
        'Forma de pagamento (só Solicitação)',
        'Período da solicitação (RDDV: cadastro)',
        'Período de vencimento',
        'Período de aprovação do financeiro',
        'Tipo de relatório (só RDDV)',
        'Status gestor',
        'Status financeiro',
        'Status contábil',
      ]);
    });

    it('botões "Limpar filtros" e "Gerar exportação"', () => {
      montarTela();
      const botoes = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('mat-card .pull-right button')).map((b) => b.textContent!.trim());

      expect(botoes).toEqual(['Limpar filtros', 'Gerar exportação']);
    });

    it('nenhum rótulo longo fica numa coluna estreita (menos de 4 de 12), onde o texto seria cortado a 1360 px', () => {
      montarTela();
      const estreitos: string[] = [];

      (fixture.nativeElement as HTMLElement).querySelectorAll('mat-card mat-form-field').forEach((campo) => {
        const rotulo = campo.querySelector('mat-label')!.textContent!.trim();
        const coluna = campo.closest('[class*="col-"]')!;
        const largura = Number(/col-md-(\d+)/.exec(coluna.className)?.[1] ?? 12);
        if (rotulo.length > 20 && largura < 4) estreitos.push(`${rotulo} (col-md-${largura})`);
      });

      expect(estreitos).toEqual([]);
    });

    it('os campos de período com rótulo longo têm pelo menos 6 de 12 colunas (rótulo mais o ícone do calendário)', () => {
      montarTela();
      const el: HTMLElement = fixture.nativeElement;
      const larguraDe = (texto: string) => {
        const campo = Array.from(el.querySelectorAll('mat-card mat-form-field'))
          .find((f) => f.querySelector('mat-label')!.textContent!.trim() === texto)!;
        return Number(/col-md-(\d+)/.exec(campo.closest('[class*="col-"]')!.className)![1]);
      };

      expect(larguraDe('Período da solicitação (RDDV: cadastro)')).toBeGreaterThanOrEqual(6);
      expect(larguraDe('Período de aprovação do financeiro')).toBeGreaterThanOrEqual(6);
    });
  });

  describe('coluna Filtros da lista (resumoFiltros)', () => {
    function resumo(filtros: any): string {
      const c = criar();
      c.userList = [{ idUsuario: 7, nome: 'Ana Teste' } as any, { idUsuario: 8, nome: 'Bruno Teste' } as any];
      c.departamentos = [{ idDepartamento: 3, nome: 'Compras' } as any];
      return c.resumoFiltros(job(1, EnumStatusExportacao.Concluido, { filtros }));
    }

    it('só o essencial: tipo de título e anexos, com dois-pontos', () => {
      expect(resumo({ tipoTitulo: EnumTipoTituloExportacao.Ambos, incluirAnexos: true }))
        .toBe('Tipo de título: Ambos (Solicitações e RDDVs) · Incluir anexos: Sim');
      expect(resumo({ tipoTitulo: EnumTipoTituloExportacao.Solicitacao, incluirAnexos: false }))
        .toBe('Tipo de título: Solicitação · Incluir anexos: Não');
      expect(resumo({ tipoTitulo: EnumTipoTituloExportacao.Rddv, incluirAnexos: true }))
        .toBe('Tipo de título: RDDV · Incluir anexos: Sim');
    });

    it('código com prefixo e seis dígitos quando o tipo é Solicitação ou RDDV; número puro com "Ambos"', () => {
      expect(resumo({ tipoTitulo: EnumTipoTituloExportacao.Solicitacao, codigo: 123, incluirAnexos: true }))
        .toContain('Código: SOL-000123');
      expect(resumo({ tipoTitulo: EnumTipoTituloExportacao.Rddv, codigo: 45, incluirAnexos: true }))
        .toContain('Código: RDDV-000045');
      expect(resumo({ tipoTitulo: EnumTipoTituloExportacao.Ambos, codigo: 123, incluirAnexos: true }))
        .toContain('Código: 123');
      expect(resumo({ tipoTitulo: EnumTipoTituloExportacao.Ambos, codigo: 123, incluirAnexos: true }))
        .not.toContain('SOL-');
    });

    it('todos os campos, com o rótulo do formulário e dois-pontos', () => {
      const texto = resumo({
        tipoTitulo: EnumTipoTituloExportacao.Solicitacao,
        codigo: 123,
        numeroDocumento: 'NF-9',
        dataSolicitacaoInicio: '2025-11-01', dataSolicitacaoFim: '2025-11-30',
        dataVencimentoInicio: '2025-12-01',
        dataAprovacaoFinanceiroFim: '2025-12-31',
        idSolicitante: 7, idAtribuido: 8, idDepartamento: 3,
        statusGestor: 2, statusFinanceiro: 1, statusContabil: 2,
        nomeParceiro: 'Parceiro Teste', documentoParceiro: '123.456',
        tipoSolicitacao: 2, tipoPagamento: 1, formaPagamento: 2, tipoRelatorio: 3,
        incluirAnexos: true,
      });

      expect(texto.split(' · ')).toEqual([
        'Tipo de título: Solicitação',
        'Código: SOL-000123',
        'Nº do documento: NF-9',
        'Período da solicitação: 01/11/2025 a 30/11/2025',
        'Período de vencimento: 01/12/2025 a …',
        'Período de aprovação do financeiro: … a 31/12/2025',
        'Solicitante: Ana Teste',
        'Atribuído a: Bruno Teste',
        'Departamento: Compras',
        'Status gestor: Autorizado',
        'Status financeiro: Aguardando Autorização',
        'Status contábil: Lançado',
        'Nome do parceiro: Parceiro Teste',
        'CPF/CNPJ do parceiro: 123.456',
        'Tipo de solicitação: Pagamento',
        'Tipo de pagamento: Nacional',
        'Forma de pagamento: Transferência Bancária',
        'Tipo de relatório: Reembolso',
        'Incluir anexos: Sim',
      ]);
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
