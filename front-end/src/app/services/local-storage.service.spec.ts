import { TestBed } from '@angular/core/testing';
import { LocalStorageService } from './local-storage.service';

describe('LocalStorageService', () => {
  let service: LocalStorageService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(LocalStorageService);
    localStorage.clear();
  });

  it('set armazena valor e get recupera o mesmo valor', () => {
    service.set('chave', 'valor');
    expect(service.get('chave')).toBe('valor');
  });

  it('set com objeto serializa e get desserializa corretamente', () => {
    const obj = { id: 1, nome: 'teste' };
    service.set('obj', obj);
    expect(service.get('obj')).toEqual(obj);
  });

  it('remove exclui a chave do armazenamento', () => {
    service.set('chave', 'valor');
    service.remove('chave');
    expect(service.get('chave')).toBeNull();
  });

  it('get retorna null para chave inexistente', () => {
    expect(service.get('nao-existe')).toBeNull();
  });
});
