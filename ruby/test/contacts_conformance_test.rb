# frozen_string_literal: true

require "test_helper"

# spec/conformance.json "contactCases": audiences and contacts, run against
# the shared mock server by every SDK that implements them.
class ContactsConformanceTest < Minitest::Test
  SPEC = TestSupport.spec("conformance.json").fetch("contactCases")
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

  # A string naming a fixture stands for it.
  def fixture(value)
    value.is_a?(String) && SPEC.key?(value) ? SPEC[value] : value
  end

  def run_case(test_case, url)
    client = Mailhive::Client.new(api_key: test_case["key"], base_url: "#{url}/v1", max_retries: 2)
    audience = SPEC["audienceId"]
    a = test_case["args"] || {}
    case test_case["call"]
    when "audiences.list" then client.audiences.list
    when "audiences.create" then client.audiences.create(a["name"])
    when "audiences.rename" then client.audiences.rename(a["id"], a["name"])
    when "contacts.add" then client.contacts.add(audience, fixture(a["contact"]))
    when "contacts.upsert"
      contact = fixture(a["contact"]).dup
      contact["double_opt_in"] = fixture(a["doubleOptIn"]) if a["doubleOptIn"]
      client.contacts.upsert(audience, contact)
    when "contacts.upsertBatch" then client.contacts.upsert_batch(audience, a["contacts"].map { |c| fixture(c) })
    when "contacts.list"
      client.contacts.list(audience, email: a["email"], status: a["status"], page: a["page"], page_size: a["pageSize"])
    when "contacts.get" then client.contacts.get(a["id"])
    when "contacts.update" then client.contacts.update(a["id"], a["changes"])
    when "contacts.unsubscribe" then client.contacts.unsubscribe(a["id"])
    when "contacts.delete" then client.contacts.delete(a["id"])
    else raise "Unknown call #{test_case["call"]}"
    end
  end

  def at(value, path)
    path.split(".").reduce(value) { |node, part| node.is_a?(Array) ? node[part.to_i] : node[part] }
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
    json = result.nil? ? nil : JSON.parse(JSON.generate(result))

    assert_equal want["attempts"], requests.length, "attempts"
    (want["resultFields"] || {}).each { |path, expected| assert_equal expected, at(json, path), path }
    assert_equal want["resultCount"], json.length if want.key?("resultCount")
    assert_nil result, "result" if want["resultIsEmpty"]
    assert_operator elapsed_ms, :>=, want["minElapsedMs"] if want.key?("minElapsedMs")

    if want.key?("error")
      expected = want["error"]
      assert_kind_of KINDS.fetch(expected["kind"]), error
      assert_equal expected["status"], error.status
      assert_equal expected["code"], error.code
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
           when "noIdempotencyKey" then !headers.key?("idempotency-key")
           when "query" then first["query"] == value
           when "body" then first["body"] == fixture(value)
           when "bodyIncludes" then first["body"].slice(*value.keys) == value
           when "bodyContactCount" then first["body"]["contacts"].length == value
           else flunk "Unknown request check #{check}"
           end
      assert ok, "#{check}: expected #{value.inspect}, got #{first.inspect}"
    end
  end

  SPEC["cases"].each_with_index do |test_case, index|
    define_method("test_#{format("%02d", index)}_#{test_case["name"].gsub(/\W+/, "_")}") { check(test_case) }
  end
end
