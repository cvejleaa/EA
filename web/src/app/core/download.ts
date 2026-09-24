import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/**
 * Henter en fil fra API'et og gemmer den i browseren. Et almindeligt <a href> kan ikke bruges, fordi
 * det ikke sender login-tokenet med.
 */
export async function downloadFile(http: HttpClient, url: string, fallbackName: string): Promise<void> {
  const response = await firstValueFrom(http.get(url, { observe: 'response', responseType: 'blob' }));
  const name = fileNameFrom(response.headers.get('Content-Disposition')) ?? fallbackName;
  const href = URL.createObjectURL(response.body!);
  try {
    const link = document.createElement('a');
    link.href = href;
    link.download = name;
    document.body.appendChild(link);
    link.click();
    link.remove();
  } finally {
    URL.revokeObjectURL(href);
  }
}

/** Filnavnet fra en Content-Disposition-header (filename* foretrækkes). */
export function fileNameFrom(header: string | null): string | null {
  if (!header) {
    return null;
  }
  const star = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (star) {
    return decodeURIComponent(star[1].trim());
  }
  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain ? plain[1].trim() : null;
}
