#:property PublishAot=false
#:package Microsoft.Data.SqlClient@7.1.2
#:package BCrypt.Net-Next@4.0.3

// Semente de dados fictícios para a verificação da exportação de títulos com anexos.
//
// Uso:   dotnet run tools/exportacao/semente/semente.cs -- [--titulos 500] [--mb-por-titulo 5] [--semente 42]
//        dotnet run tools/exportacao/semente/semente.cs -- --limpar
//        dotnet run tools/exportacao/semente/semente.cs -- --ajuda
//
// Variáveis de ambiente (os valores nunca são impressos nem gravados):
//   PAGUEARES_SEMENTE_CONEXAO  string de conexão do SQL Server LOCAL (localhost ou 127.0.0.1, porta 14330)
//   PAGUEARES_SEMENTE_SENHA    senha dos usuários fictícios (só para semear; não é usada em --limpar)
//
// Códigos de saída: 0 ok; 1 erro durante a execução; 2 uso incorreto ou servidor recusado (nada foi conectado);
//                   3 já existem dados da semente (rode --limpar antes).

using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using static Fixos;

const string Marca = "[SEMENTE]";
const string DominioEmail = "@exemplo.invalid";
const int PortaPermitida = 14330;

int titulos = 500;
double mbPorTitulo = 5;
int semente = 42;
bool limpar = false;

try
{
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--ajuda":
            case "-h":
            case "--help":
                MostrarAjuda();
                return 0;
            case "--limpar":
                limpar = true;
                break;
            case "--titulos":
                titulos = int.Parse(ValorDe(args, ref i), CultureInfo.InvariantCulture);
                break;
            case "--mb-por-titulo":
                mbPorTitulo = double.Parse(ValorDe(args, ref i), CultureInfo.InvariantCulture);
                break;
            case "--semente":
                semente = int.Parse(ValorDe(args, ref i), CultureInfo.InvariantCulture);
                break;
            default:
                Console.Error.WriteLine($"Argumento desconhecido: {args[i]}. Use --ajuda.");
                return 2;
        }
    }
}
catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
{
    Console.Error.WriteLine("Valor inválido em um dos argumentos numéricos. Use --ajuda.");
    return 2;
}

if (titulos < 1 || titulos > 100000 || mbPorTitulo < 0 || mbPorTitulo > 200)
{
    Console.Error.WriteLine("Faixa inválida: --titulos 1..100000 e --mb-por-titulo 0..200.");
    return 2;
}

// ---- Recusa de servidor não local: acontece ANTES de qualquer conexão ----------------------------------
var conexaoBruta = Environment.GetEnvironmentVariable("PAGUEARES_SEMENTE_CONEXAO");
if (string.IsNullOrWhiteSpace(conexaoBruta))
{
    Console.Error.WriteLine("Defina a variável PAGUEARES_SEMENTE_CONEXAO com a conexão do SQL Server local (localhost,14330).");
    return 2;
}

SqlConnectionStringBuilder csb;
try
{
    csb = new SqlConnectionStringBuilder(conexaoBruta);
}
catch (Exception)
{
    Console.Error.WriteLine("PAGUEARES_SEMENTE_CONEXAO não é uma string de conexão válida.");
    return 2;
}

var recusa = ValidarServidorLocal(csb);
if (recusa != null)
{
    Console.Error.WriteLine($"Servidor recusado: {recusa} Nenhuma conexão foi aberta.");
    return 2;
}

if (string.IsNullOrWhiteSpace(csb.InitialCatalog))
{
    Console.Error.WriteLine("Informe o banco (Initial Catalog) na string de conexão.");
    return 2;
}

string? senhaFicticia = null;
if (!limpar)
{
    senhaFicticia = Environment.GetEnvironmentVariable("PAGUEARES_SEMENTE_SENHA");
    if (string.IsNullOrEmpty(senhaFicticia))
    {
        Console.Error.WriteLine("Defina a variável PAGUEARES_SEMENTE_SENHA com a senha dos usuários fictícios.");
        return 2;
    }
}

// O SQL Server local do Docker usa certificado autoassinado; só chegamos aqui com o servidor local validado.
if (!conexaoBruta.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase))
    csb.TrustServerCertificate = true;
csb.ApplicationName = "PagueAres.Semente";

try
{
    using var conn = new SqlConnection(csb.ConnectionString);
    await conn.OpenAsync();

    if (limpar)
    {
        await LimparAsync(conn);
        return 0;
    }

    if (await ExisteSementeAsync(conn))
    {
        Console.Error.WriteLine("Já existem dados da semente neste banco. Rode com --limpar antes de semear de novo.");
        return 3;
    }

    await SemearAsync(conn, titulos, mbPorTitulo, semente, senhaFicticia!);
    return 0;
}
catch (SqlException ex)
{
    Console.Error.WriteLine($"Falha no SQL Server (erro {ex.Number}): {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Falha: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

// =====================================================================================================
// Funções locais
// =====================================================================================================

static void MostrarAjuda()
{
    Console.WriteLine("""
        semente.cs - dados fictícios para verificar a exportação de títulos com anexos.

        Uso:
          dotnet run tools/exportacao/semente/semente.cs -- [opções]

        Opções:
          --titulos <n>         títulos exportáveis (solicitações ~60% e RDDVs ~40%); padrão 500.
                                Além deles entram ~5% de rascunhos e cancelados, que devem ficar de fora.
          --mb-por-titulo <n>   MB de anexos por título (1 a 3 anexos); padrão 5.
          --semente <n>         semente do gerador; mesma semente, mesmos dados; padrão 42.
          --limpar              remove tudo o que a semente criou (marca [SEMENTE] e e-mails @exemplo.invalid) e sai.
          --ajuda               mostra este texto.

        Variáveis de ambiente:
          PAGUEARES_SEMENTE_CONEXAO  conexão do SQL Server local. Só localhost ou 127.0.0.1 na porta 14330.
          PAGUEARES_SEMENTE_SENHA    senha dos usuários fictícios (obrigatória, exceto em --limpar).

        Códigos de saída: 0 ok; 1 erro; 2 uso incorreto ou servidor recusado; 3 já existe semente (use --limpar).
        """);
}

static string ValorDe(string[] args, ref int i)
{
    if (i + 1 >= args.Length) throw new ArgumentException("Falta o valor de " + args[i]);
    i++;
    return args[i];
}

// Devolve o motivo da recusa, ou null se o servidor é o SQL local permitido (localhost/127.0.0.1, porta 14330).
static string? ValidarServidorLocal(SqlConnectionStringBuilder csb)
{
    if (!string.IsNullOrWhiteSpace(csb.FailoverPartner))
        return "a conexão declara um servidor de failover.";
    if (!string.IsNullOrWhiteSpace(csb.AttachDBFilename))
        return "a conexão declara um arquivo de banco anexado.";

    var origem = csb.DataSource.Trim();
    if (origem.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        origem = origem[4..];

    if (origem.Contains('\\'))
        return "instâncias nomeadas não são permitidas; use localhost,14330.";

    var partes = origem.Split(',');
    if (partes.Length != 2)
        return "o servidor precisa ser informado como localhost,14330 ou 127.0.0.1,14330.";

    var host = partes[0].Trim();
    if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && host != "127.0.0.1")
        return "só localhost ou 127.0.0.1 são permitidos.";

    if (!int.TryParse(partes[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var porta) || porta != PortaPermitida)
        return $"só a porta {PortaPermitida} é permitida.";

    return null;
}

static async Task<bool> ExisteSementeAsync(SqlConnection conn)
{
    const string sql = @"
        SELECT CASE WHEN
            EXISTS (SELECT 1 FROM Solicitacao WHERE Descricao LIKE '[[]SEMENTE]%')
         OR EXISTS (SELECT 1 FROM Rddv WHERE Finalidade LIKE '[[]SEMENTE]%')
         OR EXISTS (SELECT 1 FROM Usuario WHERE Email LIKE '%@exemplo.invalid')
        THEN 1 ELSE 0 END";
    using var cmd = new SqlCommand(sql, conn);
    return (int)(await cmd.ExecuteScalarAsync())! == 1;
}

static async Task<int> ExecutarEmLotesAsync(SqlConnection conn, string sqlComTop)
{
    // sqlComTop precisa apagar no máximo @n linhas por execução; repete até não restar nada.
    int total = 0;
    while (true)
    {
        using var cmd = new SqlCommand(sqlComTop, conn) { CommandTimeout = 600 };
        int apagadas = await cmd.ExecuteNonQueryAsync(); // -1 quando o comando não afeta linhas (tabela inexistente)
        if (apagadas <= 0) return total;
        total += apagadas;
    }
}

static async Task LimparAsync(SqlConnection conn)
{
    Console.WriteLine("Removendo dados da semente...");
    const string idsSol = "SELECT IdSolicitacao FROM Solicitacao WHERE Descricao LIKE '[[]SEMENTE]%'";
    const string idsRddv = "SELECT IdRelatorio FROM Rddv WHERE Finalidade LIKE '[[]SEMENTE]%'";

    var n = await ExecutarEmLotesAsync(conn, $"DELETE TOP (200) FROM DocumentoSolicitacao WHERE IdSolicitacao IN ({idsSol})");
    Console.WriteLine($"  anexos de solicitações: {n}");
    n = await ExecutarEmLotesAsync(conn, $"DELETE TOP (200) FROM DocumentoRddv WHERE IdRelatorio IN ({idsRddv})");
    Console.WriteLine($"  anexos de RDDVs: {n}");
    n = await ExecutarEmLotesAsync(conn, $"DELETE TOP (2000) FROM DespesaRddv WHERE IdRelatorio IN ({idsRddv})");
    Console.WriteLine($"  despesas: {n}");
    n = await ExecutarEmLotesAsync(conn, "DELETE TOP (2000) FROM Solicitacao WHERE Descricao LIKE '[[]SEMENTE]%'");
    Console.WriteLine($"  solicitações: {n}");
    n = await ExecutarEmLotesAsync(conn, "DELETE TOP (2000) FROM Rddv WHERE Finalidade LIKE '[[]SEMENTE]%'");
    Console.WriteLine($"  RDDVs: {n}");

    // Jobs de exportação dos usuários fictícios (a tabela só existe depois da migration AdicionaExportacaoJob).
    n = await ExecutarEmLotesAsync(conn, @"
        IF OBJECT_ID(N'dbo.ExportacaoJob', N'U') IS NOT NULL
            DELETE TOP (2000) FROM dbo.ExportacaoJob
            WHERE IdUsuario IN (SELECT IdUsuario FROM Usuario WHERE Email LIKE '%@exemplo.invalid')
        ELSE SELECT 0 WHERE 1 = 0");
    Console.WriteLine($"  jobs de exportação dos usuários fictícios: {n}");

    n = await ExecutarEmLotesAsync(conn, @"DELETE TOP (2000) FROM UsuarioFuncao
        WHERE IdUsuario IN (SELECT IdUsuario FROM Usuario WHERE Email LIKE '%@exemplo.invalid')");
    Console.WriteLine($"  papéis dos usuários fictícios: {n}");
    n = await ExecutarEmLotesAsync(conn, "DELETE TOP (2000) FROM Usuario WHERE Email LIKE '%@exemplo.invalid'");
    Console.WriteLine($"  usuários fictícios: {n}");
    Console.WriteLine("Limpeza concluída.");
}

static async Task SemearAsync(SqlConnection conn, int titulos, double mbPorTitulo, int semente, string senha)
{
    var rng = new Random(semente);
    var rngConteudo = new Random(unchecked(semente * 7919 + 17));

    // ---- Referências existentes (seed da migration) --------------------------------------------------
    var departamentos = await LerIdsAsync(conn, "SELECT IdDepartamento FROM Departamento ORDER BY IdDepartamento");
    var tiposDocumento = await LerIdsAsync(conn, "SELECT IdTipoDocumento FROM TipoDocumento ORDER BY IdTipoDocumento");
    var tiposViagem = await LerIdsAsync(conn, "SELECT IdTipoViagem FROM TipoViagem ORDER BY IdTipoViagem");
    if (departamentos.Count == 0 || tiposDocumento.Count == 0 || tiposViagem.Count == 0)
        throw new InvalidOperationException("O banco não tem departamentos, tipos de documento ou tipos de viagem. Aplique as migrations antes.");

    var funcoes = new Dictionary<string, int>();
    using (var cmd = new SqlCommand("SELECT Alias, IdFuncao FROM Funcao WHERE Alias IS NOT NULL", conn))
    using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync()) funcoes[r.GetString(0).ToUpperInvariant()] = r.GetInt32(1);
    foreach (var alias in new[] { "ADM", "FIN", "CON", "GES", "SOL" })
        if (!funcoes.ContainsKey(alias)) throw new InvalidOperationException($"Função {alias} não existe no banco.");

    // ---- Usuários fictícios ---------------------------------------------------------------------------
    Console.WriteLine("Criando usuários fictícios...");
    var hash = BCrypt.Net.BCrypt.HashPassword(senha);
    var usuarios = new List<(string Alias, string Email, string Nome)>
    {
        ("ADM", "adm.semente" + DominioEmail, "Usuario Ficticio Administrador"),
        ("FIN", "fin.semente" + DominioEmail, "Usuario Ficticio Financeiro"),
        ("CON", "con.semente" + DominioEmail, "Usuario Ficticio Contabilidade"),
        ("GES", "ges.semente" + DominioEmail, "Usuario Ficticio Gestor"),
    };
    for (int i = 1; i <= 5; i++)
        usuarios.Add(("SOL", $"sol{i:00}.semente{DominioEmail}", $"Usuario Ficticio Solicitante {i:00}"));

    var idsPorAlias = new Dictionary<string, List<int>>();
    foreach (var u in usuarios)
    {
        var dep = departamentos[rng.Next(departamentos.Count)];
        int id;
        using (var cmd = new SqlCommand(@"
            INSERT INTO Usuario (Email, Codigo, Nome, Origem, IdExterno, Senha, IdDepartamento, Ativo, DataCadastro)
            OUTPUT INSERTED.IdUsuario
            VALUES (@Email, NULL, @Nome, 0, '', @Senha, @Dep, 1, @Data)", conn))
        {
            P(cmd, "@Email", System.Data.SqlDbType.VarChar, u.Email, 512);
            P(cmd, "@Nome", System.Data.SqlDbType.VarChar, u.Nome, 512);
            P(cmd, "@Senha", System.Data.SqlDbType.VarChar, hash, 512);
            P(cmd, "@Dep", System.Data.SqlDbType.Int, dep);
            P(cmd, "@Data", System.Data.SqlDbType.DateTime, Ref.AddDays(-400));
            id = (int)(await cmd.ExecuteScalarAsync())!;
        }
        using (var cmd = new SqlCommand("INSERT INTO UsuarioFuncao (IdUsuario, IdFuncao) VALUES (@U, @F)", conn))
        {
            P(cmd, "@U", System.Data.SqlDbType.Int, id);
            P(cmd, "@F", System.Data.SqlDbType.Int, funcoes[u.Alias]);
            await cmd.ExecuteNonQueryAsync();
        }
        if (!idsPorAlias.TryGetValue(u.Alias, out var lista)) idsPorAlias[u.Alias] = lista = new();
        lista.Add(id);
    }
    var idFin = idsPorAlias["FIN"][0];
    var idAdm = idsPorAlias["ADM"][0];
    var idGestor = idsPorAlias["GES"][0];
    var idsSolicitantes = idsPorAlias["SOL"];

    // ---- Plano dos títulos (tipo e estado), embaralhado de forma determinística ----------------------
    int normais = titulos;
    int normaisSol = (int)Math.Round(normais * 0.6);
    int extras = Math.Max(2, normais / 20);
    var plano = new List<(bool Sol, int Estado)>(); // Estado: 0 normal, 1 rascunho, 2 cancelado
    for (int i = 0; i < normais; i++) plano.Add((i < normaisSol, 0));
    for (int i = 0; i < extras; i++) plano.Add((i % 5 < 3, 1 + (i % 2)));
    for (int i = plano.Count - 1; i > 0; i--)
    {
        int j = rng.Next(i + 1);
        (plano[i], plano[j]) = (plano[j], plano[i]);
    }

    Console.WriteLine($"Semeando {normais} títulos exportáveis + {extras} rascunhos/cancelados, ~{mbPorTitulo.ToString(CultureInfo.InvariantCulture)} MB de anexos por título...");

    var ctx = new Ctx(rng, rngConteudo, departamentos, tiposDocumento, tiposViagem, idsSolicitantes, idFin, idAdm, idGestor, mbPorTitulo);
    var estat = new Estatisticas();
    var relogio = System.Diagnostics.Stopwatch.StartNew();

    const int TitulosPorLote = 20;
    for (int inicio = 0; inicio < plano.Count; inicio += TitulosPorLote)
    {
        using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        for (int k = inicio; k < Math.Min(inicio + TitulosPorLote, plano.Count); k++)
        {
            var (sol, estado) = plano[k];
            if (sol) await InserirSolicitacaoAsync(conn, tx, ctx, estado, estat);
            else await InserirRddvAsync(conn, tx, ctx, estado, estat);
        }
        await tx.CommitAsync();

        int feitos = Math.Min(inicio + TitulosPorLote, plano.Count);
        if (feitos % 100 == 0 || feitos == plano.Count)
            Console.WriteLine($"  {feitos}/{plano.Count} títulos, {estat.Anexos} anexos, {estat.BytesAnexos / 1024.0 / 1024.0:F0} MB, {relogio.Elapsed.TotalSeconds:F0} s");
    }

    Console.WriteLine();
    Console.WriteLine("Semente concluída.");
    Console.WriteLine($"  solicitações exportáveis: {estat.SolNormais}   RDDVs exportáveis: {estat.RddvNormais}");
    Console.WriteLine($"  rascunhos: {estat.Rascunhos}   cancelados: {estat.Cancelados}   (devem ficar de fora)");
    Console.WriteLine($"  despesas de RDDV: {estat.Despesas}   anexos: {estat.Anexos}   bytes de anexos: {estat.BytesAnexos}");
    Console.WriteLine("  usuários (senha = a de PAGUEARES_SEMENTE_SENHA): " + string.Join(", ", usuarios.Select(u => u.Email)));
}

static async Task<List<int>> LerIdsAsync(SqlConnection conn, string sql)
{
    var lista = new List<int>();
    using var cmd = new SqlCommand(sql, conn);
    using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync()) lista.Add(r.GetInt32(0));
    return lista;
}

static void P(SqlCommand cmd, string nome, System.Data.SqlDbType tipo, object? valor, int tamanho = 0, byte precisao = 0, byte escala = 0)
{
    var p = new SqlParameter(nome, tipo) { Value = valor ?? DBNull.Value };
    if (tamanho != 0) p.Size = tamanho;
    if (precisao != 0) { p.Precision = precisao; p.Scale = escala; }
    cmd.Parameters.Add(p);
}

static async Task<int> InserirSolicitacaoAsync(SqlConnection conn, SqlTransaction tx, Ctx c, int estado, Estatisticas est)
{
    var rng = c.Rng;
    var solicitante = c.Solicitantes[rng.Next(c.Solicitantes.Count)];
    var dep = c.Departamentos[rng.Next(c.Departamentos.Count)];
    var dataSol = DataAleatoria(rng);
    var dataDoc = dataSol.AddDays(-rng.Next(0, 10));
    var dataVenc = dataSol.AddDays(rng.Next(5, 46));
    var aprovGestor = dataSol.AddDays(1).AddHours(rng.Next(1, 9));

    // Financeiro: 85% autorizado, 10% aguardando, 5% negado. Contábil: só depois de autorizado.
    int sorteioFin = rng.Next(100);
    int statusFin = sorteioFin < 85 ? 2 : sorteioFin < 95 ? 1 : 3;
    DateTime? dataFin = statusFin == 1 ? null : aprovGestor.AddDays(rng.Next(1, 4)).AddHours(rng.Next(0, 8));
    int statusCon = 1;
    DateTime? dataCon = null;
    if (statusFin == 2 && rng.Next(100) < 60) { statusCon = 2; dataCon = dataFin!.Value.AddDays(rng.Next(1, 6)); }

    bool pj = rng.Next(2) == 0;
    string docParceiro = pj ? Cnpj(rng, rng.Next(2) == 0) : Cpf(rng, rng.Next(2) == 0);
    string nomeParceiro = pj ? Empresas[rng.Next(Empresas.Length)] : Pessoa(rng);
    bool parcelado = rng.Next(100) < 25;
    int tipoSol = 1 + rng.Next(3);
    var descricao = $"{Marca} {Itens[rng.Next(Itens.Length)]} ref. {dataSol:MM/yyyy} lote {rng.Next(1000, 9999)}";
    int? atribuido = statusFin != 1 || rng.Next(2) == 0 ? (rng.Next(3) == 0 ? c.IdAdm : c.IdFin) : null;
    bool rascunho = estado == 1, cancelado = estado == 2;

    int id;
    using (var cmd = new SqlCommand(@"
        INSERT INTO Solicitacao (StatusGestor, StatusContabilidade, StatusFinanceiro, IdUsuario, IdDepartamento,
            IdUsuarioAtribuido, DataAtribuicao, IdUsuarioAtribuidor, DataSolicitacao, TipoSolicitacao, TipoPagamento,
            DataDocumento, DataVencimento, NumeroDocumento, Descricao, Valor, Parcelado, Parcelas, FormaPagamento,
            CentroCusto, Projeto, PCA, NumDocParceiro, CodigoParceiro, NomeParceiro, BancoParceiro, AgenciaParceiro,
            ContaParceiro, Observacao, TipoAutorizacao, IdGestor, AprovadoGestor, ObservacaoGestor, AprovadoSetor,
            ObservacaoSetor, DataAprovacaoGestor, DataAprovacaoFinanceiro, DataAprovacaoContabil, Rascunho, Cancelado)
        OUTPUT INSERTED.IdSolicitacao
        VALUES (2, @StCon, @StFin, @Usuario, @Dep,
            @Atribuido, @DataAtrib, @Atribuidor, @DataSol, @TipoSol, @TipoPag,
            @DataDoc, @DataVenc, @NumDoc, @Desc, @Valor, @Parcelado, @Parcelas, @Forma,
            @CC, @Proj, @Pca, @DocParc, @CodParc, @NomeParc, @Banco, @Agencia,
            @Conta, @Obs, 1, @Gestor, 1, @ObsGestor, @AprovSetor,
            @ObsSetor, @DataGestor, @DataFin, @DataCon, @Rascunho, @Cancelado)", conn, tx))
    {
        P(cmd, "@StCon", System.Data.SqlDbType.Int, statusCon);
        P(cmd, "@StFin", System.Data.SqlDbType.Int, statusFin);
        P(cmd, "@Usuario", System.Data.SqlDbType.Int, solicitante);
        P(cmd, "@Dep", System.Data.SqlDbType.Int, dep);
        P(cmd, "@Atribuido", System.Data.SqlDbType.Int, atribuido);
        P(cmd, "@DataAtrib", System.Data.SqlDbType.DateTime2, atribuido == null ? null : aprovGestor.AddHours(2));
        P(cmd, "@Atribuidor", System.Data.SqlDbType.Int, atribuido == null ? null : c.IdAdm);
        P(cmd, "@DataSol", System.Data.SqlDbType.DateTime, dataSol);
        P(cmd, "@TipoSol", System.Data.SqlDbType.Int, tipoSol);
        P(cmd, "@TipoPag", System.Data.SqlDbType.Int, 1 + rng.Next(2));
        P(cmd, "@DataDoc", System.Data.SqlDbType.DateTime, dataDoc);
        P(cmd, "@DataVenc", System.Data.SqlDbType.DateTime, dataVenc);
        P(cmd, "@NumDoc", System.Data.SqlDbType.VarChar, rng.Next(10) == 0 ? "" : $"{rng.Next(0, 99):00}{rng.Next(1, 999999):000000}", 64);
        P(cmd, "@Desc", System.Data.SqlDbType.VarChar, descricao, 512);
        P(cmd, "@Valor", System.Data.SqlDbType.Decimal, Math.Round((decimal)(rng.NextDouble() * 49950 + 50), 2), 0, 12, 2);
        P(cmd, "@Parcelado", System.Data.SqlDbType.Bit, parcelado);
        P(cmd, "@Parcelas", System.Data.SqlDbType.Int, parcelado ? 2 + rng.Next(11) : 1);
        P(cmd, "@Forma", System.Data.SqlDbType.Int, 1 + rng.Next(2));
        P(cmd, "@CC", System.Data.SqlDbType.VarChar, $"CC-{rng.Next(1000, 1100)}", 128);
        P(cmd, "@Proj", System.Data.SqlDbType.VarChar, $"PRJ-{rng.Next(1, 60):000}", 128);
        P(cmd, "@Pca", System.Data.SqlDbType.VarChar, $"PCA-{rng.Next(1, 40):000}", 128);
        P(cmd, "@DocParc", System.Data.SqlDbType.VarChar, docParceiro, 64);
        P(cmd, "@CodParc", System.Data.SqlDbType.VarChar, $"F{rng.Next(1, 99999):00000}", 64);
        P(cmd, "@NomeParc", System.Data.SqlDbType.VarChar, nomeParceiro, 128);
        P(cmd, "@Banco", System.Data.SqlDbType.VarChar, Bancos[rng.Next(Bancos.Length)], 64);
        P(cmd, "@Agencia", System.Data.SqlDbType.VarChar, rng.Next(1, 9999).ToString("0000"), 32);
        P(cmd, "@Conta", System.Data.SqlDbType.VarChar, $"{rng.Next(1, 999999):000000}-{rng.Next(0, 9)}", 32);
        P(cmd, "@Obs", System.Data.SqlDbType.VarChar, rng.Next(3) == 0 ? "Pagamento conforme contrato; conferir anexos." : "", 2048);
        P(cmd, "@Gestor", System.Data.SqlDbType.Int, c.IdGestor);
        P(cmd, "@ObsGestor", System.Data.SqlDbType.VarChar, "Aprovado pelo gestor.", 512);
        P(cmd, "@AprovSetor", System.Data.SqlDbType.Bit, statusFin == 1 ? null : statusFin == 2);
        P(cmd, "@ObsSetor", System.Data.SqlDbType.VarChar, statusFin == 3 ? "Negado: documentação incompleta." : statusFin == 2 ? "Autorizado." : "", 512);
        P(cmd, "@DataGestor", System.Data.SqlDbType.DateTime, aprovGestor);
        P(cmd, "@DataFin", System.Data.SqlDbType.DateTime, dataFin);
        P(cmd, "@DataCon", System.Data.SqlDbType.DateTime, dataCon);
        P(cmd, "@Rascunho", System.Data.SqlDbType.Bit, rascunho);
        P(cmd, "@Cancelado", System.Data.SqlDbType.Bit, cancelado);
        id = (int)(await cmd.ExecuteScalarAsync())!;
    }

    est.Conta(true, estado);
    await InserirAnexosAsync(conn, tx, c, est, "DocumentoSolicitacao", "IdSolicitacao", id, solicitante, dataSol, estado != 0);
    return id;
}

static async Task<int> InserirRddvAsync(SqlConnection conn, SqlTransaction tx, Ctx c, int estado, Estatisticas est)
{
    var rng = c.Rng;
    var solicitante = c.Solicitantes[rng.Next(c.Solicitantes.Count)];
    var dep = c.Departamentos[rng.Next(c.Departamentos.Count)];
    var tipoRel = 1 + rng.Next(3);
    var dataCad = DataAleatoria(rng);
    var inicio = dataCad.Date.AddDays(rng.Next(-20, 10));
    var fim = inicio.AddDays(rng.Next(1, 12));
    var venc = dataCad.AddDays(rng.Next(5, 31));
    var aprovGestor = dataCad.AddDays(1).AddHours(rng.Next(1, 9));
    int sorteioFin = rng.Next(100);
    int statusFin = sorteioFin < 85 ? 2 : sorteioFin < 95 ? 1 : 3;
    DateTime? dataFin = statusFin == 1 ? null : aprovGestor.AddDays(rng.Next(1, 4)).AddHours(rng.Next(0, 8));
    int statusCon = 1;
    DateTime? dataCon = null;
    if (statusFin == 2 && rng.Next(100) < 60) { statusCon = 2; dataCon = dataFin!.Value.AddDays(rng.Next(1, 6)); }

    bool diaria = rng.Next(100) < 70;
    bool adiant = tipoRel == 1 || rng.Next(100) < 30;
    string moeda = rng.Next(100) < 80 ? "BRL" : rng.Next(2) == 0 ? "USD" : "EUR";
    var cidade = Cidades[rng.Next(Cidades.Length)];
    var finalidade = $"{Marca} Viagem a {cidade} para {Motivos[rng.Next(Motivos.Length)]}";
    int? atribuido = statusFin != 1 || rng.Next(2) == 0 ? c.IdFin : null;
    bool rascunho = estado == 1, cancelado = estado == 2;

    int id;
    using (var cmd = new SqlCommand(@"
        INSERT INTO Rddv (IdUsuario, IdUsuarioAtribuido, DataAtribuicao, IdUsuarioAtribuidor, NomeFuncionario, CPF,
            IdDepartamento, Finalidade, TipoRelatorio, Moeda, TipoViagem, DataInicio, DataFim, DataCadastro,
            DataVencimento, LocalViagem, CentroCusto, Projeto, PCA, DiariaViagem, Diarias, Adiantamento,
            ValorAdiantamento, SaldoCartaoVtm, Observacao, TipoAutorizacao, IdGestor, AprovadoGestor, ObservacaoGestor,
            AprovadoSetor, ObservacaoSetor, StatusGestor, StatusContabilidade, StatusFinanceiro, Banco, Agencia, Conta,
            ValorDiaria, ValorKm, DataAprovacaoGestor, DataAprovacaoFinanceiro, DataAprovacaoContabil, Rascunho, Cancelado)
        OUTPUT INSERTED.IdRelatorio
        VALUES (@Usuario, @Atribuido, @DataAtrib, @Atribuidor, @Func, @Cpf,
            @Dep, @Finalidade, @TipoRel, @Moeda, @TipoViagem, @Inicio, @Fim, @Cad,
            @Venc, @Local, @CC, @Proj, @Pca, @DiariaViagem, @Diarias, @Adiant,
            @ValorAdiant, @SaldoVtm, @Obs, 1, @Gestor, 1, @ObsGestor,
            @AprovSetor, @ObsSetor, 2, @StCon, @StFin, @Banco, @Agencia, @Conta,
            @ValorDiaria, @ValorKm, @DataGestor, @DataFin, @DataCon, @Rascunho, @Cancelado)", conn, tx))
    {
        P(cmd, "@Usuario", System.Data.SqlDbType.Int, solicitante);
        P(cmd, "@Atribuido", System.Data.SqlDbType.Int, atribuido);
        P(cmd, "@DataAtrib", System.Data.SqlDbType.DateTime2, atribuido == null ? null : aprovGestor.AddHours(2));
        P(cmd, "@Atribuidor", System.Data.SqlDbType.Int, atribuido == null ? null : c.IdAdm);
        P(cmd, "@Func", System.Data.SqlDbType.NVarChar, Pessoa(rng), -1);
        P(cmd, "@Cpf", System.Data.SqlDbType.NVarChar, Cpf(rng, rng.Next(2) == 0), -1);
        P(cmd, "@Dep", System.Data.SqlDbType.Int, dep);
        P(cmd, "@Finalidade", System.Data.SqlDbType.NVarChar, finalidade, -1);
        P(cmd, "@TipoRel", System.Data.SqlDbType.Int, tipoRel);
        P(cmd, "@Moeda", System.Data.SqlDbType.NVarChar, moeda, -1);
        P(cmd, "@TipoViagem", System.Data.SqlDbType.Int, c.TiposViagem[rng.Next(c.TiposViagem.Count)]);
        P(cmd, "@Inicio", System.Data.SqlDbType.DateTime, inicio);
        P(cmd, "@Fim", System.Data.SqlDbType.DateTime, fim);
        P(cmd, "@Cad", System.Data.SqlDbType.DateTime, dataCad);
        P(cmd, "@Venc", System.Data.SqlDbType.DateTime, venc);
        P(cmd, "@Local", System.Data.SqlDbType.NVarChar, cidade, -1);
        P(cmd, "@CC", System.Data.SqlDbType.NVarChar, $"CC-{rng.Next(1000, 1100)}", -1);
        P(cmd, "@Proj", System.Data.SqlDbType.NVarChar, $"PRJ-{rng.Next(1, 60):000}", -1);
        P(cmd, "@Pca", System.Data.SqlDbType.NVarChar, $"PCA-{rng.Next(1, 40):000}", -1);
        P(cmd, "@DiariaViagem", System.Data.SqlDbType.Bit, diaria);
        P(cmd, "@Diarias", System.Data.SqlDbType.Decimal, diaria ? (decimal)(rng.Next(1, 15) + (rng.Next(2) == 0 ? 0.5 : 0)) : null, 0, 5, 2);
        P(cmd, "@Adiant", System.Data.SqlDbType.Bit, adiant);
        P(cmd, "@ValorAdiant", System.Data.SqlDbType.Decimal, adiant ? Math.Round((decimal)(rng.NextDouble() * 3000 + 100), 2) : 0m, 0, 18, 2);
        P(cmd, "@SaldoVtm", System.Data.SqlDbType.Decimal, rng.Next(100) < 20 ? Math.Round((decimal)(rng.NextDouble() * 200), 2) : 0m, 0, 18, 2);
        P(cmd, "@Obs", System.Data.SqlDbType.NVarChar, rng.Next(3) == 0 ? "Prestação conforme política de viagens." : "", -1);
        P(cmd, "@Gestor", System.Data.SqlDbType.Int, c.IdGestor);
        P(cmd, "@ObsGestor", System.Data.SqlDbType.NVarChar, "Aprovado pelo gestor.", -1);
        P(cmd, "@AprovSetor", System.Data.SqlDbType.Bit, statusFin == 1 ? null : statusFin == 2);
        P(cmd, "@ObsSetor", System.Data.SqlDbType.NVarChar, statusFin == 3 ? "Negado: comprovantes ilegíveis." : statusFin == 2 ? "Autorizado." : "", -1);
        P(cmd, "@StCon", System.Data.SqlDbType.Int, statusCon);
        P(cmd, "@StFin", System.Data.SqlDbType.Int, statusFin);
        P(cmd, "@Banco", System.Data.SqlDbType.NVarChar, Bancos[rng.Next(Bancos.Length)], -1);
        P(cmd, "@Agencia", System.Data.SqlDbType.NVarChar, rng.Next(1, 9999).ToString("0000"), -1);
        P(cmd, "@Conta", System.Data.SqlDbType.NVarChar, $"{rng.Next(1, 999999):000000}-{rng.Next(0, 9)}", -1);
        P(cmd, "@ValorDiaria", System.Data.SqlDbType.Decimal, Math.Round((decimal)(rng.NextDouble() * 300 + 80), 2), 0, 18, 2);
        P(cmd, "@ValorKm", System.Data.SqlDbType.Decimal, Math.Round((decimal)(rng.NextDouble() * 1.5 + 0.5), 2), 0, 18, 2);
        P(cmd, "@DataGestor", System.Data.SqlDbType.DateTime, aprovGestor);
        P(cmd, "@DataFin", System.Data.SqlDbType.DateTime, dataFin);
        P(cmd, "@DataCon", System.Data.SqlDbType.DateTime, dataCon);
        P(cmd, "@Rascunho", System.Data.SqlDbType.Bit, rascunho);
        P(cmd, "@Cancelado", System.Data.SqlDbType.Bit, cancelado);
        id = (int)(await cmd.ExecuteScalarAsync())!;
    }

    // Despesas: 2 a 8 por RDDV, dentro do período da viagem.
    int nDesp = 2 + rng.Next(7);
    for (int d = 0; d < nDesp; d++)
    {
        using var cmd = new SqlCommand(@"
            INSERT INTO DespesaRddv (IdRelatorio, DataDespesa, TipoDespesa, Moeda, Valor, Quantidade)
            VALUES (@Rel, @Data, @Tipo, @Moeda, @Valor, @Qtd)", conn, tx);
        int tipoDesp = 1 + rng.Next(12);
        P(cmd, "@Rel", System.Data.SqlDbType.Int, id);
        P(cmd, "@Data", System.Data.SqlDbType.DateTime, inicio.AddDays(rng.Next(0, (int)(fim - inicio).TotalDays + 1)));
        P(cmd, "@Tipo", System.Data.SqlDbType.Int, tipoDesp);
        P(cmd, "@Moeda", System.Data.SqlDbType.NVarChar, moeda, -1);
        P(cmd, "@Valor", System.Data.SqlDbType.Decimal, Math.Round((decimal)(rng.NextDouble() * 900 + 10), 2), 0, 18, 2);
        P(cmd, "@Qtd", System.Data.SqlDbType.Decimal, tipoDesp == 5 ? Math.Round((decimal)(rng.NextDouble() * 300 + 5), 2) : rng.Next(3) == 0 ? 1m : null, 0, 18, 4);
        await cmd.ExecuteNonQueryAsync();
        est.Despesas++;
    }

    est.Conta(false, estado);
    await InserirAnexosAsync(conn, tx, c, est, "DocumentoRddv", "IdRelatorio", id, solicitante, dataCad, estado != 0);
    return id;
}

// Insere 1 a 3 anexos do título, um por comando; o conteúdo de cada um existe em memória só durante o comando.
static async Task InserirAnexosAsync(SqlConnection conn, SqlTransaction tx, Ctx c, Estatisticas est,
    string tabela, string colunaTitulo, int idTitulo, int idUsuario, DateTime dataBase, bool pequeno)
{
    var rng = c.Rng;
    int n = 1 + rng.Next(3);
    double mbTotal = pequeno ? Math.Min(c.MbPorTitulo, 0.05) : c.MbPorTitulo;
    long bytesTotal = (long)(mbTotal * 1024 * 1024);

    // Pesos aleatórios para repartir o total; cada anexo tem ao menos 1 KB.
    var pesos = Enumerable.Range(0, n).Select(_ => 0.2 + rng.NextDouble()).ToArray();
    double somaPesos = pesos.Sum();
    int idxNomeMalicioso = !pequeno && rng.Next(100) < 12 ? rng.Next(n) : -1;
    bool duplicar = n >= 2 && rng.Next(100) < 15;
    int tipoDocPrimeiro = 0;
    string? nomePrimeiro = null;

    for (int a = 0; a < n; a++)
    {
        long tam = Math.Max(1024, (long)(bytesTotal * pesos[a] / somaPesos));
        bool jpeg = rng.Next(100) < 30;
        string ext = jpeg ? "jpg" : "pdf";
        int tipoDoc = c.TiposDocumento[rng.Next(c.TiposDocumento.Count)];
        string nome;

        if (a == idxNomeMalicioso)
            nome = NomesMaliciosos[rng.Next(NomesMaliciosos.Length)];
        else if (a == 1 && duplicar && nomePrimeiro != null)
        {
            // Mesmo tipo e mesmo nome (às vezes em outra caixa): exercita a deduplicação.
            tipoDoc = tipoDocPrimeiro;
            nome = rng.Next(2) == 0 ? nomePrimeiro : nomePrimeiro.ToUpperInvariant();
        }
        else
            nome = $"{NomesBase[rng.Next(NomesBase.Length)]}.{ext}";

        if (a == 0) { tipoDocPrimeiro = tipoDoc; nomePrimeiro = nome; }

        var bytes = GerarConteudo(c.RngConteudo, tam, jpeg);
        var b64 = Convert.ToBase64String(bytes);
        if (rng.Next(2) == 0) b64 = (jpeg ? "data:image/jpeg;base64," : "data:application/pdf;base64,") + b64;

        using var cmd = new SqlCommand($@"
            INSERT INTO {tabela} ({colunaTitulo}, IdTipoDocumento, IdUsuario, DataCriacao, Indice, NomeArquivo, CaminhoArquivo, Arquivo)
            VALUES (@Titulo, @Tipo, @Usuario, @Data, @Indice, @Nome, @Caminho, @Arquivo)", conn, tx)
        { CommandTimeout = 600 };
        P(cmd, "@Titulo", System.Data.SqlDbType.Int, idTitulo);
        P(cmd, "@Tipo", System.Data.SqlDbType.Int, tipoDoc);
        P(cmd, "@Usuario", System.Data.SqlDbType.Int, idUsuario);
        P(cmd, "@Data", System.Data.SqlDbType.DateTime, dataBase.AddHours(rng.Next(1, 20)));
        P(cmd, "@Indice", System.Data.SqlDbType.Int, a);
        P(cmd, "@Nome", System.Data.SqlDbType.VarChar, nome.Length > 256 ? nome[..256] : nome, 256);
        P(cmd, "@Caminho", System.Data.SqlDbType.VarChar, $"semente/{tabela}/{idTitulo}/{a}", 1024);
        // Base64 é ASCII puro: varchar(max) converte para a coluna text sem perda e sem conversão proibida.
        P(cmd, "@Arquivo", System.Data.SqlDbType.VarChar, b64, -1);
        await cmd.ExecuteNonQueryAsync();

        est.Anexos++;
        est.BytesAnexos += bytes.LongLength;
    }
}

static byte[] GerarConteudo(Random rng, long tamanho, bool jpeg)
{
    var buf = new byte[tamanho];
    byte[] cabecalho = jpeg
        ? [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]
        : Encoding.ASCII.GetBytes("%PDF-1.7\n%âãÏÓ\n");
    byte[] rodape = jpeg ? [0xFF, 0xD9] : Encoding.ASCII.GetBytes("\n%%EOF\n");
    if (tamanho <= cabecalho.Length + rodape.Length)
    {
        rng.NextBytes(buf);
        return buf;
    }
    Array.Copy(cabecalho, buf, cabecalho.Length);
    rng.NextBytes(buf.AsSpan(cabecalho.Length, buf.Length - cabecalho.Length - rodape.Length));
    Array.Copy(rodape, 0, buf, buf.Length - rodape.Length, rodape.Length);
    return buf;
}

// Datas dentro dos 12 meses anteriores à data de referência fixa (determinístico, independe do relógio).
static DateTime DataAleatoria(Random rng) => Ref.AddDays(-rng.Next(0, 365)).AddMinutes(rng.Next(0, 24 * 60 - 1));

static string Pessoa(Random rng) =>
    $"{Nomes[rng.Next(Nomes.Length)]} {Sobrenomes[rng.Next(Sobrenomes.Length)]} {Sobrenomes[rng.Next(Sobrenomes.Length)]}";

// CPF e CNPJ com dígito verificador válido, gerados ao acaso a partir da semente (sem relação com pessoas reais).
static string Cpf(Random rng, bool formatado)
{
    var d = new int[11];
    do { for (int i = 0; i < 9; i++) d[i] = rng.Next(10); } while (d.Take(9).Distinct().Count() == 1);
    d[9] = DigitoCpf(d, 9, 10);
    d[10] = DigitoCpf(d, 10, 11);
    var s = string.Concat(d);
    return formatado ? $"{s[..3]}.{s[3..6]}.{s[6..9]}-{s[9..]}" : s;
}

static int DigitoCpf(int[] d, int n, int pesoInicial)
{
    int soma = 0;
    for (int i = 0; i < n; i++) soma += d[i] * (pesoInicial - i);
    int resto = soma % 11;
    return resto < 2 ? 0 : 11 - resto;
}

static string Cnpj(Random rng, bool formatado)
{
    var d = new int[14];
    for (int i = 0; i < 8; i++) d[i] = rng.Next(10);
    d[8] = 0; d[9] = 0; d[10] = 0; d[11] = 1;
    int[] p1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    int[] p2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    d[12] = DigitoCnpj(d, 12, p1);
    d[13] = DigitoCnpj(d, 13, p2);
    var s = string.Concat(d);
    return formatado ? $"{s[..2]}.{s[2..5]}.{s[5..8]}/{s[8..12]}-{s[12..]}" : s;
}

static int DigitoCnpj(int[] d, int n, int[] pesos)
{
    int soma = 0;
    for (int i = 0; i < n; i++) soma += d[i] * pesos[i];
    int resto = soma % 11;
    return resto < 2 ? 0 : 11 - resto;
}

// =====================================================================================================
// Tipos e dados fixos
// =====================================================================================================

// Listas fixas e inventadas: nada aqui corresponde a pessoa, empresa ou banco real.
static class Fixos
{
    // Data de referência fixa: os dados dependem só da semente, nunca do relógio da máquina.
    public static readonly DateTime Ref = new(2026, 10, 31, 12, 0, 0);

    public static readonly string[] Nomes =
    [
        "Aurora", "Benedito", "Camila", "Davi", "Eloisa", "Fabricio", "Graziela", "Heitor", "Iara", "Jonas",
        "Karina", "Leandro", "Marisa", "Nelson", "Olivia", "Paulo", "Quiteria", "Rogerio", "Sabrina", "Tadeu",
        "Ursula", "Valter", "Wanda", "Xavier", "Yara", "Zeno"
    ];

    public static readonly string[] Sobrenomes =
    [
        "Albuquerque", "Barreto", "Cavalcante", "Dantas", "Esteves", "Fontoura", "Guimaraes", "Holanda", "Ibiapina",
        "Junqueira", "Lacerda", "Magalhaes", "Nogueira", "Oliveira", "Pimentel", "Quintela", "Rezende", "Sampaio",
        "Tavares", "Uchoa", "Vasconcelos", "Wanderley", "Ximenes", "Yamamoto", "Zagalo"
    ];

    public static readonly string[] Empresas =
    [
        "Comercial Alfa Exemplo Ltda", "Distribuidora Beta Ficticia SA", "Servicos Gama de Teste ME", "Industria Delta Imaginaria Ltda",
        "Transportes Epsilon Exemplo", "Tecnologia Zeta Ficticia Ltda", "Papelaria Eta de Teste", "Engenharia Teta Exemplo SA",
        "Suprimentos Iota Ficticios Ltda", "Logistica Kappa Imaginaria", "Consultoria Lambda Exemplo", "Metalurgica Mi de Teste Ltda"
    ];

    public static readonly string[] Bancos =
    [
        "Banco Exemplo", "Banco Ficticio S.A.", "Banco de Teste do Brasil", "Banco Imaginario", "Caixa Ficticia"
    ];

    public static readonly string[] Itens =
    [
        "Servico de manutencao predial", "Compra de material de escritorio", "Licenca de software", "Frete de equipamentos",
        "Consultoria tecnica", "Locacao de equipamentos", "Servico de limpeza", "Compra de pecas de reposicao",
        "Treinamento de equipe", "Servico de impressao", "Assinatura de plataforma", "Manutencao de veiculos"
    ];

    public static readonly string[] Cidades =
    [
        "Cidade Exemplo", "Vila Ficticia", "Porto Imaginario", "Serra de Teste", "Campo Inventado", "Recanto de Mentira"
    ];

    public static readonly string[] Motivos =
    [
        "reuniao com cliente", "treinamento", "visita tecnica", "feira de negocios", "auditoria interna", "instalacao de equipamento"
    ];

    // Nomes de anexo comuns (com e sem acento, espaços) para o dia a dia da exportação.
    public static readonly string[] NomesBase =
    [
        "boleto marco", "boleto março", "nota fiscal 1234", "comprovante de pagamento", "recibo", "fatura janeiro",
        "autorizacao do gestor", "contrato assinado", "orcamento", "ordem de compra", "extrato", "relatorio de viagem"
    ];

    // Nomes "maliciosos" ou problemáticos para exercitar a sanitização do pacote (C1).
    public static readonly string[] NomesMaliciosos =
    [
        "../x.pdf",
        "..\\..\\windows\\system.ini",
        "../../etc/passwd",
        "C:\\Windows\\x.exe",
        "CON.pdf",
        "nul",
        "a/b\\c.pdf",
        "nome com espaco final .pdf ",
        "arquivo<>:\"|?*.pdf",
        new string('n', 245) + ".pdf",
        "",
        "..",
        "COM1.txt"
    ];
}

sealed record Ctx(Random Rng, Random RngConteudo, List<int> Departamentos, List<int> TiposDocumento, List<int> TiposViagem,
    List<int> Solicitantes, int IdFin, int IdAdm, int IdGestor, double MbPorTitulo);

sealed class Estatisticas
{
    public int SolNormais, RddvNormais, Rascunhos, Cancelados, Despesas, Anexos;
    public long BytesAnexos;
    public void Conta(bool sol, int estado)
    {
        if (estado == 1) Rascunhos++;
        else if (estado == 2) Cancelados++;
        else if (sol) SolNormais++;
        else RddvNormais++;
    }
}
