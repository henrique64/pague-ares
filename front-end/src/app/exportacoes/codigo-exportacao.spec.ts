import { EnumTipoTituloExportacao } from 'app/models/exportacao/enum-tipo-titulo-exportacao';
import { formatarCodigoExportacao, interpretarCodigoExportacao } from './codigo-exportacao';

describe('interpretarCodigoExportacao', () => {
  const Sol = EnumTipoTituloExportacao.Solicitacao;
  const Rddv = EnumTipoTituloExportacao.Rddv;

  const aceitos: [string, number, EnumTipoTituloExportacao | null][] = [
    ['123', 123, null],
    ['000123', 123, null],
    ['  45  ', 45, null],
    ['SOL-000123', 123, Sol],
    ['sol-000123', 123, Sol],
    ['SOL123', 123, Sol],
    ['Sol-7', 7, Sol],
    ['RDDV-45', 45, Rddv],
    ['rddv45', 45, Rddv],
    ['RDDV-000045', 45, Rddv],
    ['2147483647', 2147483647, null],
  ];

  for (const [texto, numero, tipo] of aceitos) {
    it(`aceita "${texto}" como número ${numero} e tipo ${tipo === null ? 'sem prefixo' : EnumTipoTituloExportacao[tipo]}`, () => {
      expect(interpretarCodigoExportacao(texto)).toEqual({ situacao: 'ok', numero, tipo });
    });
  }

  const recusados = ['0', '000', '1,5', '1.5', '-3', 'ABC-1', 'SOL-', 'RDDV', 'SOL-0', 'SOL--1', 'SOL 1', '12a', '2147483648', '99999999999999999999'];

  for (const texto of recusados) {
    it(`recusa "${texto}"`, () => {
      expect(interpretarCodigoExportacao(texto)).toEqual({ situacao: 'invalido' });
    });
  }

  it('vazio, só espaços, null e undefined são "vazio" (sem filtro)', () => {
    for (const v of ['', '   ', null, undefined]) {
      expect(interpretarCodigoExportacao(v)).toEqual({ situacao: 'vazio' });
    }
  });

  it('aceita número puro (o modelo antigo do formulário era numérico)', () => {
    expect(interpretarCodigoExportacao(42)).toEqual({ situacao: 'ok', numero: 42, tipo: null });
  });

  it('formata como nas telas: seis dígitos com o prefixo do tipo', () => {
    expect(formatarCodigoExportacao(Sol, 123)).toBe('SOL-000123');
    expect(formatarCodigoExportacao(Rddv, 45)).toBe('RDDV-000045');
    expect(formatarCodigoExportacao(Sol, 1234567)).toBe('SOL-1234567');
  });
});
