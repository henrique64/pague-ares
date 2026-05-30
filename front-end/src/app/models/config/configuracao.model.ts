export class ConfiguracaoModel {
    idConfiguracao: number;
    chave: string;
    valor: string;
    dataAlteracao: Date | null;
    usuarioAlteracao: number | null;
}