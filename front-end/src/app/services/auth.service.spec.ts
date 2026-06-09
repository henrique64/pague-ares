import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { LocalStorageService } from './local-storage.service';
import { BaseUrlService } from './base-url.service';
import { EnumFuncao } from 'app/models/authentication/EnumFuncao.model';

const FAKE_TOKEN = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.' +
  btoa(JSON.stringify({ sub: 'Teste', nameid: '1', Roles: 'ADM', exp: Math.floor(Date.now() / 1000) + 3600 }))
    .replace(/=/g, '').replace(/\+/g, '-').replace(/\//g, '_') +
  '.assinatura';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;
  let localStorage: LocalStorageService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [AuthService, LocalStorageService, BaseUrlService]
    });

    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
    localStorage = TestBed.inject(LocalStorageService);
    window.localStorage.clear();
  });

  afterEach(() => {
    http.verify();
  });

  it('Authenticate com sucesso armazena token no localStorage', async () => {
    const promise = service.Authenticate('usuario', 'senha');

    const req = http.expectOne(r => r.url.includes('/api/auth') || r.url.includes('/api/Auth'));
    req.flush({
      success: true,
      data: { token: FAKE_TOKEN, expires: new Date(Date.now() + 3600000).toISOString() }
    });

    const result = await promise;
    expect(result?.success).toBeTrue();
    expect(window.localStorage.getItem('Token')).not.toBeNull();
  });

  it('Authenticate com falha na rede retorna null', async () => {
    const promise = service.Authenticate('usuario', 'senha');

    const req = http.expectOne(r => r.url.includes('/api/auth') || r.url.includes('/api/Auth'));
    req.error(new ProgressEvent('error'));

    const result = await promise;
    expect(result).toBeNull();
  });

  it('IsInRole retorna true quando a role está presente no token', () => {
    service.CurrentUser = {
      idUsuario: 1, email: '', nome: 'ADM',
      origem: 0, idExterno: '', senha: '', idDepartamento: 0,
      ativo: true, dataCadastro: new Date(), usuarioCadastro: 0,
      ultimaAlteracao: '', usuarioAlteracao: 0, codigo: '',
      funcoes: [{ alias: 'ADM', nome: 'Administrador' } as any]
    } as any;

    expect(service.IsInRole(EnumFuncao.Administrador)).toBeTrue();
  });

  it('IsInRole retorna false quando a role não está presente', () => {
    service.CurrentUser = {
      idUsuario: 1, email: '', nome: 'USR',
      origem: 0, idExterno: '', senha: '', idDepartamento: 0,
      ativo: true, dataCadastro: new Date(), usuarioCadastro: 0,
      ultimaAlteracao: '', usuarioAlteracao: 0, codigo: '',
      funcoes: [{ alias: 'USR', nome: 'Usuário' } as any]
    } as any;

    expect(service.IsInRole(EnumFuncao.Administrador)).toBeFalse();
  });
});
