import { ConfiguracaoModel } from "./configuracao.model";

export class RddvConfigModel {
    public ValorKm: number;
    public DiariaNacional: number;
    public DiariaInternacional: number;
    public DiariaInternacionalEuro: number;
    public DiariaInternacionalIsrael: number;

    public static FromConfiguracaoModel(config: ConfiguracaoModel[]): RddvConfigModel {
        let rddvConfig = new RddvConfigModel();

        let valorKm = config.find(c => c.chave === "Rddv.ValorKm");
        let diaNacional = config.find(c => c.chave === "Rddv.DiariaNacional");
        let diaInter = config.find(c => c.chave === "Rddv.DiariaInternacional");
        let diaInterEuro = config.find(c => c.chave === "Rddv.DiariaInternacionalEuro");
        let diaInterIsrael = config.find(c => c.chave === "Rddv.DiariaInternacionalIsrael");

        if(valorKm) {
            rddvConfig.ValorKm = parseFloat(valorKm.valor.replace(",", "."));
        }
        else {
            rddvConfig.ValorKm = 0.00;
        }

        if(diaNacional) {
            rddvConfig.DiariaNacional = parseFloat(diaNacional.valor.replace(",", "."));
        }
        else {
            rddvConfig.DiariaNacional = 0.00;
        }

        if(diaInter) {
            rddvConfig.DiariaInternacional = parseFloat(diaInter.valor.replace(",", "."));
        }
        else {
            rddvConfig.DiariaInternacional = 0.00;
        }

        if(diaInterEuro) {
            rddvConfig.DiariaInternacionalEuro = parseFloat(diaInterEuro.valor.replace(",", "."));
        }
        else {
            rddvConfig.DiariaInternacionalEuro = 0.00;
        }

        if(diaInterIsrael) {
            rddvConfig.DiariaInternacionalIsrael = parseFloat(diaInterIsrael.valor.replace(",", "."));
        }
        else {
            rddvConfig.DiariaInternacionalIsrael = 0.00;
        }

        return rddvConfig;
    }

    public static ToConfiguracaoModel(rddvConfig: RddvConfigModel): ConfiguracaoModel[] {
        let configs = [];

        configs.push({chave: "Rddv.ValorKm", valor: rddvConfig.ValorKm.toString()} as ConfiguracaoModel);
        configs.push({chave: "Rddv.DiariaNacional", valor: rddvConfig.DiariaNacional.toString()} as ConfiguracaoModel);
        configs.push({chave: "Rddv.DiariaInternacional", valor: rddvConfig.DiariaInternacional.toString()} as ConfiguracaoModel);
        configs.push({chave: "Rddv.DiariaInternacionalEuro", valor: rddvConfig.DiariaInternacionalEuro.toString()} as ConfiguracaoModel);
        configs.push({chave: "Rddv.DiariaInternacionalIsrael", valor: rddvConfig.DiariaInternacionalIsrael.toString()} as ConfiguracaoModel);

        return configs;
    }
}