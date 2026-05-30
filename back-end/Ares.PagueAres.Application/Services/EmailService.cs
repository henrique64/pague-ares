using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace Ares.PagueAres.Application.Services
{
    public class EmailService
    {

        private readonly SmtpSettings _smtp;
        private readonly PagueAresContext _dbContext;

        public EmailService(PagueAresContext dbContext, IOptions<SmtpSettings> smtp)
        {
            _smtp = smtp.Value;
            _dbContext = dbContext;
        }

        private record RequestData(string UserEmail, string UserName, string ManagerEmail, string ManagerName, string AssigneeName, string AssigneeEmail, DateTime RequestDate, decimal Value);

        public void SendEmail(string subject, string content, string to)
        {
            if (_smtp == null)
            {
                return;
            }

            if (_smtp.SmtpHost == null ||
                _smtp.SmtpUser == null ||
                _smtp.SmtpPassword == null ||
                _smtp.EnableSsl == null)
            {
                return;
            }

            try
            {
                SmtpClient cli = new();

                cli.DeliveryMethod = SmtpDeliveryMethod.Network;
                cli.Host = _smtp.SmtpHost;
                cli.Port = _smtp.SmtpPort;
                cli.EnableSsl = bool.Parse(_smtp.EnableSsl);
                cli.Credentials = new NetworkCredential(_smtp.SmtpUser, _smtp.SmtpPassword);

                string sender = "";

                if (string.IsNullOrEmpty(_smtp.DefaultSender))
                {
                    sender = _smtp.SmtpUser;
                }
                else
                {
                    sender = _smtp.DefaultSender;
                }

                MailMessage message = new(sender, to);

                message.IsBodyHtml = false;
                message.Subject = subject;
                message.SubjectEncoding = Encoding.UTF8;
                message.BodyEncoding = Encoding.UTF8;
                message.Body = content;

                cli.SendAsync(message, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ocorreu um erro ao enviar o e-mail ({subject}) de notificação para {to}: {ex.Message}");
            }
        }

        private async Task<RequestData?> GetRequestData(bool isRddv, int id)
        {
            DateTime requestDate;

            string userName = "";
            string userEmail = "";
            string managerName = "";
            string managerEmail = "";
            string assigneeName = "";
            string assigneeEmail = "";
            decimal value = 0m;

            if (isRddv)
            {
                var req = await (from r in _dbContext.Rddv
                                 from t in _dbContext.vDespesasRddv.Where(x => x.IdRelatorio == r.IdRelatorio)
                                 from u in _dbContext.Usuarios.Where(x => x.IdUsuario == r.IdUsuario)
                                 from g in _dbContext.Usuarios.Where(x => x.IdUsuario == r.IdGestor).DefaultIfEmpty()
                                 from a in _dbContext.Usuarios.Where(x => x.IdUsuario == r.IdUsuarioAtribuido).DefaultIfEmpty()
                                 where r.IdRelatorio == id
                                 select new
                                 {
                                     RequestId = r.IdRelatorio,
                                     DateTime = r.DataCadastro.ToLocalTime(),
                                     User = new
                                     {
                                         Email = u.Email,
                                         Name = u.Nome
                                     },
                                     Manager = new
                                     {
                                         Email = g.Email,
                                         Name = g.Nome
                                     },
                                     Assignee = new
                                     {
                                         Email = a.Email,
                                         Name = a.Nome
                                     },
                                     Value = t.Valor
                                 }).FirstOrDefaultAsync();

                if (req == null)
                    return null;

                requestDate = req.DateTime;

                userName = req.User.Name;
                userEmail = req.User.Email;

                managerName = req.Manager.Name;
                managerEmail = req.Manager.Email;

                assigneeName = req.Assignee.Name;
                assigneeEmail = req.Assignee.Email;

                value = req.Value;
            }
            else
            {
                var req = await (from r in _dbContext.Solicitacaos
                                 from u in _dbContext.Usuarios.Where(x => x.IdUsuario == r.IdUsuario)
                                 from g in _dbContext.Usuarios.Where(x => x.IdUsuario == r.IdGestor).DefaultIfEmpty()
                                 from a in _dbContext.Usuarios.Where(x => x.IdUsuario == r.IdUsuarioAtribuido).DefaultIfEmpty()
                                 where r.IdSolicitacao == id
                                 select new
                                 {
                                     RequestId = r.IdSolicitacao,
                                     DateTime = r.DataSolicitacao.ToLocalTime(),
                                     User = new
                                     {
                                         Email = u.Email,
                                         Name = u.Nome
                                     },
                                     Manager = new
                                     {
                                         Email = g.Email,
                                         Name = g.Nome
                                     },
                                     Assignee = new
                                     {
                                         Email = a.Email,
                                         Name = a.Nome
                                     },
                                     Value = r.Valor
                                 }).FirstOrDefaultAsync();

                if (req == null)
                    return null;

                requestDate = req.DateTime;

                userName = req.User.Name;
                userEmail = req.User.Email;

                managerName = req.Manager.Name;
                managerEmail = req.Manager.Email;

                assigneeName = req.Assignee.Name;
                assigneeEmail = req.Assignee.Email;

                value = req.Value;
            }

            return new RequestData(userEmail, userName, managerEmail, managerName, assigneeName, assigneeEmail, requestDate, value);
        }

        public async Task<bool> SendNewRequestEmail(bool isRddv, int id)
        {
            var data = await GetRequestData(isRddv, id);

            if (data == null)
                return false;

            string subject = $"Pague Ares - Nova Solicitação";

            if (!string.IsNullOrEmpty(data.UserEmail))
            {
                string contentUser = $"Você criou {(isRddv ? "um novo RDDV" : "uma nova solicitação")} em {data.RequestDate.ToString("dd/MM/yyyy HH:mm")} no valor de {data.Value.ToString("0.00").Replace(".", ",")}. Sua solicitação está em análise.";
                SendEmail(subject, contentUser, data.UserEmail);
            }

            if (!string.IsNullOrEmpty(data.ManagerEmail))
            {
                string contentUser = $"O usuário {data.UserName} criou {(isRddv ? "um novo RDDV" : "uma nova solicitação")} em {data.RequestDate.ToString("dd/MM/yyyy HH:mm")} no valor de {data.Value.ToString("0.00").Replace(".", ",")}. O usuário aguarda sua aprovação.";
                SendEmail(subject, contentUser, data.ManagerEmail);
            }

            return true;
        }

        public async Task<bool> SendRequestAssignedEmail(bool isRddv, int id)
        {
            var data = await GetRequestData(isRddv, id);

            if (data == null)
                return false;

            string subject = $"Pague Ares - Solicitação Atribuída";

            if (!string.IsNullOrEmpty(data.UserEmail))
            {
                string contentUser = $"{(isRddv ? "Seu RDDV" : "Sua solicitação")} com código {id}, criada em {data.RequestDate.ToString("dd/MM/yyyy HH:mm")} no valor de {data.Value.ToString("0.00").Replace(".", ",")}, foi atribuída.";
                SendEmail(subject, contentUser, data.UserEmail);
            }

            if (!string.IsNullOrEmpty(data.AssigneeEmail))
            {
                string contentUser = $"{(isRddv ? "O RDDV" : "A solicitação")} com código {id}, criada em {data.RequestDate.ToString("dd/MM/yyyy HH:mm")} no valor de {data.Value.ToString("0.00").Replace(".", ",")}, foi atribuída a você.";
                SendEmail(subject, contentUser, data.AssigneeEmail);
            }

            return true;
        }

        public async Task<bool> SendApprovalEmail(bool isRddv, int id)
        {
            var data = await GetRequestData(isRddv, id);

            if (data == null)
                return false;

            string subject = $"Pague Ares - Solicitação Aprovada";

            if (!string.IsNullOrEmpty(data.UserEmail))
            {
                string contentUser = $"{(isRddv ? "Seu RDDV" : "Sua solicitação")} com código {id}, criada em {data.RequestDate.ToString("dd/MM/yyyy HH:mm")} no valor de {data.Value.ToString("0.00").Replace(".", ",")}, foi aprovado(a) pelo gestor.";
                SendEmail(subject, contentUser, data.UserEmail);
            }

            return true;
        }

        public async Task<bool> SendDenialEmail(bool isRddv, int id)
        {
            var data = await GetRequestData(isRddv, id);

            if (data == null)
                return false;

            string subject = $"Pague Ares - Solicitação Negada";

            if (!string.IsNullOrEmpty(data.UserEmail))
            {
                string contentUser = $"{(isRddv ? "Seu RDDV" : "Sua solicitação")} com código {id}, criada em {data.RequestDate.ToString("dd/MM/yyyy HH:mm")} no valor de {data.Value.ToString("0.00").Replace(".", ",")}, foi negado(a) pelo gestor.";
                SendEmail(subject, contentUser, data.UserEmail);
            }

            return true;
        }

        public async Task<bool> SendPaidEmail(bool isRddv, int id)
        {
            var data = await GetRequestData(isRddv, id);

            if (data == null)
                return false;

            string subject = $"Pague Ares - Solicitação Autorizada";

            if (!string.IsNullOrEmpty(data.UserEmail))
            {
                string contentUser = $"{(isRddv ? "Seu RDDV" : "Sua solicitação")} com código {id}, criada em {data.RequestDate.ToString("dd/MM/yyyy HH:mm")} no valor de {data.Value.ToString("0.00").Replace(".", ",")}, foi aprovada.";
                SendEmail(subject, contentUser, data.UserEmail);
            }

            return true;
        }

        public async Task<bool> SendPaymentDeniedEmail(bool isRddv, int id)
        {
            var data = await GetRequestData(isRddv, id);

            if (data == null)
                return false;

            string subject = $"Pague Ares - Pagamento da Solicitação Recusado";

            if (!string.IsNullOrEmpty(data.UserEmail))
            {
                string contentUser = $"{(isRddv ? "Seu RDDV" : "Sua solicitação")} com código {id}, criada em {data.RequestDate.ToString("dd/MM/yyyy HH:mm")} no valor de {data.Value.ToString("0.00").Replace(".", ",")}, teve o pagamento recusado.";
                SendEmail(subject, contentUser, data.UserEmail);
            }

            return true;
        }

        public async Task<bool> SendAccountingDenialEmail(bool isRddv, int id)
        {
            var data = await GetRequestData(isRddv, id);

            if (data == null)
                return false;

            string subject = $"Pague Ares - Solicitação Negada";

            if (!string.IsNullOrEmpty(data.UserEmail))
            {
                string contentUser = $"{(isRddv ? "Seu RDDV" : "Sua solicitação")} com código {id}, criada em {data.RequestDate.ToString("dd/MM/yyyy HH:mm")} no valor de {data.Value.ToString("0.00").Replace(".", ",")}, foi negado(a) pela contabilidade.";
                SendEmail(subject, contentUser, data.UserEmail);
            }

            return true;
        }

        public async Task<bool> SendAccountingPaidEmail(bool isRddv, int id)
        {
            var data = await GetRequestData(isRddv, id);

            if (data == null)
                return false;

            string subject = $"Pague Ares - Solicitação Lançada";

            if (!string.IsNullOrEmpty(data.UserEmail))
            {
                string contentUser = $"{(isRddv ? "Seu RDDV" : "Sua solicitação")} com código {id}, criada em {data.RequestDate.ToString("dd/MM/yyyy HH:mm")} no valor de {data.Value.ToString("0.00").Replace(".", ",")}, foi lançado(a) pela contabilidade.";
                SendEmail(subject, contentUser, data.UserEmail);
            }

            return true;
        }

    }
}
