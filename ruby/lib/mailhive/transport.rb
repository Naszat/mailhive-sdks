# frozen_string_literal: true

module Mailhive
  # What a transport returns. +headers+ has lowercase names.
  Response = Struct.new(:status, :headers, :body)

  # The default transport, on Net::HTTP. A transport is anything with
  # +call(method, url, headers, body, timeout)+ returning a Mailhive::Response,
  # and raising the usual Net::HTTP / socket errors when there's no response.
  class NetHTTPTransport
    def call(method, url, headers, body, timeout)
      uri = URI.parse(url)
      http = Net::HTTP.new(uri.host, uri.port)
      http.use_ssl = uri.scheme == "https"
      http.open_timeout = timeout
      http.read_timeout = timeout
      http.write_timeout = timeout
      # The client does its own retries, reusing the idempotency key.
      http.max_retries = 0

      request = request_class(method).new(uri.request_uri, headers)
      request.body = body if body
      response = http.start { |connection| connection.request(request) }

      response_headers = {}
      response.each_header { |name, value| response_headers[name.downcase] = value }
      Response.new(response.code.to_i, response_headers, response.body.to_s.dup.force_encoding(Encoding::UTF_8))
    end

    private

    def request_class(method)
      case method
      when "GET" then Net::HTTP::Get
      when "POST" then Net::HTTP::Post
      when "DELETE" then Net::HTTP::Delete
      when "PATCH" then Net::HTTP::Patch
      when "PUT" then Net::HTTP::Put
      else raise ArgumentError, "Unsupported HTTP method #{method}"
      end
    end
  end
end
