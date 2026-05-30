import { Component, OnInit } from "@angular/core";
import { MatSnackBar, MatSnackBarConfig } from "@angular/material/snack-bar";
import { GenericResponse } from "app/models/GenericResponse";
import { TipoViagemModel } from "app/models/travel-type/travel-type.model";
import { TravelTypeService } from "app/services/travel-type.service";
import { UsersService } from "app/services/users.service";

@Component({
    selector: "app-travel-type-list",
    templateUrl: "./travel-type-list.component.html",
    styleUrls: ["./travel-type-list.component.css"],
    standalone: false
})
export class TravelTypeListComponent implements OnInit {

  isEditing: boolean;
  isNew: boolean;
  
  source: TipoViagemModel;
  model: TipoViagemModel;

  types: TipoViagemModel[] = [];

  constructor(
    private users: UsersService,
    private travel: TravelTypeService,
    private snack: MatSnackBar
  ) {}

  ngOnInit() {
    this.getTypes();
  }

  async getTypes() {
    try {
      let res = await this.travel.GetAll().toPromise();

      if (res.success) {
        this.types = res.data;
      }
    } catch (ex) {
      console.log(ex);
    }
  }

  new() {
    if (this.isEditing) return;

    this.isEditing = true;
    this.isNew = true;
    this.source = null;

    let temp = new TipoViagemModel();
    temp.nome = "";
    temp.valorDiaria = 0;
  
    this.model = temp;
  }

  edit(expense: TipoViagemModel) {
    if (this.isEditing) return;

    this.isEditing = true;
    this.isNew = false;
    this.source = expense;

    let temp = new TipoViagemModel();
    Object.assign(temp, expense);

    this.model = temp;
  }

  cancelEdit() {
    if (!this.isEditing) return;

    this.isEditing = false;
    this.isNew = false;
    this.source = null;
    this.model = null;
  }

  async save() {
    if (!this.isEditing) return;

    let res: GenericResponse<TipoViagemModel>;

    try {
      res = await this.travel.UpsertTravelType(this.model).toPromise();
    }
    catch(ex) {
      this.snack.open("Ocorreu um erro ao salvar os dados. Tente novamente.", "OK", { duration: 2500 } as MatSnackBarConfig);
      return;
    }

    if(!res.success) {
      this.snack.open("Ocorreu um erro ao salvar os dados. Tente novamente.", "OK", { duration: 2500 } as MatSnackBarConfig);
      return;
    }

    this.model.idTipoViagem = res.data.idTipoViagem;

    if(this.isNew) {
      this.types.push(this.model);
    }
    else {
      Object.assign(this.source, this.model);
    }

    this.isEditing = false;
    this.isNew = false;
    this.source = null;
    this.model = null;

    this.snack.open("Dados salvos com sucesso.", "OK", { duration: 2500 } as MatSnackBarConfig);
  }
}
