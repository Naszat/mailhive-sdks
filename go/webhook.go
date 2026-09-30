package mailhive

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"strconv"
	"strings"
	"time"
)

// DefaultWebhookTolerance is how far a webhook's timestamp may be from now.
const DefaultWebhookTolerance = 300 * time.Second

// WebhookErrorReason says why a webhook failed verification.
type WebhookErrorReason string

// The reasons a webhook can fail verification.
const (
	// ReasonHeader: the Mailhive-Signature header is missing or malformed.
	ReasonHeader WebhookErrorReason = "header"
	// ReasonTimestamp: the timestamp is outside the tolerance (a replay?).
	ReasonTimestamp WebhookErrorReason = "timestamp"
	// ReasonSignature: no signature matches (wrong secret, or the body
	// changed).
	ReasonSignature WebhookErrorReason = "signature"
)

// WebhookVerificationError means a webhook's signature didn't check out.
// Answer such a request with a 400.
type WebhookVerificationError struct {
	Reason  WebhookErrorReason
	Message string
}

func (e *WebhookVerificationError) Error() string { return "mailhive: " + e.Message }

// WebhookEvent is a verified webhook event.
type WebhookEvent struct {
	// Type is email.sent, email.delivered, email.delivery_delayed,
	// email.bounced, email.complained, email.opened, email.clicked or
	// contact.unsubscribed.
	Type string `json:"type"`
	// CreatedAt is an ISO 8601 timestamp.
	CreatedAt string `json:"created_at"`
	// Test is true on events that aren't real: test events sent from the
	// dashboard, and events for mail sent with a test key.
	Test bool             `json:"test"`
	Data WebhookEventData `json:"data"`
	// Raw is the whole event exactly as received, for fields this struct
	// doesn't cover.
	Raw json.RawMessage `json:"-"`
}

// WebhookEventData is an event's data. Email events fill the email fields;
// contact.unsubscribed fills Email and ContactID.
type WebhookEventData struct {
	EmailID   string            `json:"email_id,omitempty"`
	From      string            `json:"from,omitempty"`
	To        []string          `json:"to,omitempty"`
	Subject   string            `json:"subject,omitempty"`
	Tags      map[string]string `json:"tags,omitempty"`
	Recipient string            `json:"recipient,omitempty"`
	// Detail holds code, response and bounce_classification for bounces
	// and delays, and url for clicks.
	Detail map[string]any `json:"detail,omitempty"`

	Email     string `json:"email,omitempty"`
	ContactID string `json:"contact_id,omitempty"`
}

type webhookConfig struct {
	tolerance time.Duration
	now       time.Time
}

// WebhookOption configures VerifyWebhook.
type WebhookOption func(*webhookConfig)

// WithTolerance sets how far the webhook's timestamp may be from now
// (default 300 seconds).
func WithTolerance(d time.Duration) WebhookOption {
	return func(c *webhookConfig) { c.tolerance = d }
}

// WithNow verifies as if the current time were now. It is for tests.
func WithNow(now time.Time) WebhookOption {
	return func(c *webhookConfig) { c.now = now }
}

// VerifyWebhook checks a webhook's Mailhive-Signature header and returns
// the event. The header looks like t=<unix time>,v1=<hex>; the signature
// is HMAC-SHA256 of "<t>.<raw body>" keyed with the endpoint's signing
// secret. Several v1 values are accepted, so secrets can be rotated.
//
// payload must be the raw request body exactly as received: decoding and
// re-encoding it changes the bytes, and the signature won't match.
//
// A bad signature returns a *WebhookVerificationError whose Reason is
// ReasonHeader, ReasonTimestamp or ReasonSignature.
func VerifyWebhook(payload []byte, signatureHeader, secret string, opts ...WebhookOption) (*WebhookEvent, error) {
	cfg := webhookConfig{tolerance: DefaultWebhookTolerance}
	for _, opt := range opts {
		if opt != nil {
			opt(&cfg)
		}
	}
	if cfg.now.IsZero() {
		cfg.now = time.Now()
	}

	timestamp, candidates, ok := parseSignatureHeader(signatureHeader)
	if !ok {
		return nil, &WebhookVerificationError{Reason: ReasonHeader, Message: "Missing or malformed Mailhive-Signature header."}
	}
	age := cfg.now.Sub(time.Unix(timestamp, 0))
	if age < 0 {
		age = -age
	}
	if age > cfg.tolerance {
		return nil, &WebhookVerificationError{
			Reason:  ReasonTimestamp,
			Message: fmt.Sprintf("The webhook's timestamp is more than %s seconds from now; it may be a replay.", strconv.FormatFloat(cfg.tolerance.Seconds(), 'f', -1, 64)),
		}
	}

	mac := hmac.New(sha256.New, []byte(secret))
	mac.Write([]byte(strconv.FormatInt(timestamp, 10) + "."))
	mac.Write(payload)
	expected := []byte(hex.EncodeToString(mac.Sum(nil)))
	matched := false
	for _, candidate := range candidates {
		if hmac.Equal(expected, []byte(candidate)) {
			matched = true
		}
	}
	if !matched {
		return nil, &WebhookVerificationError{
			Reason:  ReasonSignature,
			Message: "The webhook's signature doesn't match. Check the endpoint's signing secret.",
		}
	}

	var event WebhookEvent
	if err := json.Unmarshal(payload, &event); err != nil {
		return nil, fmt.Errorf("mailhive: the webhook's signature is valid but its body isn't an event: %w", err)
	}
	event.Raw = append(json.RawMessage(nil), payload...)
	return &event, nil
}

// parseSignatureHeader reads "t=<unix>,v1=<hex>[,v1=<hex>…]". Unknown
// parts are ignored.
func parseSignatureHeader(header string) (timestamp int64, signatures []string, ok bool) {
	haveTimestamp := false
	for _, part := range strings.Split(header, ",") {
		key, value, found := strings.Cut(part, "=")
		if !found {
			continue
		}
		key, value = strings.TrimSpace(key), strings.TrimSpace(value)
		switch {
		case key == "t" && isDigits(value):
			if t, err := strconv.ParseInt(value, 10, 64); err == nil {
				timestamp, haveTimestamp = t, true
			}
		case key == "v1" && len(value) == 64 && isHex(value):
			signatures = append(signatures, strings.ToLower(value))
		}
	}
	return timestamp, signatures, haveTimestamp && len(signatures) > 0
}

func isDigits(s string) bool {
	if s == "" {
		return false
	}
	for i := 0; i < len(s); i++ {
		if s[i] < '0' || s[i] > '9' {
			return false
		}
	}
	return true
}

func isHex(s string) bool {
	for i := 0; i < len(s); i++ {
		c := s[i]
		if !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F') {
			return false
		}
	}
	return true
}
