import { inject } from "@angular/core";
import { CanActivateFn, Router } from "@angular/router";
import { EnumFuncao } from "app/models/authentication/EnumFuncao.model";
import { AuthService } from "app/services/auth.service";

// Libera a rota quando o usuário tem ao menos um dos papéis informados;
// caso contrário redireciona para /payments.
export function papelGuard(...funcoes: EnumFuncao[]): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (funcoes.some((funcao) => auth.IsInRole(funcao))) {
      return true;
    }

    return router.createUrlTree(["/payments"]);
  };
}
