# frozen_string_literal: true

module Mailhive
  # Base class for everything this SDK raises.
  class Error < StandardError; end

  # An error response from the API. Check #code, not #message: codes are
  # stable, messages may change.
  class APIError < Error
    attr_reader :status, :code, :details, :request_id, :headers

    def initialize(message = nil, status: nil, code: nil, details: nil, request_id: nil, headers: nil)
      super(message)
      @status = status
      @code = code
      @details = details
      @request_id = request_id
      @headers = headers || {}
    end

    def inspect
      "#<#{self.class.name} status=#{status.inspect} code=#{code.inspect} request_id=#{request_id.inspect}>"
    end
  end

  # 401: missing, unknown or revoked API key.
  class AuthenticationError < APIError; end

  # 402: Mailhive Send is paused over an unpaid invoice.
  class BillingError < APIError; end

  # 403: Send isn't activated, or the stream is paused.
  class PermissionError < APIError; end

  # 404
  class NotFoundError < APIError; end

  # 409: e.g. an Idempotency-Key reused for a different request.
  class ConflictError < APIError; end

  # 400 or 422: the request is invalid; #details lists every problem.
  class ValidationError < APIError; end

  # 429: rate_limited (retried for you), or monthly_quota_reached /
  # daily_cap_reached (not retried: waiting won't help).
  class RateLimitError < APIError
    # Seconds from the Retry-After header, or nil.
    def retry_after
      Mailhive.parse_seconds(headers["retry-after"])
    end
  end

  # The API couldn't be reached, or didn't answer in time.
  class ConnectionError < Error; end

  # A webhook's signature didn't check out. #reason is "header", "timestamp"
  # or "signature".
  class WebhookVerificationError < Error
    attr_reader :reason

    def initialize(message, reason)
      super(message)
      @reason = reason
    end
  end

  ERRORS_BY_STATUS = {
    400 => ValidationError,
    401 => AuthenticationError,
    402 => BillingError,
    403 => PermissionError,
    404 => NotFoundError,
    409 => ConflictError,
    422 => ValidationError,
    429 => RateLimitError
  }.freeze
  private_constant :ERRORS_BY_STATUS

  # @api private
  def self.error_for(status, message, **fields)
    ERRORS_BY_STATUS.fetch(status, APIError).new(message, status: status, **fields)
  end

  # @api private A non-negative number of seconds, or nil.
  def self.parse_seconds(value)
    return nil if value.nil? || value.to_s.strip.empty?

    seconds = Float(value.to_s.strip, exception: false)
    seconds if seconds&.finite? && seconds >= 0
  end
end
