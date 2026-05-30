export interface PaymentListModel {
    codigo: number;
    idTipo: number;
    tipo: string;
    dataSolicitacao: Date;
    dataVencimento: Date | null;
    numeroDocumento: string | null;
    nomeSolicitante: string;
    idSolicitante: number;
    usuarioAtribuido: string | null;
    idUsuarioAtribuido: number | null;
    nomeParceiro: string;
    valor: number | null;
    descricao: string;
    observacaoGestor: string;
    idStatusGestor: number | null;
    idStatusFinanceiro: number | null;
    idStatusContabilidade: number | null;
    statusGestor: string;
    statusFinanceiro: string;
    statusContabilidade: string;
    idGestor: number | null;
    aprovadoSetor: boolean | null;
    aprovadoGestor: boolean | null;
    rascunho: boolean | null;
    cancelado: boolean | null;
}