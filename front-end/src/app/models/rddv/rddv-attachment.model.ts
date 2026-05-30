export class RddvAttachmentModel {
    idDocumentoRddv: number;
    idRelatorio: number;
    idTipoDocumento: number;
    idUsuario: number;
    dataCriacao: Date;
    indice: number;
    nomeArquivo: string;
    caminhoArquivo: string;
    arquivo: string;
    excluido: boolean;
}