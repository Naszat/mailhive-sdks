// Package mailhive is the official Go SDK for Mailhive Send, the
// transactional and broadcast email API from Mailhive
// (https://mailhive.africa/docs/send/overview).
//
//	client, err := mailhive.New() // reads MAILHIVE_API_KEY
//	if err != nil {
//		log.Fatal(err)
//	}
//	email, err := client.Emails.Send(ctx, &mailhive.SendEmailParams{
//		From:    "Acme <hello@acme.com>",
//		To:      mailhive.Recipients{"ada@example.com"},
//		Subject: "Your receipt",
//		HTML:    "<p>Thanks for your order.</p>",
//	})
//
// Every send carries an Idempotency-Key, and retries reuse it, so a retry
// never sends twice. A Client is safe for concurrent use.
package mailhive

import (
	"bytes"
	"context"
	"crypto/rand"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math"
	mathrand "math/rand"
	"net"
	"net/http"
	"net/url"
	"os"
	"runtime"
	"strconv"
	"strings"
	"time"
)

const (
	// DefaultBaseURL is the production API.
	DefaultBaseURL = "https://api.mailhive.africa/v1"
	// DefaultTimeout is how long each attempt may take.
	DefaultTimeout = 30 * time.Second
	// DefaultMaxRetries is how many times a failed request is retried.
	DefaultMaxRetries = 2

	maxRetryAfter = 60 * time.Second
)

var (
	// ErrMissingAPIKey is returned by New when no key was given and
	// MAILHIVE_API_KEY is unset.
	ErrMissingAPIKey = errors.New("mailhive: no API key. Pass one with mailhive.WithAPIKey(...) or set MAILHIVE_API_KEY. Create keys under Mailhive Send → API keys")
	// ErrPublishableKey is returned by New for a form's publishable key
	// (mhp_…): the server SDK needs a secret key (mhs_…).
	ErrPublishableKey = errors.New("mailhive: that's a form's publishable key (mhp_…). The server SDK needs a secret API key (mhs_…) from Mailhive Send → API keys")
)

var userAgent = "mailhive-go/" + Version + " go/" + strings.TrimPrefix(runtime.Version(), "go")

// Client talks to the Mailhive Send API. Create one with New and reuse it:
// it is safe for concurrent use.
type Client struct {
	// Emails sends and looks up emails.
	Emails *EmailsService

	apiKey     string
	baseURL    string
	timeout    time.Duration
	maxRetries int
	httpClient *http.Client
}

type config struct {
	apiKey     string
	baseURL    string
	timeout    time.Duration
	maxRetries int
	httpClient *http.Client
}

// Option configures a Client.
type Option func(*config)

// WithAPIKey sets the secret API key (mhs_…). Without it, New reads
// MAILHIVE_API_KEY.
func WithAPIKey(key string) Option { return func(c *config) { c.apiKey = key } }

// WithBaseURL overrides the API's base URL, for example
// https://api-beta.mailhive.africa/v1. Without it, New reads
// MAILHIVE_BASE_URL, then uses DefaultBaseURL.
func WithBaseURL(baseURL string) Option { return func(c *config) { c.baseURL = baseURL } }

// WithTimeout sets how long each attempt may take (default 30s). Zero or
// less turns the per-attempt timeout off; the context still applies.
func WithTimeout(d time.Duration) Option { return func(c *config) { c.timeout = d } }

// WithMaxRetries sets how many times a failed request is retried (default
// 2). Zero turns retries off.
func WithMaxRetries(n int) Option {
	return func(c *config) {
		if n < 0 {
			n = 0
		}
		c.maxRetries = n
	}
}

// WithHTTPClient sets the *http.Client requests go through, for proxies,
// custom transports or tests.
func WithHTTPClient(hc *http.Client) Option { return func(c *config) { c.httpClient = hc } }

// New creates a Client. It fails without an API key, or when given a
// publishable key (mhp_…) instead of a secret one.
func New(opts ...Option) (*Client, error) {
	cfg := config{timeout: DefaultTimeout, maxRetries: DefaultMaxRetries}
	for _, opt := range opts {
		if opt != nil {
			opt(&cfg)
		}
	}
	if cfg.apiKey == "" {
		cfg.apiKey = os.Getenv("MAILHIVE_API_KEY")
	}
	if cfg.apiKey == "" {
		return nil, ErrMissingAPIKey
	}
	if strings.HasPrefix(cfg.apiKey, "mhp_") {
		return nil, ErrPublishableKey
	}
	if cfg.baseURL == "" {
		cfg.baseURL = os.Getenv("MAILHIVE_BASE_URL")
	}
	if cfg.baseURL == "" {
		cfg.baseURL = DefaultBaseURL
	}
	cfg.baseURL = strings.TrimRight(cfg.baseURL, "/")
	if u, err := url.Parse(cfg.baseURL); err != nil || (u.Scheme != "http" && u.Scheme != "https") || u.Host == "" {
		return nil, fmt.Errorf("mailhive: invalid base URL %q: it must look like https://api.mailhive.africa/v1", cfg.baseURL)
	}
	if cfg.httpClient == nil {
		cfg.httpClient = &http.Client{}
	}
	c := &Client{
		apiKey:     cfg.apiKey,
		baseURL:    cfg.baseURL,
		timeout:    cfg.timeout,
		maxRetries: cfg.maxRetries,
		httpClient: cfg.httpClient,
	}
	c.Emails = &EmailsService{client: c}
	return c, nil
}

// BaseURL returns the API base URL the client sends to.
func (c *Client) BaseURL() string { return c.baseURL }

// String describes the client without its API key.
func (c Client) String() string { return fmt.Sprintf("mailhive.Client{BaseURL: %q}", c.baseURL) }

// GoString describes the client without its API key, for %#v.
func (c Client) GoString() string { return c.String() }

// Format keeps the API key out of every fmt verb, not just %v and %s.
func (c Client) Format(f fmt.State, verb rune) { _, _ = io.WriteString(f, c.String()) }

// RequestOption configures a single API call.
type RequestOption func(*requestConfig)

type requestConfig struct {
	idempotencyKey string
}

// WithIdempotencyKey sets the call's Idempotency-Key. Without it, the SDK
// generates one per call and reuses it on every retry. Pass your own (for
// example "order-1042-receipt") to stay safe across process restarts too.
func WithIdempotencyKey(key string) RequestOption {
	return func(r *requestConfig) { r.idempotencyKey = key }
}

func requestOptions(opts []RequestOption) requestConfig {
	var r requestConfig
	for _, opt := range opts {
		if opt != nil {
			opt(&r)
		}
	}
	return r
}

// transportError is a request that got no HTTP response.
type transportError struct {
	err      error
	timedOut bool
}

// do sends a request, retrying what is worth retrying, and decodes a
// successful response into out.
func (c *Client) do(ctx context.Context, method, path string, body any, idempotencyKey string, out any) error {
	if ctx == nil {
		ctx = context.Background()
	}
	var payload []byte
	if body != nil {
		var err error
		if payload, err = json.Marshal(body); err != nil {
			return fmt.Errorf("mailhive: couldn't encode the request: %w", err)
		}
	}
	header := http.Header{}
	header.Set("Authorization", "Bearer "+c.apiKey)
	header.Set("Accept", "application/json")
	header.Set("User-Agent", userAgent)
	if payload != nil {
		header.Set("Content-Type", "application/json")
	}
	if method == http.MethodPost {
		// One key per call, reused on every retry: a retry never sends twice.
		if idempotencyKey == "" {
			idempotencyKey = newUUID()
		}
		header.Set("Idempotency-Key", idempotencyKey)
	}
	target := c.baseURL + path

	for attempt := 0; ; attempt++ {
		resp, respBody, terr, err := c.attempt(ctx, method, target, header, payload)
		if err != nil {
			return err
		}
		if terr != nil {
			if ctx.Err() != nil {
				return fmt.Errorf("mailhive: %w", ctx.Err())
			}
			if attempt < c.maxRetries {
				if err := sleep(ctx, backoff(attempt)); err != nil {
					return fmt.Errorf("mailhive: %w", err)
				}
				continue
			}
			return c.connectionError(terr)
		}
		if resp.StatusCode >= 200 && resp.StatusCode < 300 {
			if out == nil {
				return nil
			}
			if err := json.Unmarshal(respBody, out); err != nil {
				return fmt.Errorf("mailhive: couldn't decode the API's response: %w", err)
			}
			return nil
		}
		apiErr := parseError(resp, respBody)
		wait, retry := c.waitBeforeRetry(apiErr, attempt)
		if !retry {
			return typedError(apiErr)
		}
		if err := sleep(ctx, wait); err != nil {
			return fmt.Errorf("mailhive: %w", err)
		}
	}
}

// attempt makes one HTTP request and reads the whole response within the
// per-attempt timeout. A failure to get a response is a *transportError;
// err is for failures that retrying can't fix.
func (c *Client) attempt(ctx context.Context, method, target string, header http.Header, payload []byte) (*http.Response, []byte, *transportError, error) {
	attemptCtx, cancel := ctx, context.CancelFunc(func() {})
	if c.timeout > 0 {
		attemptCtx, cancel = context.WithTimeout(ctx, c.timeout)
	}
	defer cancel()

	var body io.Reader
	if payload != nil {
		body = bytes.NewReader(payload)
	}
	req, err := http.NewRequestWithContext(attemptCtx, method, target, body)
	if err != nil {
		return nil, nil, nil, fmt.Errorf("mailhive: couldn't build the request: %w", err)
	}
	req.Header = header.Clone()

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, nil, &transportError{err: err, timedOut: timedOut(attemptCtx, err)}, nil
	}
	defer resp.Body.Close()
	respBody, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, nil, &transportError{err: err, timedOut: timedOut(attemptCtx, err)}, nil
	}
	return resp, respBody, nil, nil
}

func timedOut(attemptCtx context.Context, err error) bool {
	if errors.Is(attemptCtx.Err(), context.DeadlineExceeded) {
		return true
	}
	var netErr net.Error
	return errors.As(err, &netErr) && netErr.Timeout()
}

func (c *Client) connectionError(terr *transportError) *ConnectionError {
	message := "Couldn't reach the Mailhive API at " + c.baseURL + "."
	if terr.timedOut {
		if c.timeout > 0 {
			message = "The Mailhive API didn't answer within " + strconv.FormatFloat(c.timeout.Seconds(), 'f', -1, 64) + " seconds."
		} else {
			message = "The Mailhive API didn't answer in time."
		}
	}
	return &ConnectionError{Message: message, Timeout: terr.timedOut, Err: terr.err}
}

// waitBeforeRetry says how long to wait before retrying, or false to give
// up. Only a rate limit and server errors are worth retrying: a used-up
// allowance or a bad request fails the same way again.
func (c *Client) waitBeforeRetry(e *APIError, attempt int) (time.Duration, bool) {
	if attempt >= c.maxRetries {
		return 0, false
	}
	if !(e.Status >= 500 || (e.Status == http.StatusTooManyRequests && e.Code == "rate_limited")) {
		return 0, false
	}
	if wait, ok := e.RetryAfter(); ok {
		if wait > maxRetryAfter {
			return 0, false
		}
		return wait, true
	}
	return backoff(attempt), true
}

// parseError reads an error response: {"error": {"code", "message",
// "details", "request_id"}}, or anything else (an HTML 502 from a proxy).
func parseError(resp *http.Response, body []byte) *APIError {
	e := &APIError{
		Status:  resp.StatusCode,
		Code:    "http_error",
		Message: "HTTP " + strconv.Itoa(resp.StatusCode),
		Header:  resp.Header,
	}
	var envelope struct {
		Error map[string]json.RawMessage `json:"error"`
	}
	if json.Unmarshal(body, &envelope) == nil && envelope.Error != nil {
		str := func(key string) string {
			var s string
			if raw, ok := envelope.Error[key]; ok && json.Unmarshal(raw, &s) == nil {
				return s
			}
			return ""
		}
		if s := str("code"); s != "" {
			e.Code = s
		}
		if s := str("message"); s != "" {
			e.Message = s
		}
		e.RequestID = str("request_id")
		if raw, ok := envelope.Error["details"]; ok && len(raw) > 0 && string(raw) != "null" {
			e.Details = raw
		}
	}
	if e.RequestID == "" {
		e.RequestID = resp.Header.Get("X-Request-Id")
	}
	return e
}

// retryAfter parses a Retry-After header given in seconds.
func retryAfter(header http.Header) (time.Duration, bool) {
	value := strings.TrimSpace(header.Get("Retry-After"))
	if value == "" {
		return 0, false
	}
	seconds, err := strconv.ParseFloat(value, 64)
	if err != nil || math.IsNaN(seconds) || seconds < 0 || seconds > 1e9 {
		return 0, false
	}
	return time.Duration(seconds * float64(time.Second)), true
}

// backoff is about 0.5s, 1s, 2s … up to 8s, with jitter: uniform in
// [ceiling/2, ceiling].
func backoff(attempt int) time.Duration {
	ceiling := 8.0
	if attempt < 4 {
		ceiling = 0.5 * float64(int(1)<<attempt)
	}
	seconds := ceiling/2 + mathrand.Float64()*ceiling/2
	return time.Duration(seconds * float64(time.Second))
}

func sleep(ctx context.Context, d time.Duration) error {
	if d <= 0 {
		return ctx.Err()
	}
	timer := time.NewTimer(d)
	defer timer.Stop()
	select {
	case <-ctx.Done():
		return ctx.Err()
	case <-timer.C:
		return nil
	}
}

// newUUID returns a random (version 4) UUID.
func newUUID() string {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		panic("mailhive: crypto/rand failed: " + err.Error())
	}
	b[6] = (b[6] & 0x0f) | 0x40
	b[8] = (b[8] & 0x3f) | 0x80
	return fmt.Sprintf("%x-%x-%x-%x-%x", b[0:4], b[4:6], b[6:8], b[8:10], b[10:16])
}
