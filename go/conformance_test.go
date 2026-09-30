package mailhive_test

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"reflect"
	"regexp"
	"strings"
	"sync"
	"testing"
	"time"

	mailhive "github.com/Naszat/mailhive-sdks/go"
)

// The shared conformance suite (spec/conformance.json): every Mailhive SDK
// runs these same cases against the same mock server.

var mock struct {
	once sync.Once
	url  string
	cmd  *exec.Cmd
	err  error
}

func TestMain(m *testing.M) {
	code := m.Run()
	if mock.cmd != nil && mock.cmd.Process != nil {
		_ = mock.cmd.Process.Kill()
		_ = mock.cmd.Wait()
	}
	os.Exit(code)
}

// mockServer starts mock-server/server.mjs once per run and returns its URL
// (without /v1).
func mockServer(t *testing.T) string {
	t.Helper()
	mock.once.Do(func() {
		if _, err := exec.LookPath("node"); err != nil {
			mock.err = errors.New("node isn't on PATH")
			return
		}
		script, _ := filepath.Abs(filepath.Join("..", "mock-server", "server.mjs"))
		cmd := exec.Command("node", script, "--port", "0")
		stdout, err := cmd.StdoutPipe()
		if err != nil {
			mock.err = err
			return
		}
		if err := cmd.Start(); err != nil {
			mock.err = err
			return
		}
		mock.cmd = cmd
		line, err := bufio.NewReader(stdout).ReadString('\n')
		if err != nil {
			mock.err = fmt.Errorf("mock server didn't start: %w", err)
			return
		}
		line = strings.TrimSpace(line)
		mock.url = line[strings.LastIndex(line, " ")+1:]
		go func() { _, _ = io.Copy(io.Discard, stdout) }()
	})
	if mock.err != nil {
		t.Skipf("mock server unavailable: %v", mock.err)
	}
	return mock.url
}

type recorded struct {
	Method  string            `json:"method"`
	Path    string            `json:"path"`
	Headers map[string]string `json:"headers"`
	Body    any               `json:"body"`
}

func mockCall(t *testing.T, method, path string, out any) {
	t.Helper()
	req, _ := http.NewRequest(method, mockServer(t)+path, nil)
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		t.Fatal(err)
	}
	defer resp.Body.Close()
	if out != nil {
		if err := json.NewDecoder(resp.Body).Decode(out); err != nil {
			t.Fatal(err)
		}
	}
}

func loadSpec(t *testing.T, name string, out any) {
	t.Helper()
	data, err := os.ReadFile(filepath.Join("..", "spec", name))
	if err != nil {
		t.Fatal(err)
	}
	if err := json.Unmarshal(data, out); err != nil {
		t.Fatal(err)
	}
}

type conformanceCase struct {
	Name           string `json:"name"`
	Key            string `json:"key"`
	Call           string `json:"call"`
	ID             string `json:"id"`
	IdempotencyKey string `json:"idempotencyKey"`
	Expect         struct {
		Attempts           int                        `json:"attempts"`
		Result             json.RawMessage            `json:"result"`
		ResultFields       json.RawMessage            `json:"resultFields"`
		MinElapsedMs       *float64                   `json:"minElapsedMs"`
		SameIdempotencyKey bool                       `json:"sameIdempotencyKey"`
		Request            map[string]json.RawMessage `json:"request"`
		Error              *struct {
			Kind       string  `json:"kind"`
			Status     int     `json:"status"`
			Code       string  `json:"code"`
			RequestID  *string `json:"requestId"`
			HasDetails bool    `json:"hasDetails"`
		} `json:"error"`
	} `json:"expect"`
}

// isKind maps the spec's error kinds to this SDK's error types.
func isKind(err error, kind string) bool {
	switch kind {
	case "api":
		var e *mailhive.APIError
		return errors.As(err, &e)
	case "authentication":
		var e *mailhive.AuthenticationError
		return errors.As(err, &e)
	case "billing":
		var e *mailhive.BillingError
		return errors.As(err, &e)
	case "permission":
		var e *mailhive.PermissionError
		return errors.As(err, &e)
	case "not_found":
		var e *mailhive.NotFoundError
		return errors.As(err, &e)
	case "conflict":
		var e *mailhive.ConflictError
		return errors.As(err, &e)
	case "validation":
		var e *mailhive.ValidationError
		return errors.As(err, &e)
	case "rate_limit":
		var e *mailhive.RateLimitError
		return errors.As(err, &e)
	}
	return false
}

// asJSON round-trips v through JSON, so results compare like the spec.
func asJSON(t *testing.T, v any) any {
	t.Helper()
	data, err := json.Marshal(v)
	if err != nil {
		t.Fatal(err)
	}
	var out any
	if err := json.Unmarshal(data, &out); err != nil {
		t.Fatal(err)
	}
	return out
}

// subset reports whether every key of want has the same value in got.
func subset(got, want map[string]any) bool {
	for k, v := range want {
		if !reflect.DeepEqual(got[k], v) {
			return false
		}
	}
	return true
}

func TestConformance(t *testing.T) {
	var spec struct {
		Email mailhive.SendEmailParams `json:"email"`
		Cases []conformanceCase        `json:"cases"`
	}
	loadSpec(t, "conformance.json", &spec)
	base := mockServer(t)

	for _, c := range spec.Cases {
		c := c
		t.Run(c.Name, func(t *testing.T) {
			mockCall(t, http.MethodPost, "/__reset", nil)
			client, err := mailhive.New(mailhive.WithAPIKey(c.Key), mailhive.WithBaseURL(base+"/v1"), mailhive.WithMaxRetries(2))
			if err != nil {
				t.Fatal(err)
			}
			var opts []mailhive.RequestOption
			if c.IdempotencyKey != "" {
				opts = append(opts, mailhive.WithIdempotencyKey(c.IdempotencyKey))
			}
			ctx := context.Background()
			email := spec.Email

			started := time.Now()
			var result any
			switch c.Call {
			case "emails.send":
				result, err = client.Emails.Send(ctx, &email, opts...)
			case "emails.sendBatch":
				result, err = client.Emails.SendBatch(ctx, []mailhive.SendEmailParams{email, email}, opts...)
			case "emails.get":
				result, err = client.Emails.Get(ctx, c.ID)
			default:
				t.Fatalf("unknown call %q", c.Call)
			}
			elapsed := time.Since(started)
			var requests []recorded
			mockCall(t, http.MethodGet, "/__requests", &requests)
			want := c.Expect

			if len(requests) != want.Attempts {
				t.Fatalf("attempts = %d, want %d", len(requests), want.Attempts)
			}
			for _, raw := range []json.RawMessage{want.Result, want.ResultFields} {
				if raw == nil {
					continue
				}
				var expected any
				_ = json.Unmarshal(raw, &expected)
				got := asJSON(t, result)
				switch expected := expected.(type) {
				case []any:
					list, ok := got.([]any)
					if !ok || len(list) != len(expected) {
						t.Fatalf("result = %v, want %v", got, expected)
					}
					for i := range expected {
						item, _ := list[i].(map[string]any)
						if !subset(item, expected[i].(map[string]any)) {
							t.Fatalf("result[%d] = %v, want %v", i, item, expected[i])
						}
					}
				case map[string]any:
					item, _ := got.(map[string]any)
					if !subset(item, expected) {
						t.Fatalf("result = %v, want %v", item, expected)
					}
				}
			}
			if want.MinElapsedMs != nil && float64(elapsed.Milliseconds()) < *want.MinElapsedMs {
				t.Fatalf("elapsed = %v, want at least %vms", elapsed, *want.MinElapsedMs)
			}
			if want.SameIdempotencyKey {
				keys := map[string]bool{}
				for _, r := range requests {
					keys[r.Headers["idempotency-key"]] = true
				}
				if len(keys) != 1 || keys[""] {
					t.Fatalf("idempotency keys = %v, want one", keys)
				}
			}
			if want.Error != nil {
				if !isKind(err, want.Error.Kind) {
					t.Fatalf("error = %#v, want kind %s", err, want.Error.Kind)
				}
				var apiErr *mailhive.APIError
				if !errors.As(err, &apiErr) {
					t.Fatalf("error %v isn't an *APIError", err)
				}
				if string(apiErr.Kind()) != want.Error.Kind && want.Error.Kind != "api" {
					t.Fatalf("Kind() = %s, want %s", apiErr.Kind(), want.Error.Kind)
				}
				if apiErr.Status != want.Error.Status || apiErr.Code != want.Error.Code {
					t.Fatalf("status, code = %d, %s; want %d, %s", apiErr.Status, apiErr.Code, want.Error.Status, want.Error.Code)
				}
				if want.Error.RequestID != nil && apiErr.RequestID != *want.Error.RequestID {
					t.Fatalf("request id = %q, want %q", apiErr.RequestID, *want.Error.RequestID)
				}
				if want.Error.HasDetails && len(apiErr.Details) == 0 {
					t.Fatal("details are empty")
				}
			} else if err != nil {
				t.Fatalf("unexpected error: %v", err)
			}

			if len(want.Request) == 0 {
				return
			}
			first := requests[0]
			headers := first.Headers
			for key, raw := range want.Request {
				var value any
				_ = json.Unmarshal(raw, &value)
				str, _ := value.(string)
				ok := false
				switch key {
				case "method":
					ok = first.Method == str
				case "path":
					ok = first.Path == str
				case "authorization":
					ok = headers["authorization"] == str
				case "contentType":
					ok = headers["content-type"] == str
				case "userAgentPattern":
					ok = regexp.MustCompile(str).MatchString(headers["user-agent"])
				case "idempotencyKeyPattern":
					ok = regexp.MustCompile(str).MatchString(headers["idempotency-key"])
				case "idempotencyKey":
					ok = headers["idempotency-key"] == str
				case "noIdempotencyKey":
					_, present := headers["idempotency-key"]
					ok = !present
				case "body":
					ok = reflect.DeepEqual(first.Body, value)
				case "bodyEmailCount":
					body, _ := first.Body.(map[string]any)
					emails, _ := body["emails"].([]any)
					ok = float64(len(emails)) == value
				default:
					t.Fatalf("unknown request check %q", key)
				}
				if !ok {
					t.Errorf("request check %s = %s failed; request: %+v", key, raw, first)
				}
			}
		})
	}
}
