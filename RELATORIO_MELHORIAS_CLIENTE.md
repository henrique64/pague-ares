# PagueAres — Relatório de Melhorias
**Data:** Junho de 2026

---

## Visão Geral

Nesta fase de desenvolvimento, o sistema PagueAres recebeu um conjunto abrangente de melhorias cobrindo modernização tecnológica, correção de defeitos, endurecimento de segurança e implantação de cobertura de testes. As mudanças foram entregues de forma incremental e validadas por uma suite de 107 testes automatizados.

---

## 1. Modernização Tecnológica

**Atualização de plataforma backend**
O servidor foi migrado do .NET 8 para o **.NET 10** (versão LTS mais recente), trazendo melhorias de desempenho, suporte estendido e acesso a novas funcionalidades da linguagem C#.

**Atualização de plataforma frontend**
O aplicativo web foi migrado do Angular 12 para o **Angular 20**, com adoção do novo pipeline de build baseado em `esbuild`. O resultado é um tempo de compilação significativamente menor e bundles mais enxutos para os usuários finais.

---

## 2. Segurança

Esta foi a área com maior volume de correções. Foram identificadas e corrigidas **9 vulnerabilidades** no controle de acesso da API.

**Proteção de dados de usuários**
Anteriormente, qualquer usuário autenticado conseguia acessar a lista completa de usuários do sistema, visualizar o perfil de outros usuários e criar ou alterar contas. Agora essas operações são restritas ao papel **Administrador**.

**Proteção de configurações do sistema**
A alteração de parâmetros do sistema (como o valor de quilometragem por km) estava acessível a qualquer usuário logado. O acesso de gravação foi restrito ao **Administrador**.

**Prevenção de escalada de privilégios vertical**
Um usuário sem papel de aprovação conseguia, enviando requisições diretamente à API, alterar o status de aprovação de Gestor, Financeiro ou Contabilidade. Cada estágio de aprovação agora verifica se o usuário possui o papel correspondente antes de aceitar a mudança.

**Prevenção de escalada de privilégios horizontal**
Um usuário conseguia editar solicitações criadas por outros usuários. As operações de edição agora verificam a propriedade do registro: somente o criador da solicitação (ou usuários com papéis de aprovação) podem modificá-la.

**Validação reforçada de tokens JWT**
A API passou a rejeitar corretamente tokens com assinatura adulterada e tokens expirados, retornando HTTP 401 em ambos os casos.

---

## 3. Correções de Defeitos

**Migração segura de senhas**
O sistema adotou o algoritmo **BCrypt** para armazenamento de senhas, substituindo o SHA-256 que era utilizado anteriormente. A migração é transparente: na próxima vez que um usuário fizer login com a senha antiga (SHA-256), a senha é automaticamente remigrada para BCrypt sem necessidade de redefinição.

**Limitação de tentativas de login**
Após 10 tentativas de login com credenciais inválidas, a conta é temporariamente bloqueada, com uma mensagem clara informando o usuário. Isso protege contra ataques de força bruta.

**Precisão em cálculos monetários**
Correção de erro de arredondamento em operações com valores decimais que podia causar divergências em relatórios financeiros.

**Formulário de RDDV**
Três defeitos foram corrigidos no formulário de criação/edição de relatórios de viagem:
- Campos de formulário não sendo preenchidos corretamente ao editar um registro existente
- Uploads de documentos não sendo associados ao RDDV correto em determinados fluxos
- Botões de aprovação exibindo estado incorreto em algumas situações

---

## 4. Qualidade e Testes

Foi implantada uma suite completa de testes automatizados para garantir a estabilidade contínua do sistema.

**Testes de integração (backend):** 43 cenários cobrindo o fluxo de aprovação em 3 estágios, autenticação, segregação de dados e os 20 cenários de segurança corrigidos.

**Testes unitários (backend):** 15 cenários cobrindo o serviço de autenticação (BCrypt, SHA-256, migração, lockout) e validação de tokens de preview.

**Testes unitários (frontend):** 49 cenários cobrindo os componentes principais da interface, serviços de autenticação e utilitários.

**Testes E2E (Cypress):** Estrutura criada com 5 suites de testes de ponta a ponta (login, solicitações, RDDVs, fluxo de aprovação e controle de acesso), prontas para execução em pipeline de CI/CD.

**Total: 107 testes automatizados — 100% aprovados.**

---

## Resumo

| Área | Mudanças |
|------|----------|
| Plataforma | .NET 8 → .NET 10, Angular 12 → Angular 20 |
| Segurança | 9 vulnerabilidades corrigidas |
| Senhas | SHA-256 → BCrypt com migração automática |
| Login | Rate limiting (10 tentativas) |
| Defeitos | Precisão decimal, 3 bugs no RDDV |
| Testes | 107 testes automatizados (0 falhas) |
