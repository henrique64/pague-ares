describe('Login', () => {
  beforeEach(() => {
    cy.visit('/login');
  });

  it('exibe formulário de login', () => {
    cy.get('input[type="text"], input[name="username"], input[placeholder*="usu"]').should('exist');
    cy.get('input[type="password"]').should('exist');
  });

  it('credenciais inválidas exibem mensagem de erro', () => {
    cy.get('input[type="text"], input[name="username"]').first().type('usuario@invalido.com');
    cy.get('input[type="password"]').type('senhaerrada');
    cy.get('button[type="submit"], button').contains(/entrar|login/i).click();
    cy.contains(/inválidos|incorretos|erro/i).should('be.visible');
  });

  it('login bem-sucedido redireciona para /payments', () => {
    cy.get('input[type="text"], input[name="username"]').first().type('adm@test.com');
    cy.get('input[type="password"]').type('senhaAdm');
    cy.get('button[type="submit"], button').contains(/entrar|login/i).click();
    cy.url().should('include', '/payments');
  });

  it('campos vazios impedem o envio do formulário', () => {
    cy.get('button[type="submit"], button').contains(/entrar|login/i).click();
    cy.contains(/obrigatório/i).should('exist');
  });
});
