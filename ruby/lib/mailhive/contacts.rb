# frozen_string_literal: true

module Mailhive
  # client.contacts: the people in an audience. Needs an API key with Full
  # access. Keys match the API reference (symbols or strings): email,
  # first_name, last_name, fields, status, double_opt_in.
  #
  # Someone who unsubscribed, or reported your email as spam, can't be made
  # subscribed again by these calls (a Mailhive::ConflictError with code
  # "contact_unsubscribed"). Pass double_opt_in: { from:, subject: } to email
  # them a confirmation link instead: they're subscribed only if they click
  # it, and until then they're "pending" and never mailed.
  class Contacts
    def initialize(client)
      @client = client
    end

    # A page of contacts: {items:, page:, page_size:, total:}. Pass +email+ to
    # find one person by exact address.
    def list(audience_id, email: nil, q: nil, status: nil, page: nil, page_size: nil)
      query = { email: email, q: q, status: status, page: page, page_size: page_size }
      @client.request("GET", audience_path(audience_id), nil, query: query)
    end

    # Adds one person. A 409 "contact_exists" if they're already there: use #upsert.
    #
    #   client.contacts.add(audience_id, email: "ada@example.com", first_name: "Ada")
    def add(audience_id, contact = nil, idempotency_key: nil, **fields)
      @client.request("POST", audience_path(audience_id), self.class.encode((contact || {}).merge(fields)),
                      idempotency_key: idempotency_key)
    end

    # Adds the person, or updates them if they're already there. Safe to repeat.
    def upsert(audience_id, contact = nil, **fields)
      @client.request("PUT", audience_path(audience_id), self.class.encode((contact || {}).merge(fields)))
    end

    # Adds or updates up to 100 people. A bad entry doesn't raise: check each
    # result's +ok+. Returns {created:, updated:, failed:, results: […]}.
    def upsert_batch(audience_id, contacts, idempotency_key: nil)
      body = { contacts: contacts.map { |contact| self.class.encode(contact) } }
      @client.request("POST", "#{audience_path(audience_id)}/batch", body, idempotency_key: idempotency_key)
    end

    # A contact and the latest emails sent to them (+history+).
    def get(id)
      @client.request("GET", contact_path(id))
    end

    # Edits names and fields (fields are replaced, not merged: use #upsert to
    # merge), or unsubscribes with unsubscribe: true.
    def update(id, changes = nil, **fields)
      @client.request("PATCH", contact_path(id), self.class.encode((changes || {}).merge(fields)))
    end

    # Unsubscribes them. Only they can undo it, by confirming a double opt-in email.
    def unsubscribe(id)
      update(id, unsubscribe: true)
    end

    # Deleting someone who unsubscribed keeps their address suppressed. Returns nil.
    def delete(id)
      @client.request("DELETE", contact_path(id))
      nil
    end

    def inspect
      "#<#{self.class.name}>"
    end

    # @api private The JSON body: string keys, with nil values left out (an
    # empty string clears a name, and an empty Hash is sent as {}).
    def self.encode(params)
      raise ArgumentError, "A contact must be a Hash, got #{params.class}" unless params.respond_to?(:to_hash)

      params.to_hash.each_with_object({}) do |(key, value), body|
        body[key.to_s] = value.respond_to?(:to_hash) ? stringify(value.to_hash) : value unless value.nil?
      end
    end

    # @api private
    def self.stringify(hash)
      hash.each_with_object({}) { |(key, value), out| out[key.to_s] = value.respond_to?(:to_hash) ? stringify(value.to_hash) : value }
    end

    private

    def audience_path(id)
      "/send/audiences/#{Emails.escape(id)}/contacts"
    end

    def contact_path(id)
      "/send/contacts/#{Emails.escape(id)}"
    end
  end
end
