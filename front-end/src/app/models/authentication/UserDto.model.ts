import { FuncaoDto } from "./FuncaoDto.model";

export class UsuarioDto {
    idUsuario: number;
    email: string;
    nome: string;
    origem: number;
    idExterno: string | null;
    senha: string;
    idDepartamento: number;
    ativo: boolean | null;
    dataCadastro: Date;
    usuarioCadastro: number | null;
    ultimaAlteracao: string | null;
    usuarioAlteracao: number | null;
    funcoes: FuncaoDto[];
    codigo: string | null;
}