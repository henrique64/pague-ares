import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ChangeDetectorRef } from '@angular/core';
import { of } from 'rxjs';
import { HomeComponent } from './home.component';
import { AuthService } from 'app/services/auth.service';
import { PaymentService } from 'app/services/payment.service';
import { RddvService } from 'app/services/rddv.service';
import { UsersService } from 'app/services/users.service';
import { EnumFuncao } from 'app/models/authentication/EnumFuncao.model';

describe('HomeComponent', () => {
  let component: HomeComponent;
  let fixture: ComponentFixture<HomeComponent>;
  let authSpy: jasmine.SpyObj<AuthService>;
  let routerSpy: jasmine.SpyObj<Router>;
  let paymentSpy: jasmine.SpyObj<PaymentService>;

  beforeEach(async () => {
    authSpy = jasmine.createSpyObj('AuthService', ['IsInRole']);
    authSpy.IsInRole.and.returnValue(false);
    routerSpy = jasmine.createSpyObj('Router', ['navigateByUrl', 'navigate']);
    paymentSpy = jasmine.createSpyObj('PaymentService', ['GetDashboard', 'GetFilteredList']);
    paymentSpy.GetDashboard.and.returnValue(of({ success: true, message: '', data: { pendentes: 0, aprovados: 0, reprovados: 0 }, records: 0, pages: 0, page: 1 }));
    paymentSpy.GetFilteredList.and.returnValue(of({ success: true, message: '', data: [], records: 0, pages: 0, page: 1 }));
    const rddvSpy = jasmine.createSpyObj('RddvService', ['SetUser']);
    const usersSpy = jasmine.createSpyObj('UsersService', ['GetList']);
    usersSpy.GetList.and.returnValue(of({ success: true, message: '', data: [], records: 0, pages: 0, page: 1 }));

    await TestBed.configureTestingModule({
      declarations: [HomeComponent],
      providers: [
        { provide: AuthService, useValue: authSpy },
        { provide: Router, useValue: routerSpy },
        { provide: PaymentService, useValue: paymentSpy },
        { provide: RddvService, useValue: rddvSpy },
        { provide: UsersService, useValue: usersSpy },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj('MatSnackBar', ['open']) },
        { provide: ChangeDetectorRef, useValue: jasmine.createSpyObj('ChangeDetectorRef', ['detectChanges']) }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(HomeComponent);
    component = fixture.componentInstance;
  });

  it('ngOnDestroy limpa intervalHandle', () => {
    component.intervalHandle = setInterval(() => {}, 99999);
    spyOn(window, 'clearInterval').and.callThrough();
    component.ngOnDestroy();
    expect(clearInterval).toHaveBeenCalledWith(component.intervalHandle);
  });

  it('ngOnDestroy limpa timerHandle', () => {
    component.timerHandle = setInterval(() => {}, 99999);
    spyOn(window, 'clearInterval').and.callThrough();
    component.ngOnDestroy();
    expect(clearInterval).toHaveBeenCalledWith(component.timerHandle);
  });

  it('ngOnInit redireciona para /payments quando usuário não tem role ADM/FIN/CON', () => {
    authSpy.IsInRole.and.returnValue(false);
    component.ngOnInit();
    expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/payments');
  });

  it('ngOnInit não redireciona quando usuário tem role ADM', () => {
    authSpy.IsInRole.and.callFake((role: EnumFuncao) => role === EnumFuncao.Administrador);
    component.ngOnInit();
    expect(routerSpy.navigateByUrl).not.toHaveBeenCalledWith('/payments');
  });
});
