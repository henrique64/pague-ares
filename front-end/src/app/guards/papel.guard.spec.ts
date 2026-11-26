import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { papelGuard } from './papel.guard';
import { AuthService } from 'app/services/auth.service';
import { EnumFuncao } from 'app/models/authentication/EnumFuncao.model';

describe('papelGuard', () => {
  const urlTree = {} as UrlTree;
  let router: jasmine.SpyObj<Router>;

  function configurar(papeisDoUsuario: EnumFuncao[]) {
    const auth = jasmine.createSpyObj('AuthService', ['IsInRole']);
    auth.IsInRole.and.callFake((funcao: EnumFuncao) => papeisDoUsuario.includes(funcao));
    router = jasmine.createSpyObj('Router', ['createUrlTree']);
    router.createUrlTree.and.returnValue(urlTree);

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: Router, useValue: router }
      ]
    });
  }

  function executar() {
    const guard = papelGuard(EnumFuncao.Financeiro, EnumFuncao.Administrador);
    return TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));
  }

  it('libera Financeiro', () => {
    configurar([EnumFuncao.Financeiro]);
    expect(executar()).toBeTrue();
  });

  it('libera Administrador', () => {
    configurar([EnumFuncao.Administrador]);
    expect(executar()).toBeTrue();
  });

  it('redireciona Contabilidade para /payments', () => {
    configurar([EnumFuncao.Contabilidade]);
    expect(executar()).toBe(urlTree);
    expect(router.createUrlTree).toHaveBeenCalledWith(['/payments']);
  });

  it('redireciona Solicitante para /payments', () => {
    configurar([EnumFuncao.Solicitante]);
    expect(executar()).toBe(urlTree);
    expect(router.createUrlTree).toHaveBeenCalledWith(['/payments']);
  });
});
