package mailhive_test

import (
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"

	mailhive "github.com/Naszat/mailhive-sdks/go"
)

func TestContactUpdateSendsOnlyWhatIsSetAndEmptyFieldsAsAnObject(t *testing.T) {
	cases := []struct {
		name   string
		update mailhive.ContactUpdate
		want   string
	}{
		{"nothing set", mailhive.ContactUpdate{}, `{}`},
		{"a name", mailhive.ContactUpdate{FirstName: mailhive.String("Ada")}, `{"first_name":"Ada"}`},
		{"clearing a name", mailhive.ContactUpdate{LastName: mailhive.String("")}, `{"last_name":""}`},
		{"replacing fields", mailhive.ContactUpdate{Fields: map[string]string{"plan": "pro"}}, `{"fields":{"plan":"pro"}}`},
		{"removing all fields", mailhive.ContactUpdate{Fields: map[string]string{}}, `{"fields":{}}`},
		{"unsubscribing", mailhive.ContactUpdate{Unsubscribe: true}, `{"unsubscribe":true}`},
	}
	for _, c := range cases {
		got, err := json.Marshal(c.update)
		if err != nil || string(got) != c.want {
			t.Errorf("%s: got %s (%v), want %s", c.name, got, err, c.want)
		}
	}
}

func TestContactInputLeavesOutWhatIsNotSet(t *testing.T) {
	got, _ := json.Marshal(mailhive.ContactInput{Email: "ada@example.com"})
	if string(got) != `{"email":"ada@example.com"}` {
		t.Errorf("got %s", got)
	}
	got, _ = json.Marshal(mailhive.ContactInput{
		Email: "ada@example.com", FirstName: mailhive.String(""),
		DoubleOptIn: &mailhive.DoubleOptIn{From: "Acme <news@acme.com>"},
	})
	// Go's encoder writes < and > as \u003c and \u003e: the same JSON string.
	if string(got) != "{\"email\":\"ada@example.com\",\"first_name\":\"\",\"double_opt_in\":{\"from\":\"Acme \\u003cnews@acme.com\\u003e\"}}" {
		t.Errorf("got %s", got)
	}
}

func TestListContactsEncodesTheQueryAndEscapesThePath(t *testing.T) {
	var gotURL string
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		gotURL = r.URL.RequestURI()
		_, _ = io.WriteString(w, `{"items":[],"page":1,"page_size":20,"total":0}`)
	}))
	defer server.Close()
	client, err := mailhive.New(mailhive.WithAPIKey("mhs_x"), mailhive.WithBaseURL(server.URL+"/v1"))
	if err != nil {
		t.Fatal(err)
	}
	if _, err := client.Contacts.List(context.Background(), "aud 1", &mailhive.ListContactsParams{Email: "a+b@example.com", Page: 2}); err != nil {
		t.Fatal(err)
	}
	if gotURL != "/v1/send/audiences/aud%201/contacts?email=a%2Bb%40example.com&page=2" {
		t.Errorf("url = %s", gotURL)
	}
	if _, err := client.Contacts.List(context.Background(), "aud_1", nil); err != nil || gotURL != "/v1/send/audiences/aud_1/contacts" {
		t.Errorf("url = %s, err = %v", gotURL, err)
	}
}

func TestContactsNeedAnID(t *testing.T) {
	client, _ := mailhive.New(mailhive.WithAPIKey("mhs_x"))
	ctx := context.Background()
	if _, err := client.Contacts.Get(ctx, ""); err == nil {
		t.Error("Get with no id should fail before any request")
	}
	if err := client.Contacts.Delete(ctx, ""); err == nil {
		t.Error("Delete with no id should fail before any request")
	}
	if _, err := client.Contacts.Add(ctx, "aud_1", nil); err == nil {
		t.Error("Add with no contact should fail before any request")
	}
}
