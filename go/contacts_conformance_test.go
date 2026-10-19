package mailhive_test

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"reflect"
	"strconv"
	"strings"
	"testing"
	"time"

	mailhive "github.com/Naszat/mailhive-sdks/go"
)

// spec/conformance.json "contactCases": audiences and contacts, run against
// the shared mock server by every SDK that implements them.

type recordedRequest struct {
	Method  string            `json:"method"`
	Path    string            `json:"path"`
	Query   map[string]string `json:"query"`
	Headers map[string]string `json:"headers"`
	Body    any               `json:"body"`
}

type contactCase struct {
	Name   string         `json:"name"`
	Key    string         `json:"key"`
	Call   string         `json:"call"`
	Args   map[string]any `json:"args"`
	Expect struct {
		Attempts      int                        `json:"attempts"`
		ResultFields  map[string]any             `json:"resultFields"`
		ResultCount   *int                       `json:"resultCount"`
		ResultIsEmpty bool                       `json:"resultIsEmpty"`
		MinElapsedMs  *float64                   `json:"minElapsedMs"`
		Request       map[string]json.RawMessage `json:"request"`
		Error         *struct {
			Kind   string `json:"kind"`
			Status int    `json:"status"`
			Code   string `json:"code"`
		} `json:"error"`
	} `json:"expect"`
}

// at follows a dotted path ("results.1.email", "0.id") into decoded JSON.
func at(value any, path string) any {
	for _, part := range strings.Split(path, ".") {
		switch v := value.(type) {
		case []any:
			i, err := strconv.Atoi(part)
			if err != nil || i >= len(v) {
				return nil
			}
			value = v[i]
		case map[string]any:
			value = v[part]
		default:
			return nil
		}
	}
	return value
}

func TestContactConformance(t *testing.T) {
	var spec struct {
		ContactCases struct {
			AudienceID   string          `json:"audienceId"`
			Contact      json.RawMessage `json:"contact"`
			Confirmation json.RawMessage `json:"confirmation"`
			Cases        []contactCase   `json:"cases"`
		} `json:"contactCases"`
	}
	loadSpec(t, "conformance.json", &spec)
	cc := spec.ContactCases
	base := mockServer(t)

	// A string naming a fixture stands for it.
	fixture := func(value any) json.RawMessage {
		if s, ok := value.(string); ok {
			switch s {
			case "contact":
				return cc.Contact
			case "confirmation":
				return cc.Confirmation
			}
		}
		data, _ := json.Marshal(value)
		return data
	}
	contactInput := func(value any) mailhive.ContactInput {
		var in mailhive.ContactInput
		if err := json.Unmarshal(fixture(value), &in); err != nil {
			t.Fatal(err)
		}
		return in
	}

	for _, c := range cc.Cases {
		c := c
		t.Run(c.Name, func(t *testing.T) {
			mockCall(t, http.MethodPost, "/__reset", nil)
			client, err := mailhive.New(mailhive.WithAPIKey(c.Key), mailhive.WithBaseURL(base+"/v1"), mailhive.WithMaxRetries(2))
			if err != nil {
				t.Fatal(err)
			}
			ctx := context.Background()
			a := c.Args
			str := func(key string) string { s, _ := a[key].(string); return s }
			num := func(key string) int { f, _ := a[key].(float64); return int(f) }

			started := time.Now()
			var result any
			switch c.Call {
			case "audiences.list":
				result, err = client.Audiences.List(ctx)
			case "audiences.create":
				result, err = client.Audiences.Create(ctx, str("name"))
			case "audiences.rename":
				result, err = client.Audiences.Rename(ctx, str("id"), str("name"))
			case "contacts.add":
				in := contactInput(a["contact"])
				result, err = client.Contacts.Add(ctx, cc.AudienceID, &in)
			case "contacts.upsert":
				in := contactInput(a["contact"])
				if a["doubleOptIn"] != nil {
					var d mailhive.DoubleOptIn
					_ = json.Unmarshal(fixture(a["doubleOptIn"]), &d)
					in.DoubleOptIn = &d
				}
				result, err = client.Contacts.Upsert(ctx, cc.AudienceID, &in)
			case "contacts.upsertBatch":
				var list []mailhive.ContactInput
				for _, item := range a["contacts"].([]any) {
					list = append(list, contactInput(item))
				}
				result, err = client.Contacts.UpsertBatch(ctx, cc.AudienceID, list)
			case "contacts.list":
				result, err = client.Contacts.List(ctx, cc.AudienceID, &mailhive.ListContactsParams{
					Email: str("email"), Status: str("status"), Page: num("page"), PageSize: num("pageSize"),
				})
			case "contacts.get":
				result, err = client.Contacts.Get(ctx, str("id"))
			case "contacts.update":
				var changes mailhive.ContactUpdate
				raw, _ := json.Marshal(a["changes"])
				var m struct {
					FirstName *string           `json:"first_name"`
					LastName  *string           `json:"last_name"`
					Fields    map[string]string `json:"fields"`
				}
				_ = json.Unmarshal(raw, &m)
				changes = mailhive.ContactUpdate{FirstName: m.FirstName, LastName: m.LastName, Fields: m.Fields}
				result, err = client.Contacts.Update(ctx, str("id"), changes)
			case "contacts.unsubscribe":
				result, err = client.Contacts.Unsubscribe(ctx, str("id"))
			case "contacts.delete":
				err = client.Contacts.Delete(ctx, str("id"))
			default:
				t.Fatalf("unknown call %q", c.Call)
			}
			elapsed := time.Since(started)
			var requests []recordedRequest
			mockCall(t, http.MethodGet, "/__requests", &requests)
			want := c.Expect

			if len(requests) != want.Attempts {
				t.Fatalf("attempts = %d, want %d", len(requests), want.Attempts)
			}
			if len(want.ResultFields) > 0 || want.ResultCount != nil {
				got := asJSON(t, result)
				for path, expected := range want.ResultFields {
					if actual := at(got, path); !reflect.DeepEqual(actual, expected) {
						t.Errorf("result %s = %v, want %v", path, actual, expected)
					}
				}
				if want.ResultCount != nil {
					if list, _ := got.([]any); len(list) != *want.ResultCount {
						t.Errorf("result has %d items, want %d", len(list), *want.ResultCount)
					}
				}
			}
			if want.ResultIsEmpty && (err != nil || result != nil) {
				t.Errorf("result = %v, err = %v; want nothing", result, err)
			}
			if want.MinElapsedMs != nil && float64(elapsed.Milliseconds()) < *want.MinElapsedMs {
				t.Fatalf("elapsed = %v, want at least %vms", elapsed, *want.MinElapsedMs)
			}
			if want.Error != nil {
				var apiErr *mailhive.APIError
				if !isKind(err, want.Error.Kind) || !errors.As(err, &apiErr) {
					t.Fatalf("error = %#v, want kind %s", err, want.Error.Kind)
				}
				if apiErr.Status != want.Error.Status || apiErr.Code != want.Error.Code {
					t.Fatalf("status, code = %d, %s; want %d, %s", apiErr.Status, apiErr.Code, want.Error.Status, want.Error.Code)
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
				s, _ := value.(string)
				ok := false
				switch key {
				case "method":
					ok = first.Method == s
				case "path":
					ok = first.Path == s
				case "authorization":
					ok = headers["authorization"] == s
				case "contentType":
					ok = headers["content-type"] == s
				case "noIdempotencyKey":
					_, present := headers["idempotency-key"]
					ok = !present
				case "query":
					expected := map[string]string{}
					for k, v := range value.(map[string]any) {
						expected[k] = v.(string)
					}
					got := first.Query
					if got == nil {
						got = map[string]string{}
					}
					ok = reflect.DeepEqual(got, expected)
				case "body":
					var expected any
					_ = json.Unmarshal(fixture(value), &expected)
					ok = reflect.DeepEqual(first.Body, expected)
				case "bodyIncludes":
					body, _ := first.Body.(map[string]any)
					ok = subset(body, value.(map[string]any))
				case "bodyContactCount":
					body, _ := first.Body.(map[string]any)
					list, _ := body["contacts"].([]any)
					ok = float64(len(list)) == value
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
