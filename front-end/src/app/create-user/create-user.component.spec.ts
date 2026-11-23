import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { CreateUserComponent } from './create-user.component';
import { AuthService } from 'app/services/auth.service';
import { UsersService } from 'app/services/users.service';
import { DepartmentService } from 'app/services/department.service';
import { UsuarioDto } from 'app/models/authentication/UserDto.model';

const fakeCurrentUser = {
  idUsuario: 1, email: 'a@a.com', nome: 'Admin',
  origem: 0, idExterno: '', senha: '', idDepartamento: 1,
  ativo: true, dataCadastro: new Date(), usuarioCadastro: 0,
  ultimaAlteracao: '', usuarioAlteracao: 0, codigo: '', funcoes: []
} as any;

const fakeDepts = [
  { idDepartamento: 1, nome: 'TI', codigoLancamento: 'TI01' },
  { idDepartamento: 2, nome: 'Financeiro', codigoLancamento: 'FI01' }
];

describe('CreateUserComponent', () => {
  let component: CreateUserComponent;
  let fixture: ComponentFixture<CreateUserComponent>;
  let deptSpy: jasmine.SpyObj<DepartmentService>;

  beforeEach(async () => {
    deptSpy = jasmine.createSpyObj('DepartmentService', ['GetAll']);
    deptSpy.GetAll.and.returnValue(of({ success: true, message: '', data: fakeDepts, records: 2, pages: 1, page: 1 }));
    const authSpy = jasmine.createSpyObj('AuthService', ['IsInRole'], { CurrentUser: fakeCurrentUser });
    const usersSpy = jasmine.createSpyObj('UsersService', ['GetById', 'Upsert']);

    await TestBed.configureTestingModule({
      declarations: [CreateUserComponent],
      providers: [
        { provide: AuthService, useValue: authSpy },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigateByUrl', 'navigate']) },
        { provide: ActivatedRoute, useValue: { params: of({}) } },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj('MatSnackBar', ['open']) },
        { provide: UsersService, useValue: usersSpy },
        { provide: DepartmentService, useValue: deptSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(CreateUserComponent);
    component = fixture.componentInstance;
    component.model = new UsuarioDto();
    component.model.origem = 0;
    component.model.email = '';
    component.model.nome = '';
    component.model.funcoes = [];
  });

  it('validate retorna erro quando nenhuma função está selecionada', () => {
    component.model.email = 'a@a.com';
    component.model.nome = 'Nome';
    component.model.idDepartamento = 1;
    component.isAdmin = false;
    component.isManager = false;
    component.isFinancial = false;
    component.isRequester = false;
    component.isAccounting = false;
    component.password = 'senha123';
    component.passwordConfirmation = 'senha123';

    const errors = component.validate();
    expect(errors.some(e => e.includes('função'))).toBeTrue();
  });

  it('validate retorna erro quando senhas não coincidem', () => {
    component.model.email = 'a@a.com';
    component.model.nome = 'Nome';
    component.model.idDepartamento = 1;
    component.isRequester = true;
    component.password = 'senha123';
    component.passwordConfirmation = 'diferente';

    const errors = component.validate();
    expect(errors.some(e => e.includes('iguais'))).toBeTrue();
  });

  it('validate retorna array vazio para modelo válido', () => {
    component.model.email = 'a@a.com';
    component.model.nome = 'Nome';
    component.model.idDepartamento = 1;
    component.isRequester = true;
    component.password = 'senha123';
    component.passwordConfirmation = 'senha123';

    const errors = component.validate();
    expect(errors.length).toBe(0);
  });

  it('getDepartmentList preenche departmentList com dados da API', async () => {
    await component.getDepartmentList();
    expect(component.departmentList.length).toBe(2);
  });
});
