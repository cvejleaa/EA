import { HttpErrorResponse } from '@angular/common/http';

/** ProblemDetails-typen for en forældet version — den ENESTE konflikt, hvor "Hent nyeste version" giver mening. */
export const STALE_VERSION = 'urn:ea:problem:stale-version';

/** En tør-kørsel, der er forældet: data er ændret siden. Handlingen er en ny tør-kørsel (ikke "Hent nyeste version"). */
export const STALE_DRY_RUN = 'urn:ea:problem:stale-dry-run';

export interface ProblemInfo {
  status: number;
  /** ProblemDetails.type, fx STALE_VERSION. Skelner konflikter, der ellers alle er 409. */
  type?: string;
  /** Beskeden til brugeren (ProblemDetails.detail eller .title). */
  message: string;
  /** Feltfejl fra et ValidationProblem, nøglet på feltnavn (camelCase). */
  fieldErrors: Record<string, string[]>;
}

/** Oversætter et fejlsvar fra API'et til noget, brugeren kan læse og handle på. */
export function toProblem(error: unknown): ProblemInfo {
  if (error instanceof HttpErrorResponse) {
    const body = (error.error ?? {}) as {
      type?: string;
      detail?: string;
      title?: string;
      errors?: Record<string, string[]>;
    };
    if (error.status === 0) {
      return { status: 0, message: 'Kunne ikke kontakte serveren. Prøv igen.', fieldErrors: {} };
    }
    if (error.status === 403) {
      return { status: 403, message: 'Du har ikke adgang til at gøre dette.', fieldErrors: {} };
    }
    return {
      status: error.status,
      type: body.type,
      message: body.detail ?? body.title ?? `Uventet fejl (${error.status}).`,
      fieldErrors: body.errors ?? {},
    };
  }
  return { status: -1, message: 'Uventet fejl.', fieldErrors: {} };
}
