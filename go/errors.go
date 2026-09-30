package mailhive

import (
	"encoding/json"
	"fmt"
	"net/http"
	"time"
)

// ErrorKind names a class of API error. The kinds are the same in every
// Mailhive SDK.
type ErrorKind string

// The error kinds, by HTTP status.
const (
	KindAPI            ErrorKind = "api"            // any other status
	KindAuthentication ErrorKind = "authentication" // 401
	KindBilling        ErrorKind = "billing"        // 402
	KindPermission     ErrorKind = "permission"     // 403
	KindNotFound       ErrorKind = "not_found"      // 404
	KindConflict       ErrorKind = "conflict"       // 409
	KindValidation     ErrorKind = "validation"     // 400, 422
	KindRateLimit      ErrorKind = "rate_limit"     // 429
	KindConnection     ErrorKind = "connection"     // no response (*ConnectionError)
)

// APIError is an error response from the API. Check Code, not Message:
// codes are stable, messages may change.
//
// Errors with a well-known status come wrapped in a more specific type
// (*AuthenticationError, *RateLimitError, …). errors.As finds the
// *APIError inside any of them.
type APIError struct {
	// Status is the HTTP status code.
	Status int
	// Code is Mailhive's stable error code, such as "validation_error" or
	// "monthly_quota_reached". It is "http_error" when the response wasn't
	// Mailhive JSON (a proxy's HTML 502, for example).
	Code string
	// Message is a human-readable explanation.
	Message string
	// Details is extra JSON, such as the list of problems in a validation
	// error. It is nil when the API sent none.
	Details json.RawMessage
	// RequestID identifies the request: quote it to support.
	RequestID string
	// Header holds the response's headers.
	Header http.Header
}

func (e *APIError) Error() string {
	s := fmt.Sprintf("mailhive: %s (status %d, code %s", e.Message, e.Status, e.Code)
	if e.RequestID != "" {
		s += ", request " + e.RequestID
	}
	return s + ")"
}

// Kind classifies the error by its status.
func (e *APIError) Kind() ErrorKind {
	switch e.Status {
	case http.StatusUnauthorized:
		return KindAuthentication
	case http.StatusPaymentRequired:
		return KindBilling
	case http.StatusForbidden:
		return KindPermission
	case http.StatusNotFound:
		return KindNotFound
	case http.StatusConflict:
		return KindConflict
	case http.StatusBadRequest, http.StatusUnprocessableEntity:
		return KindValidation
	case http.StatusTooManyRequests:
		return KindRateLimit
	default:
		return KindAPI
	}
}

// RetryAfter returns the response's Retry-After delay, if it sent one in
// seconds.
func (e *APIError) RetryAfter() (time.Duration, bool) { return retryAfter(e.Header) }

// AuthenticationError is a 401: the API key is missing, unknown or revoked.
type AuthenticationError struct{ *APIError }

// Unwrap returns the underlying *APIError.
func (e *AuthenticationError) Unwrap() error { return e.APIError }

// BillingError is a 402: Mailhive Send is paused over an unpaid invoice.
type BillingError struct{ *APIError }

// Unwrap returns the underlying *APIError.
func (e *BillingError) Unwrap() error { return e.APIError }

// PermissionError is a 403: Send isn't activated, or the stream is paused.
type PermissionError struct{ *APIError }

// Unwrap returns the underlying *APIError.
func (e *PermissionError) Unwrap() error { return e.APIError }

// NotFoundError is a 404.
type NotFoundError struct{ *APIError }

// Unwrap returns the underlying *APIError.
func (e *NotFoundError) Unwrap() error { return e.APIError }

// ConflictError is a 409, such as an Idempotency-Key reused for a
// different request (idempotency_conflict).
type ConflictError struct{ *APIError }

// Unwrap returns the underlying *APIError.
func (e *ConflictError) Unwrap() error { return e.APIError }

// ValidationError is a 400 or 422: the request is invalid. Details lists
// every problem.
type ValidationError struct{ *APIError }

// Unwrap returns the underlying *APIError.
func (e *ValidationError) Unwrap() error { return e.APIError }

// RateLimitError is a 429. Code "rate_limited" is retried for you, after
// RetryAfter(); "monthly_quota_reached" and "daily_cap_reached" are not,
// because waiting won't help.
type RateLimitError struct{ *APIError }

// Unwrap returns the underlying *APIError.
func (e *RateLimitError) Unwrap() error { return e.APIError }

// typedError wraps an *APIError in the type for its status.
func typedError(e *APIError) error {
	switch e.Kind() {
	case KindAuthentication:
		return &AuthenticationError{e}
	case KindBilling:
		return &BillingError{e}
	case KindPermission:
		return &PermissionError{e}
	case KindNotFound:
		return &NotFoundError{e}
	case KindConflict:
		return &ConflictError{e}
	case KindValidation:
		return &ValidationError{e}
	case KindRateLimit:
		return &RateLimitError{e}
	default:
		return e
	}
}

// ConnectionError means the API couldn't be reached, or didn't answer in
// time, after every retry.
type ConnectionError struct {
	// Message says what went wrong, for people.
	Message string
	// Timeout is true when the API didn't answer within the timeout.
	Timeout bool
	// Err is the underlying network error.
	Err error
}

func (e *ConnectionError) Error() string { return "mailhive: " + e.Message }

// Unwrap returns the underlying network error.
func (e *ConnectionError) Unwrap() error { return e.Err }

// Kind is always KindConnection.
func (e *ConnectionError) Kind() ErrorKind { return KindConnection }
