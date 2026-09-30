# frozen_string_literal: true

require "test_helper"

# The shared conformance suite (spec/conformance.json): every Mailhive SDK
# runs these same cases against the same mock server.
class ConformanceTest < Minitest::Test
  SPEC = TestSupport.spec("conformance.json")
  KINDS = {
    "api" => Mailhive::APIError,
    "authentication" => Mailhive::AuthenticationError,
    "billing" => Mailhive::BillingError,
    "permission" => Mailhive::PermissionError,
    "not_found" => Mailhive::NotFoundError,
    "conflict" => Mailhive::ConflictError,
    "validation" => Mailhive::ValidationError,
    "rate_limit" => Mailhive::RateLimitError
  }.freeze

  def run_case(test_case, url)
    client = Mailhive::Client.new(api_key: test_case["key"], base_url: "#{url}/v1", max_retries: 2)
    email = SPEC["email"].dup
    key = test_case["idempotencyKey"]
    case test_case["call"]
    when "emails.send" then client.emails.send(email, idempotency_key: key)
    when "emails.sendBatch" then client.emails.send_batch([email, email], idempotency_key: key)
    when "emails.get" then client.emails.get(test_case["id"])
    else raise "Unknown call #{test_case["call"]}"
    end
  end

  def check(test_case)
    want = test_case["expect"]
    url = TestSupport::MockServer.url
    TestSupport::MockServer.reset
    started = Process.clock_gettime(Process::CLOCK_MONOTONIC)
    result = error = nil
    begin
      result = run_case(test_case, url)
    rescue Mailhive::Error => e
      error = e
    end
    elapsed_ms = (Process.clock_gettime(Process::CLOCK_MONOTONIC) - started) * 1000
    requests = TestSupport::MockServer.requests
    # String keys, like the spec.
    result = JSON.parse(JSON.generate(result)) unless result.nil?

    assert_equal want["attempts"], requests.length, "attempts"
    %w[result resultFields].each do |field|
      next unless want.key?(field)

      expected = want[field]
      if expected.is_a?(Array)
        assert_equal expected, result.zip(expected).map { |r, e| r.slice(*e.keys) }
      else
        assert_equal expected, result.slice(*expected.keys)
      end
    end
    assert_operator elapsed_ms, :>=, want["minElapsedMs"] if want.key?("minElapsedMs")
    if want["sameIdempotencyKey"]
      keys = requests.map { |r| r["headers"]["idempotency-key"] }.uniq
      assert_equal 1, keys.length
      refute_nil keys.first
    end

    if want.key?("error")
      expected = want["error"]
      assert_kind_of KINDS.fetch(expected["kind"]), error
      assert_equal expected["status"], error.status
      assert_equal expected["code"], error.code
      assert_equal expected["requestId"], error.request_id if expected.key?("requestId")
      assert error.details && !error.details.empty?, "details" if expected["hasDetails"]
    else
      assert_nil error
    end

    request = want["request"]
    return unless request

    first = requests.first
    headers = first["headers"]
    request.each do |check, value|
      ok = case check
           when "method" then first["method"] == value
           when "path" then first["path"] == value
           when "authorization" then headers["authorization"] == value
           when "contentType" then headers["content-type"] == value
           when "userAgentPattern" then Regexp.new(value).match?(headers["user-agent"])
           when "idempotencyKeyPattern" then Regexp.new(value).match?(headers["idempotency-key"].to_s)
           when "idempotencyKey" then headers["idempotency-key"] == value
           when "noIdempotencyKey" then !headers.key?("idempotency-key")
           when "body" then first["body"] == value
           when "bodyEmailCount" then first["body"]["emails"].length == value
           else flunk "Unknown request check #{check}"
           end
      assert ok, "#{check}: expected #{value.inspect}, got #{first.inspect}"
    end
  end

  SPEC["cases"].each_with_index do |test_case, index|
    define_method("test_#{format("%02d", index)}_#{test_case["name"].gsub(/\W+/, "_")}") { check(test_case) }
  end
end
