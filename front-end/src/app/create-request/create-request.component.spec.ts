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

  beforeEach(async () => {
    paymentSpy = jasmine.createSpyObj('PaymentService', ['GetRequest', 'UpsertRequest']);
    snackSpy = jasmine.createSpyObj('MatSnackBar', ['open']);
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
        { provide: DialogService, useValue: {} }
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
});
