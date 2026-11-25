import { Provider } from "@angular/core";
import { DateAdapter, MAT_DATE_FORMATS, MAT_DATE_LOCALE, MAT_NATIVE_DATE_FORMATS, NativeDateAdapter } from "@angular/material/core";

// Esqueleto da etapa de testes: ainda herda o comportamento do NativeDateAdapter (Date.parse, que lê
// "01/11/2025" como mês/dia). A correção vem no commit seguinte.
export class PtBrDateAdapter extends NativeDateAdapter {}

export const PROVIDERS_DATA_PT_BR: Provider[] = [
  { provide: MAT_DATE_LOCALE, useValue: "pt-BR" },
  { provide: DateAdapter, useClass: PtBrDateAdapter, deps: [MAT_DATE_LOCALE] },
  { provide: MAT_DATE_FORMATS, useValue: MAT_NATIVE_DATE_FORMATS },
];
