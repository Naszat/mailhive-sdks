export { Mailhive, DEFAULT_BASE_URL, type MailhiveOptions } from "./client.js";
export { verifyWebhook, type VerifyWebhookOptions } from "./webhooks.js";
export {
  MailhiveError,
  ApiError,
  AuthenticationError,
  BillingError,
  PermissionError,
  NotFoundError,
  ConflictError,
  ValidationError,
  RateLimitError,
  ConnectionError,
  WebhookVerificationError,
} from "./errors.js";
export type * from "./types.js";
export { VERSION } from "./version.js";
