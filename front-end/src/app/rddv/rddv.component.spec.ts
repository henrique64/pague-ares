import { async, ComponentFixture, TestBed } from '@angular/core/testing';
import { RddvComponent } from './rddv.component';

describe('RddvComponent', () => {
  let component: RddvComponent;
  let fixture: ComponentFixture<RddvComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ RddvComponent ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(RddvComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
