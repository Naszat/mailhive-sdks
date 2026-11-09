package mailhive

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/url"
	"strings"
)

// Recipients is one or more email addresses. It is sent as a plain string
// when it holds exactly one address and as a list otherwise; the API
// accepts both. It also decodes from either form.
type Recipients []string

// MarshalJSON writes a single address as a string, and several as a list.
func (r Recipients) MarshalJSON() ([]byte, error) {
	if len(r) == 1 {
		return json.Marshal(r[0])
	}
	if r == nil {
		return []byte("[]"), nil
	}
	return json.Marshal([]string(r))
}

// UnmarshalJSON reads a string or a list of strings.
func (r *Recipients) UnmarshalJSON(data []byte) error {
	var one string
	if err := json.Unmarshal(data, &one); err == nil {
		*r = Recipients{one}
		return nil
	}
	var many []string
	if err := json.Unmarshal(data, &many); err != nil {
		return err
	}
	*r = many
	return nil
}

// Attachment is a file sent with an email.
type Attachment struct {
	// Filename is shown to the recipient.
	Filename string `json:"filename"`
	// Content is the file's bytes. They are base64-encoded for you.
	Content []byte `json:"content"`
	// ContentType defaults to application/octet-stream on the server.
	ContentType string `json:"content_type,omitempty"`
}

// SendEmailParams is one email. The JSON field names match the REST API
// (https://mailhive.africa/docs/api/send-email) exactly, and empty fields
// are left out.
type SendEmailParams struct {
	// From is the sender, at a verified sending domain:
	// "hello@example.com" or "Acme <hello@example.com>".
	From string `json:"from,omitempty"`
	// To is one recipient or several. At most 50 across To, Cc and Bcc.
	To  Recipients `json:"to,omitempty"`
	Cc  Recipients `json:"cc,omitempty"`
	Bcc Recipients `json:"bcc,omitempty"`
	// Subject is required unless TemplateID is set.
	Subject string `json:"subject,omitempty"`
	// HTML or Text (or both) is required unless TemplateID is set.
	HTML string `json:"html,omitempty"`
	Text string `json:"text,omitempty"`
	// TemplateID sends a template instead of Subject, HTML and Text.
	TemplateID string `json:"template_id,omitempty"`
	// Variables are the template's values: strings, numbers, booleans or nil.
	Variables   map[string]any    `json:"variables,omitempty"`
	ReplyTo     Recipients        `json:"reply_to,omitempty"`
	Headers     map[string]string `json:"headers,omitempty"`
	Tags        map[string]string `json:"tags,omitempty"`
	Attachments []Attachment      `json:"attachments,omitempty"`
}

// AcceptedEmail is the API's answer to a send.
type AcceptedEmail struct {
	ID string `json:"id"`
	// Status is "queued", or "suppressed" when every recipient is on the
	// suppression list.
	Status string `json:"status"`
	// Suppressed lists recipients dropped because they're on the
	// suppression list.
	Suppressed []string `json:"suppressed"`
	// Rejected maps recipients dropped before sending because the address
	// can't work (a mistyped, non-existent or mail-less domain) to the
	// reason: invalid_syntax, domain_typo, domain_not_found or
	// no_mail_server. They never reach the sending engine, so they can't
	// bounce. Empty when every recipient passed.
	Rejected map[string]string `json:"rejected"`
	// Test is true when sent with a test key (mhs_test_…): delivery is
	// simulated and nothing is billed.
	Test bool `json:"test"`
}

// RecipientVerdict is the API's answer about one address passed to
// Validate. Fields the API returns as null are empty strings.
type RecipientVerdict struct {
	// Email is the address as given.
	Email string `json:"email"`
	// Normalized is the bare, lowercased address, or empty when there
	// isn't one.
	Normalized string `json:"normalized"`
	// Valid is false when sending to this address would be refused.
	Valid bool `json:"valid"`
	// Reason is why it's invalid: invalid_syntax, domain_typo,
	// domain_not_found, no_mail_server or suppressed (on your suppression
	// list). Empty when valid.
	Reason string `json:"reason"`
	// Message is the reason in plain words, fit to show the person typing
	// the address.
	Message string `json:"message"`
	// Suggestion is, for domain_typo, the address that was probably meant
	// (ada@gmail.com for ada@gmial.com).
	Suggestion string `json:"suggestion"`
}

// Email is a sent email and its delivery status. Fields the API returns as
// null are left empty (or zero).
type Email struct {
	ID string `json:"id"`
	// Status is queued, sent, delivered, delayed, bounced, complained,
	// suppressed or failed.
	Status string `json:"status"`
	// Stream is transactional or broadcast.
	Stream     string   `json:"stream"`
	Test       bool     `json:"test"`
	From       string   `json:"from"`
	To         []string `json:"to"`
	Cc         []string `json:"cc"`
	Bcc        []string `json:"bcc"`
	Subject    string   `json:"subject"`
	Suppressed []string `json:"suppressed"`
	// Rejected maps recipients dropped before sending because the address
	// can't work to the reason; see AcceptedEmail.Rejected.
	Rejected   map[string]string `json:"rejected"`
	Tags       map[string]string `json:"tags"`
	TemplateID string            `json:"template_id"`
	// TemplateVersion is 0 when the email wasn't sent from a template.
	TemplateVersion int `json:"template_version"`
	// CreatedAt, SentAt and LastEventAt are ISO 8601 timestamps.
	CreatedAt   string `json:"created_at"`
	SentAt      string `json:"sent_at"`
	LastEventAt string `json:"last_event_at"`
}

// EmailsService sends and looks up emails. Use it as client.Emails.
type EmailsService struct {
	client *Client
}

// Send sends one email.
func (s *EmailsService) Send(ctx context.Context, params *SendEmailParams, opts ...RequestOption) (*AcceptedEmail, error) {
	if params == nil {
		return nil, errors.New("mailhive: Send needs the email to send")
	}
	var out AcceptedEmail
	if err := s.client.do(ctx, http.MethodPost, "/send/emails", params, requestOptions(opts).idempotencyKey, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// SendBatch sends up to 100 independent emails in one request. All are
// accepted, or none.
func (s *EmailsService) SendBatch(ctx context.Context, emails []SendEmailParams, opts ...RequestOption) ([]AcceptedEmail, error) {
	if emails == nil {
		emails = []SendEmailParams{}
	}
	body := struct {
		Emails []SendEmailParams `json:"emails"`
	}{emails}
	var out struct {
		Data []AcceptedEmail `json:"data"`
	}
	if err := s.client.do(ctx, http.MethodPost, "/send/emails/batch", body, requestOptions(opts).idempotencyKey, &out); err != nil {
		return nil, err
	}
	return out.Data, nil
}

// Get fetches an email and its delivery status.
func (s *EmailsService) Get(ctx context.Context, id string) (*Email, error) {
	if id == "" {
		return nil, errors.New("mailhive: Get needs an email id")
	}
	var out Email
	if err := s.client.do(ctx, http.MethodGet, "/send/emails/"+escapePathSegment(id), nil, "", &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// Validate runs the checks sending applies, without sending or storing
// anything: syntax, mistyped domains (gmial.com), domains that don't exist
// or have no mail server, and your suppression list. Call it while the
// person can still fix a typo, such as on a signup or checkout form. One
// verdict per address, in the order given. An unknown answer (a DNS
// timeout) counts as valid.
func (s *EmailsService) Validate(ctx context.Context, emails []string) ([]RecipientVerdict, error) {
	if len(emails) == 0 {
		return nil, errors.New("mailhive: Validate needs at least one address")
	}
	body := struct {
		Emails []string `json:"emails"`
	}{emails}
	var out struct {
		Data []RecipientVerdict `json:"data"`
	}
	if err := s.client.do(ctx, http.MethodPost, "/send/emails/validate", body, "", &out); err != nil {
		return nil, err
	}
	return out.Data, nil
}

// escapePathSegment percent-encodes everything except unreserved
// characters, like the other SDKs do.
func escapePathSegment(s string) string {
	return strings.ReplaceAll(url.QueryEscape(s), "+", "%20")
}
