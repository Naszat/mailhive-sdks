# frozen_string_literal: true

module Mailhive
  # The Mailhive Send API client.
  #
  #   client = Mailhive::Client.new # reads MAILHIVE_API_KEY
  #   client.emails.send(from: "Acme <hello@acme.com>", to: "ada@example.com", subject: "Hi", text: "Hello")
  #
  # Responses are Hashes with symbol keys, parsed from the API's JSON.
  class Client
    DEFAULT_BASE_URL = "https://api.mailhive.africa/v1"
    MAX_RETRY_AFTER = 60.0

    # Errors that mean no response came back: retried, then raised as
    # Mailhive::ConnectionError.
    NETWORK_ERRORS = [
      Timeout::Error, # includes Net::OpenTimeout and Net::ReadTimeout
      Net::WriteTimeout,
      IOError, # includes EOFError
      SocketError,
      SystemCallError, # Errno::ECONNREFUSED, ECONNRESET, EPIPE, ETIMEDOUT, …
      OpenSSL::SSL::SSLError,
      Net::HTTPBadResponse
    ].freeze

    TIMEOUT_ERRORS = [Timeout::Error, Net::WriteTimeout, Errno::ETIMEDOUT].freeze
    private_constant :TIMEOUT_ERRORS

    attr_reader :base_url, :timeout, :max_retries, :emails

    # Options:
    # - api_key: a secret key (mhs_…); default MAILHIVE_API_KEY
    # - base_url: default MAILHIVE_BASE_URL, then production
    # - timeout: seconds per request (connect, write and read)
    # - max_retries: retries after the first attempt
    # - transport, sleeper: for tests (see NetHTTPTransport; sleeper gets seconds)
    def initialize(api_key: nil, base_url: nil, timeout: 30, max_retries: 2, transport: nil, sleeper: nil)
      key = present(api_key) || present(ENV.fetch("MAILHIVE_API_KEY", nil))
      if key.nil?
        raise Error, "No API key. Pass one to Mailhive::Client.new(api_key: …) or set MAILHIVE_API_KEY. " \
                     "Create keys under Mailhive Send → API keys."
      end
      if key.start_with?("mhp_")
        raise Error, "That's a form's publishable key (mhp_…). The server SDK needs a secret API key (mhs_…) " \
                     "from Mailhive Send → API keys."
      end

      @api_key = key
      @base_url = (present(base_url) || present(ENV.fetch("MAILHIVE_BASE_URL", nil)) || DEFAULT_BASE_URL).sub(%r{/+\z}, "")
      @timeout = timeout
      @max_retries = [max_retries.to_i, 0].max
      @transport = transport || NetHTTPTransport.new
      @sleeper = sleeper || ->(seconds) { sleep(seconds) }
      @emails = Emails.new(self)
    end

    # Never shows the key.
    def inspect
      "#<#{self.class.name} base_url=#{base_url.inspect}>"
    end
    alias to_s inspect

    # @api private Used by the resources.
    def request(method, path, body = nil, idempotency_key: nil)
      headers = {
        "Authorization" => "Bearer #{@api_key}",
        "Accept" => "application/json",
        "User-Agent" => "mailhive-ruby/#{VERSION} ruby/#{RUBY_VERSION}"
      }
      payload = nil
      unless body.nil?
        headers["Content-Type"] = "application/json"
        payload = JSON.generate(body)
      end
      # One key per call, reused on every retry: a retry never sends twice.
      headers["Idempotency-Key"] = present(idempotency_key&.to_s) || SecureRandom.uuid if method == "POST"
      url = "#{base_url}#{path}"

      attempt = 0
      loop do
        begin
          response = @transport.call(method, url, headers, payload, timeout)
        rescue *NETWORK_ERRORS => e
          if attempt < max_retries
            @sleeper.call(self.class.backoff(attempt))
            attempt += 1
            next
          end
          raise ConnectionError, connection_message(e)
        end

        return parse(response.body) if response.status.between?(200, 299)

        error = build_error(response)
        wait = wait_before_retry(error, attempt)
        raise error if wait.nil?

        @sleeper.call(wait)
        attempt += 1
      end
    end

    # About 0.5s, 1s, 2s … up to 8s, with jitter.
    def self.backoff(attempt)
      ceiling = [8.0, 0.5 * (2**attempt)].min
      (ceiling / 2) + (rand * ceiling / 2)
    end

    private

    def present(value)
      value.nil? || value.empty? ? nil : value
    end

    def parse(body)
      body.nil? || body.strip.empty? ? {} : JSON.parse(body, symbolize_names: true)
    end

    # Seconds to wait before retrying, or nil to give up. Only a rate limit
    # and server errors are worth retrying: a used-up allowance or a bad
    # request fails the same way again.
    def wait_before_retry(error, attempt)
      return nil if attempt >= max_retries
      return nil unless error.status >= 500 || (error.status == 429 && error.code == "rate_limited")

      wait = Mailhive.parse_seconds(error.headers["retry-after"])
      return nil if wait && wait > MAX_RETRY_AFTER

      wait || self.class.backoff(attempt)
    end

    def build_error(response)
      headers = response.headers || {}
      error = begin
        decoded = JSON.parse(response.body.to_s, symbolize_names: true)
        decoded.is_a?(Hash) && decoded[:error].is_a?(Hash) ? decoded[:error] : {}
      rescue JSON::ParserError
        {}
      end
      Mailhive.error_for(
        response.status,
        error[:message].is_a?(String) ? error[:message] : "HTTP #{response.status}",
        code: error[:code].is_a?(String) ? error[:code] : "http_error",
        details: error[:details],
        request_id: error[:request_id] || headers["x-request-id"],
        headers: headers
      )
    end

    def connection_message(error)
      if TIMEOUT_ERRORS.any? { |klass| error.is_a?(klass) }
        seconds = timeout.to_f == timeout.to_i ? timeout.to_i : timeout.to_f
        "The Mailhive API didn't answer within #{seconds} seconds."
      else
        "Couldn't reach the Mailhive API at #{base_url}."
      end
    end
  end
end
