# frozen_string_literal: true

module Mailhive
  # client.emails. Note that #send here sends an email: use #__send__ or
  # #public_send for Ruby's dynamic dispatch on this object.
  class Emails
    def initialize(client)
      @client = client
    end

    # Sends one email and returns the accepted email: {id:, status:, suppressed:, test:}.
    #
    # Keys match the API reference exactly (symbols or strings): from, to,
    # cc, bcc, subject, html, text, template_id, variables, reply_to,
    # headers, tags, attachments. An attachment's +content+ is the raw file
    # (encoded for you); use +content_base64+ for data that's already base64.
    #
    #   client.emails.send({ from: "hello@acme.com", to: "ada@example.com", subject: "Hi", text: "Hello" })
    #   client.emails.send(from: "hello@acme.com", to: "ada@example.com", subject: "Hi", text: "Hello")
    def send(params = nil, idempotency_key: nil, **fields)
      @client.request("POST", "/send/emails", self.class.encode((params || {}).merge(fields)), idempotency_key: idempotency_key)
    end

    # Up to 100 independent emails; all are accepted, or none. Returns the
    # list of accepted emails.
    def send_batch(emails, idempotency_key: nil)
      body = { emails: emails.map { |email| self.class.encode(email) } }
      @client.request("POST", "/send/emails/batch", body, idempotency_key: idempotency_key)[:data]
    end

    # One email, with its status ("delivered", "bounced", …).
    def get(id)
      @client.request("GET", "/send/emails/#{self.class.escape(id)}")
    end

    def inspect
      "#<#{self.class.name}>"
    end

    # @api private The JSON body for one email.
    def self.encode(params)
      raise ArgumentError, "An email must be a Hash, got #{params.class}" unless params.respond_to?(:to_hash)

      email = {}
      params.to_hash.each { |key, value| email[key.to_s] = value unless value.nil? }
      attachments = email["attachments"]
      email["attachments"] = attachments.map { |attachment| encode_attachment(attachment) } if attachments
      email
    end

    # @api private
    def self.encode_attachment(attachment)
      encoded = {}
      attachment.to_hash.each { |key, value| encoded[key.to_s] = value }
      if encoded.key?("content_base64")
        encoded["content"] = encoded.delete("content_base64").to_s
      else
        encoded["content"] = [encoded["content"].to_s].pack("m0")
      end
      encoded
    end

    # @api private Percent-encodes a path segment.
    def self.escape(value)
      value.to_s.b.gsub(/[^A-Za-z0-9\-._~]/n) { |char| format("%%%02X", char.ord) }
    end
  end
end
