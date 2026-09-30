package mailhive_test

import (
	"context"
	"errors"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"

	mailhive "github.com/Naszat/mailhive-sdks/go"
)

func ExampleEmailsService_Send() {
	client, err := mailhive.New() // reads MAILHIVE_API_KEY
	if err != nil {
		log.Fatal(err)
	}
	email, err := client.Emails.Send(context.Background(), &mailhive.SendEmailParams{
		From:    "Acme <hello@acme.com>",
		To:      mailhive.Recipients{"ada@example.com"},
		Subject: "Your receipt",
		HTML:    "<p>Thanks for your order.</p>",
	}, mailhive.WithIdempotencyKey("order-1042-receipt"))
	var rateLimit *mailhive.RateLimitError
	if errors.As(err, &rateLimit) && rateLimit.Code == "monthly_quota_reached" {
		log.Fatal("the monthly allowance is used up")
	} else if err != nil {
		log.Fatal(err)
	}
	fmt.Println(email.ID)
}

func ExampleVerifyWebhook() {
	http.HandleFunc("/webhooks/mailhive", func(w http.ResponseWriter, r *http.Request) {
		body, err := io.ReadAll(r.Body) // the raw body, exactly as received
		if err != nil {
			http.Error(w, "bad body", http.StatusBadRequest)
			return
		}
		event, err := mailhive.VerifyWebhook(body, r.Header.Get("Mailhive-Signature"), os.Getenv("MAILHIVE_WEBHOOK_SECRET"))
		if err != nil {
			http.Error(w, "invalid signature", http.StatusBadRequest)
			return
		}
		log.Printf("%s for %s", event.Type, event.Data.EmailID)
		w.WriteHeader(http.StatusNoContent)
	})
}
