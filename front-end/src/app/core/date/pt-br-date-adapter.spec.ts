import { TestBed } from "@angular/core/testing";
import { DateAdapter, MAT_DATE_FORMATS, MAT_DATE_LOCALE } from "@angular/material/core";
import { AdminLayoutModule } from "app/layouts/admin-layout/admin-layout.module";
import { PROVIDERS_DATA_PT_BR, PtBrDateAdapter } from "./pt-br-date-adapter";

describe("PtBrDateAdapter", () => {
  let adapter: DateAdapter<Date>;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: PROVIDERS_DATA_PT_BR });
    adapter = TestBed.inject(DateAdapter);
  });

  describe("parse (texto digitado, dd/MM/aaaa)", () => {
    it("01/11/2025 é 1º de novembro, não 11 de janeiro", () => {
      const d = adapter.parse("01/11/2025", null)!;

      expect(d.getFullYear()).toBe(2025);
      expect(d.getMonth()).toBe(10);
      expect(d.getDate()).toBe(1);
    });

    it("31/12/2025 é 31 de dezembro", () => {
      const d = adapter.parse("31/12/2025", null)!;

      expect(d.getFullYear()).toBe(2025);
      expect(d.getMonth()).toBe(11);
      expect(d.getDate()).toBe(31);
    });

    it("aceita dia e mês sem zero à esquerda (1/2/2025 é 1º de fevereiro)", () => {
      const d = adapter.parse("1/2/2025", null)!;

      expect(d.getFullYear()).toBe(2025);
      expect(d.getMonth()).toBe(1);
      expect(d.getDate()).toBe(1);
    });

    it("tolera espaços ao redor do texto", () => {
      const d = adapter.parse("  05/03/2025 ", null)!;

      expect(d.getMonth()).toBe(2);
      expect(d.getDate()).toBe(5);
    });

    it("a hora fica zerada (data do calendário em hora local)", () => {
      const d = adapter.parse("15/06/2025", null)!;

      expect(d.getHours()).toBe(0);
      expect(d.getMinutes()).toBe(0);
    });

    it("31/02/2025 devolve data inválida, para o validador do Material", () => {
      const d = adapter.parse("31/02/2025", null)!;

      expect(adapter.isDateInstance(d)).toBeTrue();
      expect(adapter.isValid(d)).toBeFalse();
    });

    it("mês 13 e dia 0 devolvem data inválida", () => {
      expect(adapter.isValid(adapter.parse("10/13/2025", null)!)).toBeFalse();
      expect(adapter.isValid(adapter.parse("00/10/2025", null)!)).toBeFalse();
    });

    it("texto que não é data devolve data inválida", () => {
      expect(adapter.isValid(adapter.parse("abc", null)!)).toBeFalse();
      expect(adapter.isValid(adapter.parse("01/11", null)!)).toBeFalse();
    });

    it("ano bissexto: 29/02/2024 vale, 29/02/2025 não", () => {
      expect(adapter.isValid(adapter.parse("29/02/2024", null)!)).toBeTrue();
      expect(adapter.isValid(adapter.parse("29/02/2025", null)!)).toBeFalse();
    });

    it("texto vazio ou só espaços devolve null", () => {
      expect(adapter.parse("", null)).toBeNull();
      expect(adapter.parse("   ", null)).toBeNull();
      expect(adapter.parse(null, null)).toBeNull();
    });
  });

  describe("format (exibição)", () => {
    it("mostra dd/MM/aaaa com zeros à esquerda", () => {
      const fmt = TestBed.inject(MAT_DATE_FORMATS);

      expect(adapter.format(new Date(2025, 10, 1), fmt.display.dateInput)).toBe("01/11/2025");
      expect(adapter.format(new Date(2025, 11, 31), fmt.display.dateInput)).toBe("31/12/2025");
    });

    it("o que é exibido volta igual quando digitado de novo", () => {
      const fmt = TestBed.inject(MAT_DATE_FORMATS);
      const original = new Date(2026, 1, 9);

      const texto = adapter.format(original, fmt.display.dateInput);

      expect(adapter.parse(texto, null)!.getTime()).toBe(original.getTime());
    });
  });

  describe("ligação nos módulos do app", () => {
    it("o AdminLayoutModule (onde ficam todos os datepickers) usa o adaptador pt-BR", () => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({ imports: [AdminLayoutModule] });

      expect(TestBed.inject(MAT_DATE_LOCALE)).toBe("pt-BR");
      const doModulo = TestBed.inject(DateAdapter);
      expect(doModulo instanceof PtBrDateAdapter).toBeTrue();
      expect(doModulo.parse("01/11/2025", null)!.getMonth()).toBe(10);
    });
  });
});
