import { Component, OnInit } from "@angular/core";
import { MatSnackBar } from "@angular/material/snack-bar";
import { Router } from "@angular/router";
import { EnumFuncao } from "app/models/authentication/EnumFuncao.model";
import { UsuarioDto } from "app/models/authentication/UserDto.model";
import { UserListModel } from "app/models/authentication/UserList.model";
import { ConfiguracaoModel } from "app/models/config/configuracao.model";
import { RddvConfigModel } from "app/models/config/rddv-config.model";
import { AuthService } from "app/services/auth.service";
import { ConfigService } from "app/services/config.service";
import { UsersService } from "app/services/users.service";

@Component({
    selector: "app-config",
    templateUrl: "./configuration.component.html",
    styleUrls: ["./configuration.component.css"],
    standalone: false
})
export class ConfigurationComponent implements OnInit {
  isBusy: boolean = false;
  isReadOnly: boolean = false;
  userList: UsuarioDto[] = [];

  userTypes = {
    0: "Usuário Interno",
    1: "Usuário Externo",
  };

  defaultValueMask = {
    prefix: "",
    thousands: ".",
    decimal: ",",
    align: "left",
  };

  /* Parâmetros de Configuração */

  configuration: ConfiguracaoModel[] = [];
  rddv: RddvConfigModel = null;

  constructor(private auth: AuthService, private users: UsersService, private config: ConfigService, private snack: MatSnackBar, private router: Router) {}

  ngOnInit() {
    if(!this.auth.IsInRole(EnumFuncao.Administrador)) {
      this.router.navigateByUrl("/");
      return;
    }

    this.getUsers();
    this.getConfig();
  }

  async getUsers() {
    try {
      let res = await this.users.GetAll().toPromise();

      if (res.success) {
        this.userList = res.data;
      }
    } catch (ex) {
      console.log(ex);
    }
  }

  async getConfig() {
    try {
      let res = await this.config.GetAll().toPromise();

      if (res.success) {
        this.configuration = res.data;

        this.rddv = RddvConfigModel.FromConfiguracaoModel(this.configuration);
      }
    } catch (ex) {
      console.log(ex);
    }
  }

  async saveRddvConfig() {
    let config = RddvConfigModel.ToConfiguracaoModel(this.rddv);

    try {
      let res = await this.config.Upsert(config).toPromise();

      if (res.success) {
        //this.configuration = res.data;

        this.snack.open("Configurações salvas com sucesso.");
      }
    } catch (ex) {
      console.log(ex);
      this.snack.open("Ocorreu um erro ao salvar as configurações. Tente novamente.", "OK", { duration: 4000 });
    }
  }
}
