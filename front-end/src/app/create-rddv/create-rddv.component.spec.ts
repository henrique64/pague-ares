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

  beforeEach(async () => {
    rddvSpy = jasmine.createSpyObj('RddvService', ['GetRddv', 'UpsertRddv']);
    snackSpy = jasmine.createSpyObj('MatSnackBar', ['open']);
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
        { provide: DialogService, useValue: {} }
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
});
