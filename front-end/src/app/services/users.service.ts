import { Injectable } from "@angular/core";
import { BaseUrlService } from "./base-url.service";
import { HttpClient } from "@angular/common/http";
import { GenericResponse } from "app/models/GenericResponse";
import { Observable } from "rxjs";
import { UsuarioDto } from "app/models/authentication/UserDto.model";
import { UserListModel } from "app/models/authentication/UserList.model";

const USER_VAR: string = "User";

@Injectable({
  providedIn: "root",
})
export class UsersService {
  constructor(private http: HttpClient, private url: BaseUrlService) {}

  public GetAll(): Observable<GenericResponse<UsuarioDto[]>> {
    let url = this.url.Users.Get;

    return this.http.get<GenericResponse<UsuarioDto[]>>(url);
  }

  public GetById(idUsuario: number): Observable<GenericResponse<UsuarioDto>> {
    let url = this.url.Users.Get + "/"+ idUsuario;

    return this.http.get<GenericResponse<UsuarioDto>>(url);
  }

  public SearchDirectory(userName: string): Observable<GenericResponse<UsuarioDto>> {
    let url = this.url.Users.Get + "/directory?userName="+ userName;

    return this.http.get<GenericResponse<UsuarioDto>>(url);
  }

  public Upsert(user: UsuarioDto): Observable<GenericResponse<UsuarioDto>> {
    let url = this.url.Users.Get;

    return this.http.post<GenericResponse<UsuarioDto>>(url, user);
  }

  public GetList(roles: string = ""): Observable<GenericResponse<UserListModel[]>> {
    let url = this.url.Users.Get + "/list";

    if(roles)
      url += `?roles=${roles}`;

    return this.http.get<GenericResponse<UserListModel[]>>(url);
  }

}
