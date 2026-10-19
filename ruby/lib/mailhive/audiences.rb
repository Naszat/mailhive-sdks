# frozen_string_literal: true

module Mailhive
  # client.audiences: the lists your broadcasts go to. Needs an API key with
  # Full access.
  class Audiences
    def initialize(client)
      @client = client
    end

    # The organization's audiences: [{id:, name:, contacts:, mailable:, …}].
    def list
      @client.request("GET", "/send/audiences")
    end

    def create(name, idempotency_key: nil)
      @client.request("POST", "/send/audiences", { name: name }, idempotency_key: idempotency_key)
    end

    # Changes an audience's name. Returns {id:, name:}.
    def rename(id, name)
      @client.request("PATCH", "/send/audiences/#{Emails.escape(id)}", { name: name })
    end

    def inspect
      "#<#{self.class.name}>"
    end
  end
end
