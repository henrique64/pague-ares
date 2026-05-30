import { Component, OnInit } from "@angular/core";
import { Router } from "@angular/router";
import { EnumFuncao } from "app/models/authentication/EnumFuncao.model";
import { AuthService } from "../../services/auth.service"

declare const $: any;
declare interface RouteInfo {
  path: string;
  title: string;
  icon: string;
  class: string;
}

export var ROUTES: RouteInfo[] = [];

export const RequesterRoutes: RouteInfo[] = [
  {
    path: "/payments",
    title: "Solicitações",
    icon: "source",
    class: "",
  },
];

export const ManagerRoutes: RouteInfo[] = [
  {
    path: "/payments",
    title: "Solicitações",
    icon: "source",
    class: "",
  }
];

export const FinanceRoutes: RouteInfo[] = [
  { path: "/home", title: "Home", icon: "dashboard", class: "" },
  {
    path: "/payments",
    title: "Solicitações",
    icon: "source",
    class: "",
  }
];

export const AdminRoutes: RouteInfo[] = [
  { path: "/home", title: "Home", icon: "dashboard", class: "" },
  {
    path: "/payments",
    title: "Solicitações",
    icon: "source",
    class: "",
  },
  { path: "/config", title: "Configurações", icon: "settings", class: "" },
];

@Component({
    selector: "app-sidebar",
    templateUrl: "./sidebar.component.html",
    styleUrls: ["./sidebar.component.css"],
    standalone: false
})
export class SidebarComponent implements OnInit {
  menuItems: any[];
  constructor(private router: Router, private authService: AuthService) {}

  ngOnInit() {
    this.selectingRoute();
    this.menuItems = ROUTES.filter((menuItem) => menuItem);
  }

  selectingRoute() {
    if (this.authService.IsInRole(EnumFuncao.Administrador)) {
      ROUTES = AdminRoutes;
    }
    else if (this.authService.IsInRole(EnumFuncao.Financeiro) || this.authService.IsInRole(EnumFuncao.Contabilidade)) {
      ROUTES = FinanceRoutes;
    }
    else if (this.authService.IsInRole(EnumFuncao.Gestor)) {
      ROUTES = ManagerRoutes;
    }
    else if (this.authService.IsInRole(EnumFuncao.Solicitante)) {
      ROUTES = RequesterRoutes;
    }
  }

  isMobileMenu() {
    if ($(window).width() > 991) return false;
    return true;
  }

  doLogout() {
    this.authService.Logout();
    this.router.navigateByUrl("/login");
  }

  get UserName(): string {
    return this.authService.CurrentUser?.nome;
  }
}
