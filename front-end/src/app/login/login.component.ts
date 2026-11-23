import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { User } from './user';

@Component({
    selector: 'app-login',
    templateUrl: './login.component.html',
    styleUrls: ['./login.component.css'],
    standalone: false
})
export class LoginComponent implements OnInit {

  user: User = new User();
  constructor(private authService: AuthService, private router: Router) { }

  ocupado: boolean = false;
  erro: boolean = false;
  saveLogin: boolean = false;
  mensagemErro: string = "";

  ngOnInit() {
    if(this.authService.IsAuthenticated) {
      this.router.navigate(['/home']);
      return;
    }
  }

  displayError(message: string) {
    this.erro = true;
    this.mensagemErro = message;
  }

  clearError() {
    this.erro = false;
    this.mensagemErro = "";
  }

  validateForm(): boolean {
    if(this.user.name == null || this.user.name.length == 0) {
      this.displayError("O campo usuário é obrigatório.");
      return false;
    }
    if(this.user.password == null || this.user.password.length == 0) {
      this.displayError("O campo senha é obrigatório.");
      return false;
    }
    return true;
  }

  async ExecuteLogin(): Promise<void> {
    if(this.ocupado || !this.validateForm()) return;

    this.ocupado = true;
    this.clearError();

    try {
      let result = await this.authService.Authenticate(this.user.name, this.user.password);

      if(!result) {
        this.displayError("Erro de conexão. Verifique sua rede e tente novamente.");
        return;
      }

      if(result.success) {
        this.router.navigate(['/payments']);
      }
      else {
        this.displayError(result.message || "Usuário ou senha inválidos.");
      }
    }
    catch {
      this.displayError("Erro de conexão. Verifique sua rede e tente novamente.");
    }
    finally {
      this.ocupado = false;
    }
  }
}