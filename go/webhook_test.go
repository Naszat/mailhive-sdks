package mailhive_test

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"strings"
	"testing"
	"time"

	mailhive "github.com/Naszat/mailhive-sdks/go"
)

func TestWebhookVectors(t *testing.T) {
	var spec struct {
		ToleranceSeconds int `json:"tolerance_seconds"`
		Cases            []struct {
			Name    string `json:"name"`
			Payload string `json:"payload"`
			Header  string `json:"header"`
			Secret  string `json:"secret"`
			Now     int64  `json:"now"`
			Valid   bool   `json:"valid"`
			Reason  string `json:"reason"`
		} `json:"cases"`
	}
	loadSpec(t, "webhook-vectors.json", &spec)
	if len(spec.Cases) == 0 {
		t.Fatal("no webhook vectors")
	}
	for _, c := range spec.Cases {
		c := c
		t.Run(c.Name, func(t *testing.T) {
			event, err := mailhive.VerifyWebhook([]byte(c.Payload), c.Header, c.Secret,
				mailhive.WithNow(time.Unix(c.Now, 0)),
				mailhive.WithTolerance(time.Duration(spec.ToleranceSeconds)*time.Second))
			if c.Valid {
				if err != nil {
					t.Fatalf("unexpected error: %v", err)
				}
				if event.Type != "email.delivered" || event.Data.EmailID == "" || string(event.Raw) != c.Payload {
					t.Fatalf("event = %+v", event)
				}
				return
			}
			var verr *mailhive.WebhookVerificationError
			if !errors.As(err, &verr) {
				t.Fatalf("error = %v, want a *WebhookVerificationError", err)
			}
			if string(verr.Reason) != c.Reason {
				t.Fatalf("reason = %s, want %s", verr.Reason, c.Reason)
			}
		})
	}
}

func sign(secret string, timestamp int64, body string) string {
	mac := hmac.New(sha256.New, []byte(secret))
	fmt.Fprintf(mac, "%d.%s", timestamp, body)
	return hex.EncodeToString(mac.Sum(nil))
}

func TestWebhookUsesTheClockAndDefaultTolerance(t *testing.T) {
	body := `{"type":"contact.unsubscribed","created_at":"2026-09-28T12:00:00Z","data":{"email":"ada@example.com","contact_id":"c_1"}}`
	now := time.Now().Unix()
	header := fmt.Sprintf("t=%d,v1=%s", now, sign("whsec_x", now, body))
	event, err := mailhive.VerifyWebhook([]byte(body), header, "whsec_x")
	if err != nil {
		t.Fatal(err)
	}
	if event.Type != "contact.unsubscribed" || event.Data.Email != "ada@example.com" || event.Data.ContactID != "c_1" {
		t.Fatalf("event = %+v", event)
	}

	old := now - 301
	header = fmt.Sprintf("t=%d,v1=%s", old, sign("whsec_x", old, body))
	_, err = mailhive.VerifyWebhook([]byte(body), header, "whsec_x")
	var verr *mailhive.WebhookVerificationError
	if !errors.As(err, &verr) || verr.Reason != mailhive.ReasonTimestamp {
		t.Fatalf("error = %v, want a timestamp failure", err)
	}
	if _, err := mailhive.VerifyWebhook([]byte(body), header, "whsec_x", mailhive.WithTolerance(time.Hour)); err != nil {
		t.Fatalf("a wider tolerance should accept it: %v", err)
	}
}

func TestWebhookAcceptsUppercaseHexAndSpaces(t *testing.T) {
	body := `{"type":"email.sent","created_at":"2026-09-28T12:00:00Z","data":{}}`
	var ts int64 = 1790640000
	header := fmt.Sprintf("t=%d, v1=%s", ts, strings.ToUpper(sign("s", ts, body)))
	if _, err := mailhive.VerifyWebhook([]byte(body), header, "s", mailhive.WithNow(time.Unix(ts, 0))); err != nil {
		t.Fatal(err)
	}
}
