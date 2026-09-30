package mailhive_test

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"regexp"
	"strings"
	"sync/atomic"
	"testing"
	"time"

	mailhive "github.com/Naszat/mailhive-sdks/go"
)

func TestReadsKeyAndBaseURLFromTheEnvironment(t *testing.T) {
	var auth atomic.Value
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		auth.Store(r.Header.Get("Authorization") + " " + r.URL.Path)
		_, _ = io.WriteString(w, `{"id":"msg_1","status":"queued","suppressed":[],"test":true}`)
	}))
	defer server.Close()
	t.Setenv("MAILHIVE_API_KEY", "mhs_test_env")
	t.Setenv("MAILHIVE_BASE_URL", server.URL+"/v1/")

	client, err := mailhive.New()
	if err != nil {
		t.Fatal(err)
	}
	if client.BaseURL() != server.URL+"/v1" {
		t.Fatalf("base URL = %q", client.BaseURL())
	}
	email, err := client.Emails.Send(context.Background(), &mailhive.SendEmailParams{From: "a@b.co", To: mailhive.Recipients{"c@d.co"}, Text: "x"})
	if err != nil {
		t.Fatal(err)
	}
	if !email.Test || auth.Load() != "Bearer mhs_test_env /v1/send/emails" {
		t.Fatalf("email = %+v, request = %v", email, auth.Load())
	}
}

func TestDefaultBaseURL(t *testing.T) {
	t.Setenv("MAILHIVE_BASE_URL", "")
	client, err := mailhive.New(mailhive.WithAPIKey("mhs_x"))
	if err != nil {
		t.Fatal(err)
	}
	if client.BaseURL() != mailhive.DefaultBaseURL || mailhive.DefaultBaseURL != "https://api.mailhive.africa/v1" {
		t.Fatalf("base URL = %q", client.BaseURL())
	}
}

func TestRequiresAKey(t *testing.T) {
	t.Setenv("MAILHIVE_API_KEY", "")
	if _, err := mailhive.New(); !errors.Is(err, mailhive.ErrMissingAPIKey) {
		t.Fatalf("error = %v", err)
	}
}

func TestRefusesAPublishableKey(t *testing.T) {
	if _, err := mailhive.New(mailhive.WithAPIKey("mhp_abc")); !errors.Is(err, mailhive.ErrPublishableKey) {
		t.Fatalf("error = %v", err)
	}
}

func TestRefusesAnInvalidBaseURL(t *testing.T) {
	if _, err := mailhive.New(mailhive.WithAPIKey("mhs_x"), mailhive.WithBaseURL("api.mailhive.africa")); err == nil {
		t.Fatal("expected an error")
	}
}

func TestNeverPrintsTheKey(t *testing.T) {
	client, err := mailhive.New(mailhive.WithAPIKey("mhs_supersecret"), mailhive.WithBaseURL("https://example.com/v1"))
	if err != nil {
		t.Fatal(err)
	}
	for _, format := range []string{"%v", "%+v", "%#v", "%s", "%q", "%x", "%d"} {
		for _, value := range []any{client, *client} {
			out := fmt.Sprintf(format, value)
			if strings.Contains(out, "supersecret") || strings.Contains(out, hexOf("supersecret")) {
				t.Fatalf("%s printed the key: %s", format, out)
			}
			if !strings.Contains(out, "example.com") {
				t.Fatalf("%s = %s, want the base URL", format, out)
			}
		}
	}
}

func hexOf(s string) string { return fmt.Sprintf("%x", s) }

func TestVersion(t *testing.T) {
	if !regexp.MustCompile(`^\d+\.\d+\.\d+$`).MatchString(mailhive.Version) {
		t.Fatalf("Version = %q", mailhive.Version)
	}
}

func TestEncodesAttachmentsAndRecipients(t *testing.T) {
	var body map[string]any
	var header http.Header
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		header = r.Header
		_ = json.NewDecoder(r.Body).Decode(&body)
		_, _ = io.WriteString(w, `{"id":"msg_1","status":"queued","suppressed":[],"test":false}`)
	}))
	defer server.Close()
	client, _ := mailhive.New(mailhive.WithAPIKey("mhs_x"), mailhive.WithBaseURL(server.URL))
	_, err := client.Emails.Send(context.Background(), &mailhive.SendEmailParams{
		From:        "a@b.co",
		To:          mailhive.Recipients{"c@d.co", "e@f.co"},
		ReplyTo:     mailhive.Recipients{"r@b.co"},
		TemplateID:  "tpl_1",
		Variables:   map[string]any{"n": 1},
		Attachments: []mailhive.Attachment{{Filename: "a.txt", Content: []byte("hello"), ContentType: "text/plain"}},
	}, mailhive.WithIdempotencyKey("mine"))
	if err != nil {
		t.Fatal(err)
	}
	want := map[string]any{
		"from":        "a@b.co",
		"to":          []any{"c@d.co", "e@f.co"},
		"reply_to":    "r@b.co",
		"template_id": "tpl_1",
		"variables":   map[string]any{"n": float64(1)},
		"attachments": []any{map[string]any{"filename": "a.txt", "content": base64.StdEncoding.EncodeToString([]byte("hello")), "content_type": "text/plain"}},
	}
	if got, _ := json.Marshal(body); string(got) != mustJSON(want) {
		t.Fatalf("body = %s, want %s", got, mustJSON(want))
	}
	if header.Get("Idempotency-Key") != "mine" || header.Get("Content-Type") != "application/json" || header.Get("Accept") != "application/json" {
		t.Fatalf("headers = %v", header)
	}
	if !regexp.MustCompile(`^mailhive-go/\d+\.\d+\.\d+ go/`).MatchString(header.Get("User-Agent")) {
		t.Fatalf("User-Agent = %q", header.Get("User-Agent"))
	}
}

func mustJSON(v any) string {
	data, _ := json.Marshal(v)
	return string(data)
}

func TestRecipientsJSON(t *testing.T) {
	for _, c := range []struct {
		in   mailhive.Recipients
		want string
	}{
		{mailhive.Recipients{"a@b.co"}, `"a@b.co"`},
		{mailhive.Recipients{"a@b.co", "c@d.co"}, `["a@b.co","c@d.co"]`},
		{mailhive.Recipients{}, `[]`},
	} {
		if got := mustJSON(c.in); got != c.want {
			t.Fatalf("marshal %v = %s, want %s", c.in, got, c.want)
		}
	}
	var r mailhive.Recipients
	if err := json.Unmarshal([]byte(`"a@b.co"`), &r); err != nil || len(r) != 1 || r[0] != "a@b.co" {
		t.Fatalf("unmarshal string = %v, %v", r, err)
	}
	if err := json.Unmarshal([]byte(`["a@b.co","c@d.co"]`), &r); err != nil || len(r) != 2 {
		t.Fatalf("unmarshal list = %v, %v", r, err)
	}
	if mustJSON(mailhive.SendEmailParams{}) != `{}` {
		t.Fatalf("empty params = %s, want {}", mustJSON(mailhive.SendEmailParams{}))
	}
}

func TestGivesUpWhenRetryAfterIsTooLong(t *testing.T) {
	var hits atomic.Int32
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		hits.Add(1)
		w.Header().Set("Retry-After", "120")
		w.WriteHeader(http.StatusTooManyRequests)
		_, _ = io.WriteString(w, `{"error":{"code":"rate_limited","message":"Slow down.","details":null,"request_id":"req_9"}}`)
	}))
	defer server.Close()
	client, _ := mailhive.New(mailhive.WithAPIKey("mhs_x"), mailhive.WithBaseURL(server.URL))
	_, err := client.Emails.Send(context.Background(), &mailhive.SendEmailParams{From: "a@b.co"})
	var rl *mailhive.RateLimitError
	if !errors.As(err, &rl) {
		t.Fatalf("error = %v", err)
	}
	if hits.Load() != 1 {
		t.Fatalf("requests = %d, want 1", hits.Load())
	}
	if wait, ok := rl.RetryAfter(); !ok || wait != 120*time.Second {
		t.Fatalf("RetryAfter = %v, %v", wait, ok)
	}
	if rl.Code != "rate_limited" || rl.RequestID != "req_9" || rl.Details != nil || rl.Kind() != mailhive.KindRateLimit {
		t.Fatalf("error = %+v", rl.APIError)
	}
}

func TestNonJSONErrorBody(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("X-Request-Id", "req_proxy")
		w.WriteHeader(http.StatusBadGateway)
		_, _ = io.WriteString(w, "<html>502 Bad Gateway</html>")
	}))
	defer server.Close()
	client, _ := mailhive.New(mailhive.WithAPIKey("mhs_x"), mailhive.WithBaseURL(server.URL), mailhive.WithMaxRetries(0))
	_, err := client.Emails.Get(context.Background(), "msg_1")
	var apiErr *mailhive.APIError
	if !errors.As(err, &apiErr) || apiErr.Code != "http_error" || apiErr.Message != "HTTP 502" || apiErr.RequestID != "req_proxy" || apiErr.Kind() != mailhive.KindAPI {
		t.Fatalf("error = %#v", err)
	}
}

func TestTimeoutIsAConnectionError(t *testing.T) {
	release := make(chan struct{})
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		select {
		case <-release:
		case <-time.After(5 * time.Second):
		}
	}))
	defer server.Close()
	defer close(release)
	client, _ := mailhive.New(mailhive.WithAPIKey("mhs_x"), mailhive.WithBaseURL(server.URL),
		mailhive.WithTimeout(100*time.Millisecond), mailhive.WithMaxRetries(0))
	_, err := client.Emails.Get(context.Background(), "msg_1")
	var connErr *mailhive.ConnectionError
	if !errors.As(err, &connErr) || !connErr.Timeout {
		t.Fatalf("error = %v", err)
	}
	if connErr.Message != "The Mailhive API didn't answer within 0.1 seconds." {
		t.Fatalf("message = %q", connErr.Message)
	}
}

func TestUnreachableIsAConnectionError(t *testing.T) {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	base := "http://" + listener.Addr().String() + "/v1"
	listener.Close()
	client, _ := mailhive.New(mailhive.WithAPIKey("mhs_x"), mailhive.WithBaseURL(base), mailhive.WithMaxRetries(0))
	_, err = client.Emails.Get(context.Background(), "msg_1")
	var connErr *mailhive.ConnectionError
	if !errors.As(err, &connErr) || connErr.Timeout || connErr.Message != "Couldn't reach the Mailhive API at "+base+"." {
		t.Fatalf("error = %v", err)
	}
	if connErr.Kind() != mailhive.KindConnection {
		t.Fatalf("kind = %s", connErr.Kind())
	}
}

func TestContextCancelsTheWaitBeforeARetry(t *testing.T) {
	var hits atomic.Int32
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		hits.Add(1)
		w.Header().Set("Retry-After", "30")
		w.WriteHeader(http.StatusServiceUnavailable)
	}))
	defer server.Close()
	client, _ := mailhive.New(mailhive.WithAPIKey("mhs_x"), mailhive.WithBaseURL(server.URL))
	ctx, cancel := context.WithTimeout(context.Background(), 100*time.Millisecond)
	defer cancel()
	started := time.Now()
	_, err := client.Emails.Send(ctx, &mailhive.SendEmailParams{From: "a@b.co"})
	if !errors.Is(err, context.DeadlineExceeded) || time.Since(started) > 2*time.Second || hits.Load() != 1 {
		t.Fatalf("error = %v after %v and %d requests", err, time.Since(started), hits.Load())
	}
}

func TestGetEscapesTheID(t *testing.T) {
	var path string
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		path = r.URL.EscapedPath()
		if r.Header.Get("Idempotency-Key") != "" {
			t.Error("GET sent an Idempotency-Key")
		}
		_, _ = io.WriteString(w, `{"id":"a/b","status":"sent","test":false,"template_id":null,"template_version":null}`)
	}))
	defer server.Close()
	client, _ := mailhive.New(mailhive.WithAPIKey("mhs_x"), mailhive.WithBaseURL(server.URL))
	email, err := client.Emails.Get(context.Background(), "a/b c")
	if err != nil {
		t.Fatal(err)
	}
	if path != "/send/emails/a%2Fb%20c" || email.Status != "sent" {
		t.Fatalf("path = %s, email = %+v", path, email)
	}
}
