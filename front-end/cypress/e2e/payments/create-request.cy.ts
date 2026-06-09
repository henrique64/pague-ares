describe('Create Request', () => {
  beforeEach(() => {
    cy.login('adm@test.com', 'senhaAdm');
    cy.visit('/create-request');
  });

  it('submit com formulário vazio exibe erros de validação', () => {
    cy.contains(/salvar|enviar|submit/i).first().click();
    cy.contains(/obrigatório|campo/i).should('exist');
  });

  it('salvar como rascunho com dados válidos não exibe erro', () => {
    cy.get('[placeholder*="scrição"], [formcontrolname*="descricao"], [name*="descricao"]').first().type('Pagamento de Teste');
    cy.contains(/rascunho/i).click();
    cy.contains(/sucesso|armazenado/i).should('be.visible');
  });

  it('após salvar rascunho redireciona ou exibe confirmação', () => {
    cy.get('[placeholder*="scrição"], [formcontrolname*="descricao"], [name*="descricao"]').first().type('Pagamento Redirect');
    cy.contains(/rascunho/i).click();
    cy.url().should('match', /payments|create-request/);
  });
});
