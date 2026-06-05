import { Component, OnDestroy, OnInit } from "@angular/core";
import { MatSnackBar, MatSnackBarConfig } from "@angular/material/snack-bar";
import { ActivatedRoute, Router } from "@angular/router";
import { FuncaoDto } from "app/models/authentication/FuncaoDto.model";
import { UsuarioDto } from "app/models/authentication/UserDto.model";
import { AuthService } from "app/services/auth.service";
import { UsersService } from "app/services/users.service";
import { GenericResponse } from "app/models/GenericResponse";
import { DepartmentService } from "app/services/department.service";
import { DepartamentoModel } from "app/models/department/departamento.model";
import { Subscription } from "rxjs";

@Component({
    selector: "create-user",
    templateUrl: "./create-user.component.html",
    styleUrls: ["./create-user.component.css"],
    standalone: false
})
export class CreateUserComponent implements OnInit, OnDestroy {
  showAdm: boolean = false;

  userId: number = 0;
  model: UsuarioDto;
  isEdit: boolean = false;

  isAdmin: boolean = false;
  isManager: boolean = false;
  isRequester: boolean = false;
  isFinancial: boolean = false;
  isAccounting: boolean = false;

  isBusy: boolean = false;

  password: string = "";
  passwordConfirmation: string = "";

  private subs = new Subscription();

  get isReadOnly(): boolean {
    return this.model.origem > 0;
  }

  departmentList: DepartamentoModel[] = [];

  constructor(
    private auth: AuthService,
    private router: Router,
    private thisRoute: ActivatedRoute,
    private snack: MatSnackBar,
    private users: UsersService,
    private departmentService: DepartmentService
  ) {}

  ngOnInit() {
    this.getDepartmentList();
    this.subs.add(this.thisRoute.params.subscribe((p) => {
      if (p["id"]) {
        this.userId = p["id"];
        this.isEdit = true;
      }

      this.initForm();
    }));
  }

  ngOnDestroy() {
    this.subs.unsubscribe();
  }

  async getDepartmentList() {
    try {
      const res = await this.departmentService.GetAll().toPromise();
      if (res.success) {
        this.departmentList = res.data;
      }
    } catch (ex) {
      console.log(ex);
    }
  }


  async initForm() {
    if (!this.isEdit) {
      this.model = new UsuarioDto();

      this.model.idUsuario = 0;
      this.model.dataCadastro = new Date();
      this.model.usuarioCadastro = this.auth.CurrentUser.idUsuario;
      this.model.origem = 0;
      this.model.idExterno = "";
      this.model.ativo = true;
      this.model.nome = "";
      this.model.email = "";

      this.model.usuarioCadastro = this.auth.CurrentUser.idUsuario;
    } else {
      await this.getUserInfo();
    }
  }

  async getUserInfo(): Promise<boolean> {
    try {
      let req = await this.users.GetById(this.userId).toPromise();
      if (req.success) {
        this.model = req.data as UsuarioDto;

        this.isAdmin = !!this.model.funcoes.find(f => f.alias === "ADM");
        this.isRequester = !!this.model.funcoes.find(f => f.alias === "SOL");
        this.isManager = !!this.model.funcoes.find(f => f.alias === "GES");
        this.isFinancial = !!this.model.funcoes.find(f => f.alias === "FIN");
        this.isAccounting = !!this.model.funcoes.find(f => f.alias === "CON");
      } else {
        return false;
      }
    } catch (ex) {
      console.log(ex);
      return false;
    }

    return true;
  }

  alert(message): void {
    var cfg = new MatSnackBarConfig();
    cfg.duration = 5000;

    this.snack.open(message, "OK", cfg);
  } 

  validate(): string[] {
    let errors: string[] = [];

    if(this.model.origem === 1) {
      if(this.model.idExterno.trim() === "" && !this.isEdit) {
        errors.push("Você deve informar um usuário do Active Directory válido.");
      }

      return errors;
    }

    if(this.model.email.trim() === "") {
      errors.push("O campo 'E-mail' é obrigatório.");
    }

    if(this.model.nome.trim() === "") {
      errors.push("O campo 'Nome' é obrigatório.");
    }

    if(!this.isAdmin && !this.isManager && !this.isFinancial && !this.isRequester && !this.isAccounting) {
      errors.push("Você deve selecionar ao menos uma função para este usuário.");
    }

    if(!this.model.idDepartamento) {
      errors.push("O campo 'Departamento' é obrigatório.");
    }

    if(!this.isEdit) {
      if(this.password.trim() === "") {
        errors.push("O campo 'Senha' é obrigatório.");
      }
  
      if(this.passwordConfirmation.trim() === "") {
        errors.push("O campo 'Confirmação da Senha' é obrigatório.");
      }
  
      if(this.password != this.passwordConfirmation) {
        errors.push("As senhas devem ser iguais.");
      }
    }
    else if(this.password || this.passwordConfirmation) {
      if(this.password.trim() === "") {
        errors.push("O campo 'Senha' é obrigatório para alteração da senha.");
      }
  
      if(this.passwordConfirmation.trim() === "") {
        errors.push("O campo 'Confirmação da Senha' é obrigatório para alteração da senha.");
      }
  
      if(this.password != this.passwordConfirmation) {
        errors.push("As senhas devem ser iguais.");
      }
    }

    return errors;
  }

  async save() {
    let errors = this.validate();

    if(errors.length > 0) {
      this.alert(errors.join("\r\n"));
      return false;
    }
    
    if(this.isBusy)
      return;

    this.isBusy = true;

    this.model.senha = this.password;

    this.model.funcoes = [];
    if(this.isAdmin) {
      this.model.funcoes.push({
        idFuncao: 1, nome: "", alias: ""
      } as FuncaoDto)
    }

    if(this.isRequester) {
      this.model.funcoes.push({
        idFuncao: 2, nome: "", alias: ""
      } as FuncaoDto)
    }

    if(this.isManager) {
      this.model.funcoes.push({
        idFuncao: 3, nome: "", alias: ""
      } as FuncaoDto)
    }

    if(this.isFinancial) {
      this.model.funcoes.push({
        idFuncao: 4, nome: "", alias: ""
      } as FuncaoDto)
    }

    if(this.isAccounting) {
      this.model.funcoes.push({
        idFuncao: 5, nome: "", alias: ""
      } as FuncaoDto)
    }

    try {
      await this.users.Upsert(this.model).toPromise();
    }
    catch(ex) {
      this.isBusy = false;

      this.snack.open("Ocorreu um erro ao salvar os dados deste usuário.", "OK");

      return;
    }

    this.router.navigateByUrl("/config");
  }

  async searchDirectory() {
    if(this.isBusy)
      return;

    this.isBusy = true;

    try {
      let adUser = await this.users.SearchDirectory(this.model.codigo).toPromise() as GenericResponse<UsuarioDto>;

      if(!adUser.success) {
        this.snack.open(adUser.message, "OK");
        this.isBusy = false;
        return;
      }

      this.model = adUser.data;
      this.isBusy = false;
    }
    catch(ex) {
      this.isBusy = false;

      this.snack.open("Ocorreu um erro ao buscar os dados do usuário.", "OK");

      return;
    }
  }

}
