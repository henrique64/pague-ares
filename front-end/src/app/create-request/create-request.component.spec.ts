import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { CreateRequestComponent } from './create-request.component';
import { AuthService } from 'app/services/auth.service';
import { PaymentService } from 'app/services/payment.service';
import { UsersService } from 'app/services/users.service';
import { BaseUrlService } from 'app/services/base-url.service';
import { ProviderService } from 'app/services/provider.service';
import { ExportService } from 'app/services/export.service';
import { PreviewTokenService } from 'app/services/preview-token.service';
import { DialogService } from 'app/components/dialog/dialog.service';
import { EnumDialogResult } from 'app/components/dialog/dialogresult.enum';
import { PaymentRequestModel } from 'app/models/payment/payment-request.model';

const fakeCurrentUser = {
  idUsuario: 1, email: 'a@a.com', nome: 'Teste',
  origem: 0, idExterno: '', senha: '', idDepartamento: 1,
  ativo: true, dataCadastro: new Date(), usuarioCadastro: 0,
  ultimaAlteracao: '', usuarioAlteracao: 0, codigo: '', funcoes: []
} as any;

describe('CreateRequestComponent', () => {
  let component: CreateRequestComponent;
  let fixture: ComponentFixture<CreateRequestComponent>;
  let paymentSpy: jasmine.SpyObj<PaymentService>;
  let snackSpy: jasmine.SpyObj<MatSnackBar>;
  let dialogSpy: jasmine.SpyObj<DialogService>;

  beforeEach(async () => {
    paymentSpy = jasmine.createSpyObj('PaymentService', ['GetRequest', 'UpsertRequest']);
    snackSpy = jasmine.createSpyObj('MatSnackBar', ['open']);
    dialogSpy = jasmine.createSpyObj('DialogService', ['confirm']);
    dialogSpy.confirm.and.returnValue(of(EnumDialogResult.Yes) as any);
    const authSpy = jasmine.createSpyObj('AuthService', ['IsInRole'], { CurrentUser: fakeCurrentUser });
    const usersSpy = jasmine.createSpyObj('UsersService', ['GetList']);
    usersSpy.GetList.and.returnValue(of({ success: true, message: '', data: [], records: 0, pages: 0, page: 1 }));

    await TestBed.configureTestingModule({
      declarations: [CreateRequestComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        { provide: AuthService, useValue: authSpy },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigateByUrl', 'navigate']) },
        { provide: ActivatedRoute, useValue: { params: of({}), queryParamMap: of({ get: () => null }) } },
        { provide: PaymentService, useValue: paymentSpy },
        { provide: MatSnackBar, useValue: snackSpy },
        { provide: UsersService, useValue: usersSpy },
        { provide: BaseUrlService, useValue: {} },
        { provide: ProviderService, useValue: {} },
        { provide: ExportService, useValue: {} },
        { provide: PreviewTokenService, useValue: {} },
        { provide: DialogService, useValue: dialogSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(CreateRequestComponent);
    component = fixture.componentInstance;
    component.model = new PaymentRequestModel();
    component.model.documentos = [];
  });

  it('validate retorna erro quando descricao está vazia', () => {
    component.model.descricao = '';
    const errors = component.validate();
    expect(errors.some(e => e.includes('Descrição'))).toBeTrue();
  });

  it('validate retorna erro quando numDocParceiro está vazio', () => {
    component.model.numDocParceiro = '';
    const errors = component.validate();
    expect(errors.some(e => e.includes('CPF'))).toBeTrue();
  });

  it('validate retorna erro para CPF inválido', () => {
    component.model.numDocParceiro = '11111111111'; // CPF sem formatação com dígitos repetidos (inválido)
    const errors = component.validate();
    expect(errors.some(e => e.includes('inválido'))).toBeTrue();
  });

  it('validate sem erros para modelo preenchido válido', () => {
    component.model.descricao = 'Pagamento';
    component.model.valor = 100;
    component.model.centroCusto = 'TI';
    component.model.projeto = 'PJ1';
    component.model.pca = 'PCA1';
    component.model.numDocParceiro = '12345678909'; // CPF sem formatação (≤11 dígitos)
    component.model.tipoAutorizacao = 2;
    component.model.idGestor = 1;
    const errors = component.validate();
    expect(errors.length).toBe(0);
  });

  it('save com falha na API reseta isBusy para false', async () => {
    component.userList = [{ idUsuario: 1, nome: 'Gestor' } as any];
    component.model.descricao = 'Pagamento';
    component.model.valor = 100;
    component.model.centroCusto = 'TI';
    component.model.projeto = 'PJ1';
    component.model.pca = 'PCA1';
    component.model.numDocParceiro = '123.456.789-09';
    component.model.tipoAutorizacao = 2;
    component.model.idGestor = 1;

    paymentSpy.UpsertRequest.and.returnValue(of({ success: false, message: 'Erro', data: null as any, records: 0, pages: 0, page: 1 }));

    await component.save(true);

    expect(component.isBusy).toBeFalse();
  });

  // Bug 1 — rascunho não valida
  it('save(true) salva rascunho mesmo sem gestor e com campos vazios', async () => {
    component.model.tipoAutorizacao = 2; // Por Sistema, exigiria gestor no envio
    component.model.idGestor = null;     // sem gestor
    component.model.descricao = '';      // campos obrigatórios vazios
    paymentSpy.UpsertRequest.and.returnValue(of({ success: true, message: '', data: component.model as any, records: 0, pages: 0, page: 1 }));

    await component.save(true);

    expect(paymentSpy.UpsertRequest).toHaveBeenCalled();
    expect(snackSpy.open).not.toHaveBeenCalled();
  });

  it('save(false) ainda valida e bloqueia quando incompleto', async () => {
    component.model.tipoAutorizacao = 2;
    component.model.idGestor = null;
    component.model.descricao = '';

    await component.save(false);

    expect(paymentSpy.UpsertRequest).not.toHaveBeenCalled();
    expect(snackSpy.open).toHaveBeenCalled();
  });

  // Bug 2 — somente-leitura baseado em dono/rascunho/cancelado
  function buildResponse(overrides: Partial<PaymentRequestModel>) {
    const data = Object.assign(new PaymentRequestModel(), {
      idSolicitacao: 5, idUsuario: 1, rascunho: true, cancelado: false, statusGestor: 1
    }, overrides);
    return of({ success: true, message: '', data: data as any, records: 0, pages: 0, page: 1 });
  }

  it('rascunho do próprio usuário é editável ao reabrir', async () => {
    component.isEdit = true;
    component.requestId = 5;
    paymentSpy.GetRequest.and.returnValue(buildResponse({ idUsuario: 1, rascunho: true }));

    await component.initForm();

    expect(component.isUserView).toBeTrue();
    expect(component.isReadOnly).toBeFalse();
  });

  it('registro de outro usuário é somente-leitura', async () => {
    component.isEdit = true;
    component.requestId = 5;
    paymentSpy.GetRequest.and.returnValue(buildResponse({ idUsuario: 999 }));

    await component.initForm();

    expect(component.isReadOnly).toBeTrue();
  });

  it('registro cancelado é somente-leitura mesmo sendo do usuário', async () => {
    component.isEdit = true;
    component.requestId = 5;
    paymentSpy.GetRequest.and.returnValue(buildResponse({ idUsuario: 1, cancelado: true }));

    await component.initForm();

    expect(component.isReadOnly).toBeTrue();
  });

  // Bug 3b — cancelar não inicia o fluxo de aprovação
  it('cancel() marca cancelado sem tirar do rascunho', async () => {
    component.model.rascunho = true;
    component.model.cancelado = false;
    let enviado: PaymentRequestModel | null = null;
    paymentSpy.UpsertRequest.and.callFake((m: PaymentRequestModel) => {
      enviado = m;
      return of({ success: true, message: '', data: m as any, records: 0, pages: 0, page: 1 });
    });

    await component.cancel();

    expect(paymentSpy.UpsertRequest).toHaveBeenCalled();
    expect(enviado!.cancelado).toBeTrue();
    expect(enviado!.rascunho).toBeTrue();
  });

  // Estado inválido: gestor selecionado não está na lista de usuários carregada
  it('save(true) não quebra quando o gestor não está na lista de usuários', async () => {
    component.userList = [];
    component.model.idGestor = 999;
    paymentSpy.UpsertRequest.and.returnValue(of({ success: true, message: '', data: component.model as any, records: 0, pages: 0, page: 1 }));

    await component.save(true);

    expect(paymentSpy.UpsertRequest).toHaveBeenCalled();
  });

  // Gating de etapa: uma etapa decidida (2/3) não pode mais ser editada
  it('etapa decidida (status 2) bloqueia a autorização mesmo com o papel', async () => {
    const auth = TestBed.inject(AuthService) as jasmine.SpyObj<AuthService>;
    auth.IsInRole.and.returnValue(true);
    component.isEdit = true;
    component.requestId = 5;
    paymentSpy.GetRequest.and.returnValue(buildResponse({ idUsuario: 1, statusGestor: 2, statusFinanceiro: 2 }));

    await component.initForm();

    expect(component.isManagerReadOnly).toBeTrue();
    expect(component.isFinanceReadOnly).toBeTrue();
  });
});
