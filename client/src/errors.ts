export interface FieldError {
  /** The field's name, or null for a problem with the whole submission. */
  field: string | null;
  message: string;
}

/** Why a submission didn't go through. `code` is stable; `message` is
 * written for people and safe to show. */
export class FormError extends Error {
  override name = "FormError";
  readonly code: string;
  readonly status: number;
  readonly fieldErrors: FieldError[];
  readonly requestId: string | null;
  /** Seconds to wait before trying again, when rate limited. */
  readonly retryAfter: number | null;

  constructor(init: {
    code: string;
    message: string;
    status: number;
    fieldErrors?: FieldError[];
    requestId?: string | null;
    retryAfter?: number | null;
  }) {
    super(init.message);
    this.code = init.code;
    this.status = init.status;
    this.fieldErrors = init.fieldErrors ?? [];
    this.requestId = init.requestId ?? null;
    this.retryAfter = init.retryAfter ?? null;
  }
}
