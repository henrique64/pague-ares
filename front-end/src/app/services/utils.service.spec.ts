import { UtilsService } from './utils.service';

describe('UtilsService', () => {
  describe('validarCPF', () => {
    it('cpf válido retorna true', () => {
      expect(UtilsService.validarCPF('123.456.789-09')).toBeTrue();
    });

    it('cpf com dígito errado retorna false', () => {
      expect(UtilsService.validarCPF('123.456.789-00')).toBeFalse();
    });

    it('cpf vazio retorna false', () => {
      expect(UtilsService.validarCPF('')).toBeFalse();
    });

    it('cpf com todos os dígitos iguais retorna false', () => {
      expect(UtilsService.validarCPF('111.111.111-11')).toBeFalse();
    });
  });

  describe('validarCNPJ', () => {
    it('cnpj válido retorna true', () => {
      expect(UtilsService.validarCNPJ('11.222.333/0001-81')).toBeTrue();
    });

    it('cnpj inválido retorna false', () => {
      expect(UtilsService.validarCNPJ('11.222.333/0001-00')).toBeFalse();
    });

    it('cnpj vazio retorna false', () => {
      expect(UtilsService.validarCNPJ('')).toBeFalse();
    });

    it('cnpj com todos os dígitos iguais retorna false', () => {
      expect(UtilsService.validarCNPJ('11.111.111/1111-11')).toBeFalse();
    });
  });

  describe('validarDocumento', () => {
    it('delega para validarCPF quando comprimento <= 11', () => {
      expect(UtilsService.validarDocumento('12345678909')).toBeTrue();
    });

    it('delega para validarCNPJ quando comprimento === 14', () => {
      expect(UtilsService.validarDocumento('11222333000181')).toBeTrue();
    });

    it('retorna false para comprimento inválido', () => {
      expect(UtilsService.validarDocumento('1234567')).toBeFalse();
    });
  });
});
