import { Router } from '@angular/router';
import { SidebarComponent } from './sidebar.component';
import { AuthService } from '../../services/auth.service';
import { EnumFuncao } from 'app/models/authentication/EnumFuncao.model';

describe('SidebarComponent', () => {
  function menuPara(...papeis: EnumFuncao[]): string[] {
    const auth = jasmine.createSpyObj('AuthService', ['IsInRole']);
    auth.IsInRole.and.callFake((f: EnumFuncao) => papeis.includes(f));
    const sidebar = new SidebarComponent({} as Router, auth as AuthService);
    sidebar.ngOnInit();
    return sidebar.menuItems.map(i => i.path);
  }

  it('Financeiro vê Exportações', () => {
    expect(menuPara(EnumFuncao.Financeiro)).toContain('/exportacoes');
  });

  it('Administrador vê Exportações', () => {
    expect(menuPara(EnumFuncao.Administrador)).toContain('/exportacoes');
  });

  it('Contabilidade não vê Exportações, mas mantém Home e Solicitações', () => {
    const menu = menuPara(EnumFuncao.Contabilidade);
    expect(menu).not.toContain('/exportacoes');
    expect(menu).toEqual(['/home', '/payments']);
  });

  it('Solicitante e Gestor não veem Exportações', () => {
    expect(menuPara(EnumFuncao.Solicitante)).not.toContain('/exportacoes');
    expect(menuPara(EnumFuncao.Gestor)).not.toContain('/exportacoes');
  });
});
