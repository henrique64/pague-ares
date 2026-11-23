declare namespace Cypress {
  interface Chainable {
    login(username: string, password: string): Chainable<void>;
  }
}

Cypress.Commands.add('login', (username: string, password: string) => {
  cy.request('POST', 'http://localhost:7219/api/auth', { username, password }).then((response) => {
    if (response.body.success && response.body.data?.token) {
      window.localStorage.setItem('Token', JSON.stringify(response.body.data));
    }
  });
});
