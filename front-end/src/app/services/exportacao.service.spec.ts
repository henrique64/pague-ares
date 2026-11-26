import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ExportacaoService } from './exportacao.service';
import { BaseUrlService } from './base-url.service';
import { FiltroExportacaoModel } from 'app/models/exportacao/filtro-exportacao.model';
import { EnumTipoTituloExportacao } from 'app/models/exportacao/enum-tipo-titulo-exportacao';

describe('ExportacaoService', () => {
  let service: ExportacaoService;
  let http: HttpTestingController;
  let base: string;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [ExportacaoService, BaseUrlService]
    });

    service = TestBed.inject(ExportacaoService);
    http = TestBed.inject(HttpTestingController);
    base = TestBed.inject(BaseUrlService).Exportacao.Base;
  });

  afterEach(() => http.verify());

  it('criar faz POST em api/exportacao com o filtro no corpo', () => {
    const filtro: FiltroExportacaoModel = {
      tipoTitulo: EnumTipoTituloExportacao.Solicitacao,
      dataSolicitacaoInicio: '2026-11-01',
      incluirAnexos: true
    };

    service.criar(filtro).subscribe();

    const req = http.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.url.endsWith('/api/exportacao')).toBeTrue();
    expect(req.request.body).toEqual(filtro);
    req.flush({ success: true });
  });

  it('listar faz GET em api/exportacao', () => {
    service.listar().subscribe();

    const req = http.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: [] });
  });

  it('obter faz GET em api/exportacao/{id}', () => {
    service.obter(42).subscribe();

    const req = http.expectOne(base + '/42');
    expect(req.request.method).toBe('GET');
    req.flush({ success: true });
  });

  it('gerarLinkDownload faz POST em api/exportacao/{id}/link-download sem corpo', () => {
    service.gerarLinkDownload(42).subscribe();

    const req = http.expectOne(base + '/42/link-download');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush({ success: true });
  });

  it('cancelar faz DELETE em api/exportacao/{id}', () => {
    service.cancelar(42).subscribe();

    const req = http.expectOne(base + '/42');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('urlDownload monta a URL de download com o token codificado', () => {
    const url = service.urlDownload('ab-_c=');

    expect(url.endsWith('/api/exportacao/download?token=ab-_c%3D')).toBeTrue();
  });
});
