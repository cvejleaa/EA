import { fileNameFrom } from './download';

describe('fileNameFrom', () => {
  it('foretrækker filename* (UTF-8)', () => {
    expect(fileNameFrom("attachment; filename=x.csv; filename*=UTF-8''integrationer-l%C3%B8n.csv")).toBe('integrationer-løn.csv');
  });

  it('bruger filename, når filename* mangler', () => {
    expect(fileNameFrom('attachment; filename="systemer-2026-09-24.csv"')).toBe('systemer-2026-09-24.csv');
  });

  it('giver null uden header', () => {
    expect(fileNameFrom(null)).toBeNull();
  });
});
