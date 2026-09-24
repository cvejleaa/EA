import { HttpErrorResponse } from '@angular/common/http';

export interface ProblemInfo {
  status: number;
  /** Beskeden til brugeren (ProblemDetails.detail eller .title). */
  message: string;
  /** Feltfejl fra et ValidationProblem, nøglet på feltnavn (camelCase). */
  fieldErrors: Record<string, string[]>;
}

/** Oversætter et fejlsvar fra API'et til noget, brugeren kan læse og handle på. */
export function toProblem(error: unknown): ProblemInfo {
  if (error instanceof HttpErrorResponse) {
    const body = (error.error ?? {}) as { detail?: string; title?: string; errors?: Record<string, string[]> };
    if (error.status === 0) {
      return { status: 0, message: 'Kunne ikke kontakte serveren. Prøv igen.', fieldErrors: {} };
    }
    if (error.status === 403) {
      return { status: 403, message: 'Du har ikke adgang til at gøre dette.', fieldErrors: {} };
    }
    return {
      status: error.status,
      message: body.detail ?? body.title ?? `Uventet fejl (${error.status}).`,
      fieldErrors: body.errors ?? {},
    };
  }
  return { status: -1, message: 'Uventet fejl.', fieldErrors: {} };
}
