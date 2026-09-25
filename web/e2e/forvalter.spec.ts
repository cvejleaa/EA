import { expect, test, type Page } from '@playwright/test';

/**
 * Delopgave 4 i den rigtige app: forvalteren (Frida, fiktiv) er systemforvalter på Nordlys ERP og Laborant i
 * eksempeldataene (DevSeed); Leo er læser. Hvert scenarie asserterer både det, der skal stå, og det, der IKKE må.
 */
async function loginAs(page: Page, name: RegExp): Promise<void> {
  await page.goto('/');
  await page.getByRole('button', { name }).click();
}

const frida = /^Frida Forvalter/;
const leo = /^Leo Læser/;

test('forvalteren lander på sine systemer med sin rolle', async ({ page }) => {
  await loginAs(page, frida);

  await expect(page).toHaveURL(/\/systemer\?mine=true$/);
  await expect(page.getByTestId('nav-mine')).toHaveText('Mine systemer (5)');
  await expect(page.getByTestId('mine-help')).toContainText('De ældst bekræftede står øverst');

  // Hele listen i serverens rækkefølge: ældst bekræftede først, samme tidspunkt efter navn (DevSeed er fast).
  // Præcis fem: Kompas Sag og de andre systemer uden en rolle til hende må ikke være med.
  await expect(page.locator('table.systems tbody tr td:first-child a')).toHaveText([
    'Nordlys ERP › Nordlys HR',
    'Laborant',
    'Nordlys ERP',
    'Nordlys ERP › Nordlys Økonomi',
    'Nordlys ERP › Nordlys Projekter',
  ]);
  await expect(page.getByTestId('my-role')).toHaveText([
    'Via Nordlys ERP',
    'Systemforvalter',
    'Systemforvalter',
    'Via Nordlys ERP',
    'Via Nordlys ERP',
  ]);
});

test('forvalteren retter et modul under sit system og ser, hvem der ellers kan', async ({ page }) => {
  await loginAs(page, frida);
  await page.getByRole('link', { name: 'Nordlys ERP › Nordlys HR' }).click();

  await expect(page.getByTestId('editors')).toHaveText('Redigeres af Frida Forvalter (fiktiv) (via Nordlys ERP).');
  await page.getByTestId('edit').click();

  // Forældre-listen tilbyder kun forældre, hun selv kan redigere.
  await page.getByRole('combobox', { name: 'Modul af' }).click();
  await expect(page.getByRole('option')).toHaveText(['Intet — selvstændigt system', 'Laborant', 'Nordlys ERP']);
  await page.keyboard.press('Escape');

  const text = `Rettet af forvalteren i E2E ${Date.now()}`;
  await page.getByLabel('Beskrivelse').fill(text);
  await page.getByRole('button', { name: 'Gem ændringer' }).click();

  await expect(page).toHaveURL(/\/systemer\/[0-9a-f-]+$/);
  await expect(page.getByText(text)).toBeVisible();
});

test('forvalteren kan ikke redigere et fremmed system og får at vide, hvem der kan', async ({ page }) => {
  await loginAs(page, frida);
  await page.getByTestId('nav-systems').click();
  await page.getByRole('link', { name: 'Kompas Sag' }).click();

  await expect(page.getByRole('heading', { name: 'Kompas Sag' })).toBeVisible();
  await expect(page.getByTestId('edit')).toHaveCount(0);
  await expect(page.getByTestId('editors')).toHaveText(
    'Systemet har ingen systemejer eller -forvalter. Kontakt enterprise arkitekten.',
  );
});

test('en læser har hverken "Mine systemer" eller "Rediger"', async ({ page }) => {
  await loginAs(page, leo);

  await expect(page).toHaveURL(/\/systemer$/);
  await expect(page.getByTestId('nav-mine')).toHaveCount(0);
  await page.getByRole('link', { name: 'Nordlys ERP', exact: true }).click();

  await expect(page.getByRole('heading', { name: 'Nordlys ERP' })).toBeVisible();
  await expect(page.getByTestId('edit')).toHaveCount(0);
  await expect(page.getByTestId('editors')).toHaveText('Ser noget forkert ud? Det rettes af Frida Forvalter (fiktiv).');
});
