import { EnumStatusExportacao } from "./enum-status-exportacao";
import { FiltroExportacaoModel } from "./filtro-exportacao.model";

// Espelho de ExportacaoJobDto (back-end). Datas em UTC, como string ISO.
export interface ExportacaoJobModel {
    id: number;
    status: EnumStatusExportacao;
    statusDescricao: string;
    dataCriacao: string;
    dataInicio?: string | null;
    dataConclusao?: string | null;
    dataExpiracao?: string | null;
    totalTitulos: number;
    titulosProcessados: number;
    percentual: number;
    totalAnexos: number;
    tamanhoBytes?: number | null;
    mensagemErro?: string | null;
    filtros: FiltroExportacaoModel;
}

// Espelho de LinkDownloadExportacaoDto (back-end).
export interface LinkDownloadExportacaoModel {
    token: string;
    expiraEm: string;
}
