import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { CreateRddvComponent } from './create-rddv.component';
import { AuthService } from 'app/services/auth.service';
import { RddvService } from 'app/services/rddv.service';
import { UsersService } from 'app/services/users.service';
import { TravelTypeService } from 'app/services/travel-type.service';
import { ConfigService } from 'app/services/config.service';
import { DepartmentService } from 'app/services/department.service';
import { BaseUrlService } from 'app/services/base-url.service';
import { ExportService } from 'app/services/export.service';
import { PreviewTokenService } from 'app/services/preview-token.service';
import { DialogService } from 'app/components/dialog/dialog.service';
import { EnumDialogResult } from 'app/components/dialog/dialogresult.enum';
import { RddvModel } from 'app/models/rddv/rddv.model';

const fakeCurrentUser = {
  idUsuario: 1, email: 'a@a.com', nome: 'Funcionário',
  origem: 0, idExterno: '', senha: '', idDepartamento: 1,
  ativo: true, dataCadastro: new Date(), usuarioCadastro: 0,
  ultimaAlteracao: '', usuarioAlteracao: 0, codigo: '', funcoes: []
} as any;

describe('CreateRddvComponent', () => {
  let component: CreateRddvComponent;
  let fixture: ComponentFixture<CreateRddvComponent>;
  let rddvSpy: jasmine.SpyObj<RddvService>;
  let snackSpy: jasmine.SpyObj<MatSnackBar>;
  let dialogSpy: jasmine.SpyObj<DialogService>;

  beforeEach(async () => {
    rddvSpy = jasmine.createSpyObj('RddvService', ['GetRddv', 'UpsertRddv']);
    snackSpy = jasmine.createSpyObj('MatSnackBar', ['open']);
    dialogSpy = jasmine.createSpyObj('DialogService', ['confirm']);
    dialogSpy.confirm.and.returnValue(of(EnumDialogResult.Yes) as any);
    const authSpy = jasmine.createSpyObj('AuthService', ['IsInRole'], { CurrentUser: fakeCurrentUser });
    const usersSpy = jasmine.createSpyObj('UsersService', ['GetList']);
    usersSpy.GetList.and.returnValue(of({ success: true, message: '', data: [], records: 0, pages: 0, page: 1 }));
    const travelSpy = jasmine.createSpyObj('TravelTypeService', ['GetAll']);
    travelSpy.GetAll.and.returnValue(of({ success: true, message: '', data: [], records: 0, pages: 0, page: 1 }));
    const configSpy = jasmine.createSpyObj('ConfigService', ['GetAll', 'GetRddvConfig']);
    configSpy.GetAll.and.returnValue(of({ success: true, message: '', data: [], records: 0, pages: 0, page: 1 }));
    configSpy.GetRddvConfig.and.returnValue(of({ success: true, message: '', data: { ValorKm: 0 }, records: 0, pages: 0, page: 1 }));
    const deptSpy = jasmine.createSpyObj('DepartmentService', ['GetAll']);
    deptSpy.GetAll.and.returnValue(of({ success: true, message: '', data: [], records: 0, pages: 0, page: 1 }));

    await TestBed.configureTestingModule({
      declarations: [CreateRddvComponent],
      providers: [
        { provide: AuthService, useValue: authSpy },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigateByUrl', 'navigate']) },
        { provide: ActivatedRoute, useValue: { params: of({}), queryParamMap: of({ get: () => null }) } },
        { provide: RddvService, useValue: rddvSpy },
        { provide: MatSnackBar, useValue: snackSpy },
        { provide: UsersService, useValue: usersSpy },
        { provide: TravelTypeService, useValue: travelSpy },
        { provide: ConfigService, useValue: configSpy },
        { provide: DepartmentService, useValue: deptSpy },
        { provide: BaseUrlService, useValue: {} },
        { provide: ExportService, useValue: {} },
        { provide: PreviewTokenService, useValue: {} },
        { provide: DialogService, useValue: dialogSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(CreateRddvComponent);
    component = fixture.componentInstance;
    component.model = new RddvModel();
    component.model.documentos = [];
    component.model.despesas = [];
  });

  it('validate retorna erro quando cpf está vazio', () => {
    component.model.cpf = '';
    const errors = component.validate();
    expect(errors.some(e => e.includes('CPF'))).toBeTrue();
  });

  it('validate retorna erro para CPF inválido', () => {
    component.model.cpf = '111.111.111-11';
    const errors = component.validate();
    expect(errors.some(e => e.includes('inválido'))).toBeTrue();
  });

  it('validate retorna erro quando nomeFuncionario está vazio', () => {
    component.model.nomeFuncionario = '';
    const errors = component.validate();
    expect(errors.some(e => e.includes('Nome do Funcionário'))).toBeTrue();
  });

  it('getTotal calcula soma corretamente para tipo de despesa', () => {
    component.model.despesas = [
      { tipoDespesa: 1, valor: 50 } as any,
      { tipoDespesa: 1, valor: 30 } as any,
      { tipoDespesa: 2, valor: 20 } as any
    ];
    expect(component.getTotal(1)).toBe(80);
    expect(component.getTotal(2)).toBe(20);
  });

  it('ngOnDestroy cancela subscriptions', () => {
    spyOn((component as any).subs, 'unsubscribe');
    component.ngOnDestroy();
    expect((component as any).subs.unsubscribe).toHaveBeenCalled();
  });

  // Permite que save()/cancel() rodem o corpo sem depender de ngOnInit
  function prepareSaveDeps() {
    (component as any).rddvConfig = { ValorKm: 1 };
    component.travelTypes = [{ idTipoViagem: 1, valorDiaria: 10 } as any];
    component.model.tipoViagem = 1;
  }

  // Bug 1 — rascunho não valida
  it('save(true) salva rascunho mesmo sem gestor e com campos vazios', async () => {
    prepareSaveDeps();
    component.model.tipoAutorizacao = 2; // Por Sistema, exigiria gestor no envio
    component.model.idGestor = null;
    rddvSpy.UpsertRddv.and.returnValue(of({ success: true, message: '', data: component.model as any, records: 0, pages: 0, page: 1 }));

    await component.save(true);

    expect(rddvSpy.UpsertRddv).toHaveBeenCalled();
    expect(snackSpy.open).not.toHaveBeenCalled();
  });

  it('save(false) ainda valida e bloqueia quando incompleto', async () => {
    await component.save(false);

    expect(rddvSpy.UpsertRddv).not.toHaveBeenCalled();
    expect(snackSpy.open).toHaveBeenCalled();
  });

  // Bug 2 — somente-leitura baseado em dono/rascunho/cancelado
  function buildResponse(overrides: Partial<RddvModel>) {
    const data = Object.assign(new RddvModel(), {
      idRelatorio: 5, idUsuario: 1, rascunho: true, cancelado: false, statusGestor: 1
    }, overrides);
    return of({ success: true, message: '', data: data as any, records: 0, pages: 0, page: 1 });
  }

  it('rascunho do próprio usuário é editável ao reabrir', async () => {
    component.isEdit = true;
    component.reportId = 5;
    rddvSpy.GetRddv.and.returnValue(buildResponse({ idUsuario: 1, rascunho: true }));

    await component.initForm();

    expect(component.isUserView).toBeTrue();
    expect(component.isReadOnly).toBeFalse();
  });

  it('registro de outro usuário é somente-leitura', async () => {
    component.isEdit = true;
    component.reportId = 5;
    rddvSpy.GetRddv.and.returnValue(buildResponse({ idUsuario: 999 }));

    await component.initForm();

    expect(component.isReadOnly).toBeTrue();
  });

  it('registro cancelado é somente-leitura mesmo sendo do usuário', async () => {
    component.isEdit = true;
    component.reportId = 5;
    rddvSpy.GetRddv.and.returnValue(buildResponse({ idUsuario: 1, cancelado: true }));

    await component.initForm();

    expect(component.isReadOnly).toBeTrue();
  });

  // Bug 3b — cancelar não inicia o fluxo de aprovação
  it('cancel() marca cancelado sem tirar do rascunho', async () => {
    prepareSaveDeps();
    component.model.rascunho = true;
    component.model.cancelado = false;
    let enviado: RddvModel | null = null;
    rddvSpy.UpsertRddv.and.callFake((m: RddvModel) => {
      enviado = m;
      return of({ success: true, message: '', data: m as any, records: 0, pages: 0, page: 1 });
    });

    await component.cancel();

    expect(rddvSpy.UpsertRddv).toHaveBeenCalled();
    expect(enviado!.cancelado).toBeTrue();
    expect(enviado!.rascunho).toBeTrue();
  });

  // Estado inválido: salvar rascunho antes de config/tipos de viagem carregarem
  it('save(true) não quebra quando rddvConfig/travelTypes não carregaram', async () => {
    // rddvConfig permanece null e travelTypes vazio de propósito
    component.model.rascunho = true;
    component.model.tipoViagem = 1;
    rddvSpy.UpsertRddv.and.returnValue(of({ success: true, message: '', data: component.model as any, records: 0, pages: 0, page: 1 }));

    await component.save(true);

    expect(rddvSpy.UpsertRddv).toHaveBeenCalled();
  });

  // Gating de etapa: uma etapa decidida (2/3) não pode mais ser editada
  it('etapa decidida (status 2) bloqueia a autorização mesmo com o papel', async () => {
    const auth = TestBed.inject(AuthService) as jasmine.SpyObj<AuthService>;
    auth.IsInRole.and.returnValue(true);
    component.isEdit = true;
    component.reportId = 5;
    rddvSpy.GetRddv.and.returnValue(buildResponse({ idUsuario: 1, rascunho: false, statusGestor: 2, statusFinanceiro: 2, statusContabilidade: null }));

    await component.initForm();

    expect(component.isManagerReadOnly).toBeTrue();   // gestor já aprovou → travado
    expect(component.isFinanceReadOnly).toBeTrue();    // financeiro já aprovou → travado
  });

  it('etapa pendente (status 1/null) permanece editável para o papel', async () => {
    const auth = TestBed.inject(AuthService) as jasmine.SpyObj<AuthService>;
    auth.IsInRole.and.returnValue(true);
    component.isEdit = true;
    component.reportId = 5;
    rddvSpy.GetRddv.and.returnValue(buildResponse({ idUsuario: 1, rascunho: false, statusGestor: 1, statusFinanceiro: null, statusContabilidade: null }));

    await component.initForm();

    expect(component.isManagerReadOnly).toBeFalse();   // gestor pendente (1)
    expect(component.isFinanceReadOnly).toBeFalse();    // financeiro pendente (null)
  });
});
