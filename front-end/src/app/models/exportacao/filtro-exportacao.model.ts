import { EnumTipoTituloExportacao } from "./enum-tipo-titulo-exportacao";

// Espelho de FiltroExportacaoDto (back-end). Datas trafegam como string ISO "yyyy-MM-dd".
export interface FiltroExportacaoModel {
    tipoTitulo: EnumTipoTituloExportacao;
    codigo?: number | null;
    numeroDocumento?: string | null;
    dataSolicitacaoInicio?: string | null;
    dataSolicitacaoFim?: string | null;
    dataVencimentoInicio?: string | null;
    dataVencimentoFim?: string | null;
    dataAprovacaoFinanceiroInicio?: string | null;
    dataAprovacaoFinanceiroFim?: string | null;
    idSolicitante?: number | null;
    idAtribuido?: number | null;
    idDepartamento?: number | null;
    statusGestor?: number | null;
    statusFinanceiro?: number | null;
    statusContabil?: number | null;
    nomeParceiro?: string | null;
    documentoParceiro?: string | null;
    tipoSolicitacao?: number | null;
    tipoPagamento?: number | null;
    formaPagamento?: number | null;
    tipoRelatorio?: number | null;
    incluirAnexos: boolean;
}
