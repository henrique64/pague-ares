import { Injectable, Provider } from "@angular/core";
import { DateAdapter, MAT_DATE_FORMATS, MAT_DATE_LOCALE, MatDateFormats, NativeDateAdapter } from "@angular/material/core";

// Nome do formato de exibição e de leitura do texto dos campos de data. É resolvido em PtBrDateAdapter.format,
// que monta o dd/MM/aaaa sem depender do Intl do navegador.
const FORMATO_ENTRADA = "dd/MM/yyyy";

export const PT_BR_DATE_FORMATS: MatDateFormats = {
  parse: { dateInput: FORMATO_ENTRADA },
  display: {
    dateInput: FORMATO_ENTRADA,
    monthYearLabel: { year: "numeric", month: "short" },
    dateA11yLabel: { year: "numeric", month: "long", day: "numeric" },
    monthYearA11yLabel: { year: "numeric", month: "long" },
  },
};

// Adaptador de datas do Angular Material para o português do Brasil.
//
// O NativeDateAdapter lê o texto digitado com Date.parse, que entende "01/11/2025" como mês/dia (11 de janeiro).
// Este adaptador lê o texto como dia/mês/ano, aceita dia e mês com um ou dois dígitos ("1/2/2025") e devolve uma
// data inválida (e não uma data errada) para o que não existe, como 31/02/2025: o validador do Material
// (matDatepickerParse) marca o campo com erro e o valor não chega ao modelo.
@Injectable()
export class PtBrDateAdapter extends NativeDateAdapter {
  override parse(value: any, parseFormat?: any): Date | null {
    if (typeof value !== "string") return super.parse(value, parseFormat);

    const texto = value.trim();
    if (texto.length === 0) return null;

    const partes = /^(\d{1,2})[\/.\-](\d{1,2})[\/.\-](\d{4})$/.exec(texto);
    if (!partes) return this.invalid();

    const dia = Number(partes[1]);
    const mes = Number(partes[2]);
    const ano = Number(partes[3]);

    // Confere dia/mês/ano contra o calendário: new Date(2025, 1, 31) viraria 3 de março em vez de falhar.
    const data = this.createDate(ano, 0, 1);
    data.setMonth(mes - 1, dia);
    if (data.getFullYear() !== ano || data.getMonth() !== mes - 1 || data.getDate() !== dia) return this.invalid();

    return data;
  }

  override format(date: Date, displayFormat: any): string {
    if (displayFormat !== FORMATO_ENTRADA) return super.format(date, displayFormat);

    if (!this.isValid(date)) {
      throw Error("PtBrDateAdapter: não é possível formatar uma data inválida.");
    }

    const dd = String(date.getDate()).padStart(2, "0");
    const mm = String(date.getMonth() + 1).padStart(2, "0");
    return `${dd}/${mm}/${String(date.getFullYear()).padStart(4, "0")}`;
  }
}

// Todos os datepickers do app ficam no AdminLayoutModule, que registra estes provedores.
export const PROVIDERS_DATA_PT_BR: Provider[] = [
  { provide: MAT_DATE_LOCALE, useValue: "pt-BR" },
  { provide: DateAdapter, useClass: PtBrDateAdapter },
  { provide: MAT_DATE_FORMATS, useValue: PT_BR_DATE_FORMATS },
];
