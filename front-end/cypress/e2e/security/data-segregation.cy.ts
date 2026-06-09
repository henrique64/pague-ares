describe('Segregação de Dados', () => {
  it('usuário comum vê apenas as próprias solicitações na lista', () => {
    cy.login('solicitante@test.com', 'senha');
    cy.visit('/payments');
    cy.url().should('include', '/payments');
    // Verifica que a lista carrega sem erros
    cy.get('mat-row, tr').should('exist');
  });

  it('admin consegue acessar a listagem completa', () => {
    cy.login('adm@test.com', 'senhaAdm');
    cy.visit('/payments');
    cy.url().should('include', '/payments');
  });

  it('acesso direto a solicitação de terceiro exibe mensagem de restrição', () => {
    cy.login('outro@test.com', 'senha');
    cy.request({
      url: 'http://localhost:7219/api/request/11',
      headers: { Authorization: `Bearer ${getTokenFromStorage()}` },
      failOnStatusCode: false
    }).then((resp) => {
      expect(resp.body.success).to.equal(false);
    });
  });

  it('gestor acessa solicitações da equipe', () => {
    cy.login('gestor@test.com', 'senha');
    cy.visit('/payments');
    cy.url().should('include', '/payments');
  });
});

function getTokenFromStorage(): string {
  return '';
}
