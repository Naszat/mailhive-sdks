package mailhive

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/url"
	"strconv"
)

// Audience is a list of contacts that broadcasts go to.
type Audience struct {
	ID        string `json:"id"`
	Name      string `json:"name"`
	CreatedAt string `json:"created_at"`
	// Contacts is everyone in the audience; Mailable is how many a broadcast
	// would reach.
	Contacts int `json:"contacts"`
	Mailable int `json:"mailable"`
}

// DoubleOptIn emails the person a confirmation link first: they're
// subscribed only if they click it. Until then they're "pending" and never
// mailed.
type DoubleOptIn struct {
	// From is on a verified sending domain: "Acme <news@example.com>".
	From string `json:"from"`
	// Subject defaults to "Please confirm your subscription".
	Subject string `json:"subject,omitempty"`
}

// ContactInput is a person to add or update. The JSON field names match the
// REST API (https://mailhive.africa/docs/api/contacts).
type ContactInput struct {
	Email string `json:"email"`
	// FirstName and LastName: nil keeps the current value, a pointer to ""
	// clears it. Use String to make a pointer.
	FirstName *string `json:"first_name,omitempty"`
	LastName  *string `json:"last_name,omitempty"`
	// Fields are merged into the contact's custom fields; an empty value
	// removes one.
	Fields map[string]string `json:"fields,omitempty"`
	// Status is "subscribed" or "unsubscribed". New contacts default to
	// subscribed. Someone who unsubscribed or reported spam can't be set to
	// subscribed: use DoubleOptIn.
	Status      string       `json:"status,omitempty"`
	DoubleOptIn *DoubleOptIn `json:"double_opt_in,omitempty"`
}

// ContactUpdate changes an existing contact.
type ContactUpdate struct {
	// FirstName and LastName: nil keeps the current value, a pointer to ""
	// clears it.
	FirstName *string
	LastName  *string
	// Fields replaces the contact's custom fields: a nil map leaves them as
	// they are, and an empty one removes them all. Use Upsert to merge.
	Fields map[string]string
	// Unsubscribe unsubscribes the contact. Only they can undo it, by
	// confirming a double opt-in email.
	Unsubscribe bool
}

// MarshalJSON leaves out what isn't set, but sends an empty (non-nil) Fields
// as {}.
func (u ContactUpdate) MarshalJSON() ([]byte, error) {
	m := map[string]any{}
	if u.FirstName != nil {
		m["first_name"] = *u.FirstName
	}
	if u.LastName != nil {
		m["last_name"] = *u.LastName
	}
	if u.Fields != nil {
		m["fields"] = u.Fields
	}
	if u.Unsubscribe {
		m["unsubscribe"] = true
	}
	return json.Marshal(m)
}

// String returns a pointer to s, for the optional name fields.
func String(s string) *string { return &s }

// Contact is a person in an audience. Fields the API returns as null are
// left empty.
type Contact struct {
	ID         string            `json:"id"`
	AudienceID string            `json:"audience_id"`
	Email      string            `json:"email"`
	FirstName  string            `json:"first_name"`
	LastName   string            `json:"last_name"`
	Fields     map[string]string `json:"fields"`
	// Status is "subscribed", "unsubscribed" or "pending".
	Status string `json:"status"`
	// Suppressed means on the suppression list (bounce, spam complaint or
	// unsubscribe): never mailed. Mailable is subscribed and not suppressed.
	Suppressed bool `json:"suppressed"`
	Mailable   bool `json:"mailable"`
	// Source is manual, import, api or form.
	Source         string `json:"source"`
	CreatedAt      string `json:"created_at"`
	UnsubscribedAt string `json:"unsubscribed_at"`
	// Test is true when a test key made the call: nothing was saved or sent.
	Test bool `json:"test"`
}

// ContactDetail is a contact with the latest emails sent to them.
type ContactDetail struct {
	Contact
	History []Email `json:"history"`
}

// ContactPage is one page of contacts.
type ContactPage struct {
	Items    []Contact `json:"items"`
	Page     int       `json:"page"`
	PageSize int       `json:"page_size"`
	Total    int       `json:"total"`
}

// ListContactsParams filters a list. Zero values are left out.
type ListContactsParams struct {
	// Email finds one person by exact address (case-insensitive).
	Email string
	// Q searches names and addresses.
	Q string
	// Status is subscribed, unsubscribed or pending.
	Status string
	// Page starts at 1; PageSize is up to 100 (default 20).
	Page     int
	PageSize int
}

// ContactBatchError says why one entry of a batch failed.
type ContactBatchError struct {
	Code    string `json:"code"`
	Message string `json:"message"`
}

// ContactBatchEntry is the result for one contact of a batch.
type ContactBatchEntry struct {
	Index   int                `json:"index"`
	Email   string             `json:"email"`
	OK      bool               `json:"ok"`
	Created bool               `json:"created"`
	Contact *Contact           `json:"contact"`
	Error   *ContactBatchError `json:"error"`
}

// ContactBatchResult has one entry per contact sent, in order. A failure in
// an entry isn't returned as an error: check OK.
type ContactBatchResult struct {
	Created int                 `json:"created"`
	Updated int                 `json:"updated"`
	Failed  int                 `json:"failed"`
	Results []ContactBatchEntry `json:"results"`
}

// AudiencesService manages audiences. It needs an API key with Full
// access. Use it as client.Audiences.
type AudiencesService struct {
	client *Client
}

// List returns the organization's audiences.
func (s *AudiencesService) List(ctx context.Context) ([]Audience, error) {
	var out []Audience
	if err := s.client.do(ctx, http.MethodGet, "/send/audiences", nil, "", &out); err != nil {
		return nil, err
	}
	return out, nil
}

// Create makes an audience.
func (s *AudiencesService) Create(ctx context.Context, name string, opts ...RequestOption) (*Audience, error) {
	var out Audience
	body := map[string]string{"name": name}
	if err := s.client.do(ctx, http.MethodPost, "/send/audiences", body, requestOptions(opts).idempotencyKey, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// Rename changes an audience's name.
func (s *AudiencesService) Rename(ctx context.Context, id, name string) (*Audience, error) {
	if id == "" {
		return nil, errors.New("mailhive: Rename needs an audience id")
	}
	var out Audience
	if err := s.client.do(ctx, http.MethodPatch, "/send/audiences/"+escapePathSegment(id), map[string]string{"name": name}, "", &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// ContactsService manages the people in an audience. It needs an API key
// with Full access. Use it as client.Contacts.
//
// Someone who unsubscribed, or reported your email as spam, can't be made
// subscribed again by these calls: you get a *ConflictError with code
// "contact_unsubscribed". Set DoubleOptIn to email them a confirmation link
// instead.
type ContactsService struct {
	client *Client
}

func audiencePath(id string) string { return "/send/audiences/" + escapePathSegment(id) + "/contacts" }
func contactPath(id string) string  { return "/send/contacts/" + escapePathSegment(id) }

// List returns a page of contacts. Pass Email to find one person.
func (s *ContactsService) List(ctx context.Context, audienceID string, params *ListContactsParams) (*ContactPage, error) {
	if audienceID == "" {
		return nil, errors.New("mailhive: List needs an audience id")
	}
	path := audiencePath(audienceID)
	if params != nil {
		query := url.Values{}
		if params.Email != "" {
			query.Set("email", params.Email)
		}
		if params.Q != "" {
			query.Set("q", params.Q)
		}
		if params.Status != "" {
			query.Set("status", params.Status)
		}
		if params.Page > 0 {
			query.Set("page", strconv.Itoa(params.Page))
		}
		if params.PageSize > 0 {
			query.Set("page_size", strconv.Itoa(params.PageSize))
		}
		if len(query) > 0 {
			path += "?" + query.Encode()
		}
	}
	var out ContactPage
	if err := s.client.do(ctx, http.MethodGet, path, nil, "", &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// Add adds one person. It fails with code "contact_exists" if they're
// already in the audience: use Upsert.
func (s *ContactsService) Add(ctx context.Context, audienceID string, contact *ContactInput, opts ...RequestOption) (*Contact, error) {
	if audienceID == "" || contact == nil {
		return nil, errors.New("mailhive: Add needs an audience id and a contact")
	}
	var out Contact
	if err := s.client.do(ctx, http.MethodPost, audiencePath(audienceID), contact, requestOptions(opts).idempotencyKey, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// Upsert adds the person, or updates them if they're already there. It is
// safe to repeat.
func (s *ContactsService) Upsert(ctx context.Context, audienceID string, contact *ContactInput) (*Contact, error) {
	if audienceID == "" || contact == nil {
		return nil, errors.New("mailhive: Upsert needs an audience id and a contact")
	}
	var out Contact
	if err := s.client.do(ctx, http.MethodPut, audiencePath(audienceID), contact, "", &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// UpsertBatch adds or updates up to 100 people. A bad entry doesn't fail the
// call: check each result's OK.
func (s *ContactsService) UpsertBatch(ctx context.Context, audienceID string, contacts []ContactInput, opts ...RequestOption) (*ContactBatchResult, error) {
	if audienceID == "" {
		return nil, errors.New("mailhive: UpsertBatch needs an audience id")
	}
	if contacts == nil {
		contacts = []ContactInput{}
	}
	body := struct {
		Contacts []ContactInput `json:"contacts"`
	}{contacts}
	var out ContactBatchResult
	if err := s.client.do(ctx, http.MethodPost, audiencePath(audienceID)+"/batch", body, requestOptions(opts).idempotencyKey, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// Get returns a contact and the latest emails sent to them.
func (s *ContactsService) Get(ctx context.Context, id string) (*ContactDetail, error) {
	if id == "" {
		return nil, errors.New("mailhive: Get needs a contact id")
	}
	var out ContactDetail
	if err := s.client.do(ctx, http.MethodGet, contactPath(id), nil, "", &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// Update edits a contact's names and fields (fields are replaced, not
// merged: use Upsert to merge), or unsubscribes them.
func (s *ContactsService) Update(ctx context.Context, id string, changes ContactUpdate) (*Contact, error) {
	if id == "" {
		return nil, errors.New("mailhive: Update needs a contact id")
	}
	var out Contact
	if err := s.client.do(ctx, http.MethodPatch, contactPath(id), changes, "", &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// Unsubscribe unsubscribes a contact. Only they can undo it, by confirming
// a double opt-in email.
func (s *ContactsService) Unsubscribe(ctx context.Context, id string) (*Contact, error) {
	return s.Update(ctx, id, ContactUpdate{Unsubscribe: true})
}

// Delete removes a contact. Deleting someone who unsubscribed keeps their
// address suppressed.
func (s *ContactsService) Delete(ctx context.Context, id string) error {
	if id == "" {
		return errors.New("mailhive: Delete needs a contact id")
	}
	return s.client.do(ctx, http.MethodDelete, contactPath(id), nil, "", nil)
}
