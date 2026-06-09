describe('Create RDDV', () => {
  beforeEach(() => {
    cy.login('adm@test.com', 'senhaAdm');
    cy.visit('/create-rddv');
  });

  it('CPF inválido exibe erro de validação', () => {
    cy.get('[placeholder*="CPF"], [formcontrolname*="cpf"], [name*="cpf"]').first().type('111.111.111-11');
    cy.contains(/rascunho/i).click();
    cy.contains(/inválido/i).should('be.visible');
  });

  it('adicionar despesa aumenta a linha da tabela', () => {
    cy.contains(/despesa|adicionar/i).first().click();
    cy.get('table tbody tr, mat-row').should('have.length.greaterThan', 0);
  });

  it('salvar rascunho com campos obrigatórios exige preenchimento', () => {
    cy.contains(/rascunho/i).click();
    cy.contains(/obrigatório/i).should('exist');
  });
});
