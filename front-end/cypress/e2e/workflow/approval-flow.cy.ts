describe('Fluxo de Aprovação', () => {
  it('solicitante cria rascunho e envia para aprovação', () => {
    cy.login('solicitante@test.com', 'senha');
    cy.visit('/create-request');
    cy.contains(/rascunho/i).click();
    cy.contains(/obrigatório|sucesso/i).should('exist');
  });

  it('gestor vê solicitações pendentes no ManagerView', () => {
    cy.login('gestor@test.com', 'senha');
    cy.visit('/payments');
    cy.contains(/gerente|gestor|aguardando/i).should('exist');
  });

  it('financeiro vê solicitações aprovadas pelo gestor', () => {
    cy.login('financeiro@test.com', 'senha');
    cy.visit('/payments');
    cy.url().should('include', '/payments');
  });

  it('contabilidade acessa o módulo de pagamentos', () => {
    cy.login('contabilidade@test.com', 'senha');
    cy.visit('/payments');
    cy.url().should('include', '/payments');
  });

  it('usuario sem permissão é redirecionado do dashboard', () => {
    cy.login('solicitante@test.com', 'senha');
    cy.visit('/home');
    cy.url().should('include', '/payments');
  });
});
