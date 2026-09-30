# frozen_string_literal: true

module Mailhive
  # Checking Mailhive-Signature on webhooks.
  module Webhook
    V1 = /\A[0-9a-fA-F]{64}\z/.freeze
    private_constant :V1

    # Checks a webhook's Mailhive-Signature header (HMAC-SHA256 of
    # "<t>.<raw body>" with the endpoint's secret) and returns the event, a
    # Hash with symbol keys. +payload+ must be the raw body exactly as
    # received (in Rails, request.raw_post): parsing and re-serializing it
    # changes the bytes. Several v1= values are accepted, so secrets can be
    # rotated. Raises Mailhive::WebhookVerificationError.
    def self.verify(payload, signature_header, secret, tolerance: 300, now: nil)
      unless payload.is_a?(String)
        raise TypeError, "Mailhive::Webhook.verify needs the raw request body (a String), not #{payload.class}: " \
                         "re-serializing changes the bytes, so the signature can't match."
      end

      timestamp, candidates = parse(signature_header)
      raise WebhookVerificationError.new("Missing or malformed Mailhive-Signature header.", "header") if timestamp.nil?

      current = now.nil? ? Time.now.to_f : now.to_f
      if (current - timestamp).abs > tolerance
        raise WebhookVerificationError.new(
          "The webhook's timestamp is more than #{tolerance} seconds from now; it may be a replay.", "timestamp"
        )
      end

      expected = OpenSSL::HMAC.hexdigest("SHA256", secret.to_s, "#{timestamp}.".b + payload.b)
      unless candidates.any? { |candidate| OpenSSL.fixed_length_secure_compare(expected, candidate) }
        raise WebhookVerificationError.new(
          "The webhook's signature doesn't match. Check the endpoint's signing secret.", "signature"
        )
      end

      JSON.parse(payload, symbolize_names: true)
    end

    # [timestamp, [v1, …]], or nil when either is missing.
    def self.parse(header)
      return nil if header.nil?

      timestamp = nil
      candidates = []
      header.to_s.split(",").each do |part|
        key, separator, value = part.partition("=")
        next if separator.empty?

        key = key.strip
        value = value.strip
        if key == "t" && value.match?(/\A\d+\z/)
          timestamp = value.to_i
        elsif key == "v1" && value.match?(V1)
          candidates << value.downcase
        end
      end
      return nil if timestamp.nil? || candidates.empty?

      [timestamp, candidates]
    end
    private_class_method :parse
  end
end
