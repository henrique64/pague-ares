import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { LoginComponent } from './login.component';
import { AuthService } from '../services/auth.service';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let authSpy: jasmine.SpyObj<AuthService>;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    authSpy = jasmine.createSpyObj('AuthService', ['Authenticate'], { IsAuthenticated: false });
    routerSpy = jasmine.createSpyObj('Router', ['navigate', 'navigateByUrl']);

    await TestBed.configureTestingModule({
      declarations: [LoginComponent],
      providers: [
        { provide: AuthService, useValue: authSpy },
        { provide: Router, useValue: routerSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('validateForm retorna false quando usuário está vazio', () => {
    component.user.name = '';
    component.user.password = 'senha';
    expect(component.validateForm()).toBeFalse();
  });

  it('validateForm retorna false quando senha está vazia', () => {
    component.user.name = 'usuario';
    component.user.password = '';
    expect(component.validateForm()).toBeFalse();
  });

  it('validateForm retorna true com campos preenchidos', () => {
    component.user.name = 'usuario';
    component.user.password = 'senha';
    expect(component.validateForm()).toBeTrue();
  });

  it('ExecuteLogin com erro de rede exibe mensagem de erro de conexão', async () => {
    component.user.name = 'usuario';
    component.user.password = 'senha';
    authSpy.Authenticate.and.rejectWith(new Error('Network error'));

    await component.ExecuteLogin();

    expect(component.erro).toBeTrue();
    expect(component.mensagemErro).toContain('Erro de conexão');
  });

  it('ExecuteLogin bem-sucedido navega para /payments', async () => {
    component.user.name = 'usuario';
    component.user.password = 'senha';
    authSpy.Authenticate.and.resolveTo({ success: true, message: '', data: null, records: 0, pages: 0, page: 1 });

    await component.ExecuteLogin();

    expect(routerSpy.navigate).toHaveBeenCalledWith(['/payments']);
  });

  it('ExecuteLogin com resposta de falha exibe erro', async () => {
    component.user.name = 'usuario';
    component.user.password = 'senha';
    authSpy.Authenticate.and.resolveTo({ success: false, message: 'inválido', data: null, records: 0, pages: 0, page: 1 });

    await component.ExecuteLogin();

    expect(component.erro).toBeTrue();
  });

  it('isBusy é false após ExecuteLogin terminar', async () => {
    component.user.name = 'usuario';
    component.user.password = 'senha';
    authSpy.Authenticate.and.rejectWith(new Error('err'));

    await component.ExecuteLogin();

    expect(component.ocupado).toBeFalse();
  });
});
