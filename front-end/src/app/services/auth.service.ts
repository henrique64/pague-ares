import { Injectable } from "@angular/core";
import { BaseUrlService } from "./base-url.service";
import { HttpClient } from "@angular/common/http";
import { LocalStorageService } from "./local-storage.service";
import { UserLoginDto } from "app/models/authentication/UserLoginDto.model";
import { EnumFuncao } from "app/models/authentication/EnumFuncao.model";
import { UsuarioDto } from "app/models/authentication/UserDto.model";
import { TokenModel } from "app/models/authentication/TokenModel";
import { GenericResponse } from "app/models/GenericResponse";
import * as jwt_decode from 'jwt-decode'
import moment from 'moment';
import { FuncaoDto } from "app/models/authentication/FuncaoDto.model";

const AuthTokenCacheKey: string = "Token";

@Injectable({
  providedIn: "root",
})
export class AuthService {
  ApiAuthToken: TokenModel = null!;
  CurrentUser: UsuarioDto | null = null;

  constructor(
    private http: HttpClient,
    private url: BaseUrlService,
    private local: LocalStorageService
  ) {
    this.LoadData();
  }

  private LoadData(): void {
    let tokenData = this.local.get(AuthTokenCacheKey) as TokenModel;

    if(tokenData) {
      this.ApiAuthToken = tokenData;
      this.CurrentUser = this.decodeToken(this.ApiAuthToken.token);
    }
  }

  public async Authenticate(username: string, password: string): Promise<GenericResponse<TokenModel> | null> {
    let loginData = new UserLoginDto();

    loginData.username = username;
    loginData.password = password;

    let loginResponse: GenericResponse<TokenModel>;

    try {
      loginResponse = await this.http.post<GenericResponse<TokenModel>>(this.url.Authentication.Login, loginData).toPromise();
    }
    catch(ex) {
      return null;
    }

    this.ApiAuthToken = loginResponse.data;
    this.CurrentUser = this.decodeToken(this.ApiAuthToken.token);
    
    this.local.set(AuthTokenCacheKey, this.ApiAuthToken);

    return loginResponse;
  }

  public Logout() {
    this.CurrentUser = null;
    this.ApiAuthToken = null;
    this.local.remove(AuthTokenCacheKey);
  }

  public get IsAuthenticated(): boolean {
    return this.ApiAuthToken && 
          this.ApiAuthToken?.token && 
          moment(this.ApiAuthToken?.expires).isAfter(moment(new Date().toUTCString()));
  }

  public IsInRole(role: EnumFuncao): boolean {
    if(!this.CurrentUser)
      return false;

    let alias = "";

    if(role === EnumFuncao.Administrador)
      alias = "ADM";
    else if(role === EnumFuncao.Financeiro)
      alias = "FIN";
    else if(role === EnumFuncao.Gestor)
      alias = "GES";
    else if(role === EnumFuncao.Solicitante)
      alias = "SOL";
    else if(role === EnumFuncao.Contabilidade)
      alias = "CON";

    return !!this.CurrentUser.funcoes.find(f => f.alias.toLowerCase() === alias.toLowerCase());
  }

  getToken(): string {
    return this.ApiAuthToken?.token;
  }

  private decodeToken(token: string): UsuarioDto | null{
    let jwtData: any;

    try {
      jwtData = jwt_decode.jwtDecode(token);
    } catch (Error) {
      jwtData = null;
    }

    if (!jwtData) return null;

    return {
        idUsuario: jwtData.nameid,
        email: "",
        nome: jwtData.sub,
        origem: 0,
        idExterno: "",
        senha: "",
        idDepartamento: 0,
        ativo: true,
        dataCadastro: new Date(),
        usuarioCadastro: 0,
        ultimaAlteracao: "",
        usuarioAlteracao: 0,
        funcoes: (jwtData.Roles?.split(",") ?? []).map((r: string) => { return { alias: r, nome: r } as FuncaoDto }),
        codigo: ""
    } as UsuarioDto;
  }
}
