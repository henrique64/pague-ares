import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { PaymentsComponent } from './payments.component';
import { AuthService } from 'app/services/auth.service';
import { PaymentService } from 'app/services/payment.service';
import { UsersService } from 'app/services/users.service';
import { ExportService } from 'app/services/export.service';
import { PreviewTokenService } from 'app/services/preview-token.service';
import { EnumPaymentListViewMode } from 'app/models/payment/enum-payment-list-view';

describe('PaymentsComponent', () => {
  let component: PaymentsComponent;
  let fixture: ComponentFixture<PaymentsComponent>;

  beforeEach(async () => {
    const authSpy = jasmine.createSpyObj('AuthService', ['IsInRole'], { CurrentUser: { idUsuario: 6 } });
    authSpy.IsInRole.and.returnValue(false);
    const paymentSpy = jasmine.createSpyObj('PaymentService', ['GetFilteredList']);
    const usersSpy = jasmine.createSpyObj('UsersService', ['GetList']);
    usersSpy.GetList.and.returnValue(of({ success: true, message: '', data: [], records: 0, pages: 0, page: 1 }));

    await TestBed.configureTestingModule({
      declarations: [PaymentsComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        { provide: AuthService, useValue: authSpy },
        { provide: PaymentService, useValue: paymentSpy },
        { provide: UsersService, useValue: usersSpy },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate', 'navigateByUrl']) },
        { provide: ActivatedRoute, useValue: { params: of({}), queryParamMap: of({ get: () => null }) } },
        { provide: ExportService, useValue: {} },
        { provide: PreviewTokenService, useValue: {} },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj('MatSnackBar', ['open']) }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(PaymentsComponent);
    component = fixture.componentInstance;
  });

  it('solicitante só pode usar MyView', () => {
    component.isAdmin = false;
    component.isManager = false;
    component.isFinancial = false;
    component.isAccounting = false;

    expect(component.isViewModeAllowed(EnumPaymentListViewMode.MyView)).toBeTrue();
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.ManagerView)).toBeFalse();
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.FinancialView)).toBeFalse();
  });

  it('gestor pode usar ManagerView', () => {
    component.isManager = true;
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.ManagerView)).toBeTrue();
  });

  it('financeiro pode usar FinancialView', () => {
    component.isFinancial = true;
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.FinancialView)).toBeTrue();
  });

  it('contabilidade pode usar FinancialView', () => {
    component.isAccounting = true;
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.FinancialView)).toBeTrue();
  });

  it('admin pode usar todas as visões', () => {
    component.isAdmin = true;
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.MyView)).toBeTrue();
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.ManagerView)).toBeTrue();
    expect(component.isViewModeAllowed(EnumPaymentListViewMode.FinancialView)).toBeTrue();
  });
});
