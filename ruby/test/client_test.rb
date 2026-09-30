# frozen_string_literal: true

require "test_helper"

class ClientTest < Minitest::Test
  include TestSupport

  ACCEPTED = { id: "msg_1", status: "queued", suppressed: [], test: false }.freeze

  def client(transport, **options)
    sleeps = options.delete(:sleeps) || []
    Mailhive::Client.new(api_key: "mhs_test_unit", transport: transport, sleeper: ->(s) { sleeps << s }, **options)
  end

  def test_version
    assert_match(/\A\d+\.\d+\.\d+\z/, Mailhive::VERSION)
    assert_equal "0.1.0", Mailhive::VERSION
  end

  def test_reads_the_key_and_base_url_from_the_environment
    with_env("MAILHIVE_API_KEY" => "mhs_from_env", "MAILHIVE_BASE_URL" => "https://api-beta.mailhive.africa/v1/") do
      transport = FakeTransport.json(200, ACCEPTED)
      client = Mailhive::Client.new(transport: transport)
      assert_equal "https://api-beta.mailhive.africa/v1", client.base_url
      client.emails.send(from: "a@acme.com", to: "b@example.com", subject: "Hi", text: "Hello")
      assert_equal "Bearer mhs_from_env", transport.requests[0][:headers]["Authorization"]
      assert_equal "https://api-beta.mailhive.africa/v1/send/emails", transport.requests[0][:url]
    end
  end

  def test_arguments_beat_the_environment
    with_env("MAILHIVE_API_KEY" => "mhs_from_env", "MAILHIVE_BASE_URL" => "https://env.example/v1") do
      client = Mailhive::Client.new(api_key: "mhs_arg", base_url: "https://arg.example/v1//")
      assert_equal "https://arg.example/v1", client.base_url
    end
  end

  def test_defaults_to_production
    with_env("MAILHIVE_API_KEY" => "mhs_x", "MAILHIVE_BASE_URL" => nil) do
      assert_equal "https://api.mailhive.africa/v1", Mailhive::Client.new.base_url
    end
  end

  def test_a_missing_key_is_an_error
    with_env("MAILHIVE_API_KEY" => nil) do
      error = assert_raises(Mailhive::Error) { Mailhive::Client.new }
      assert_match(/MAILHIVE_API_KEY/, error.message)
    end
  end

  def test_a_publishable_key_is_refused
    error = assert_raises(Mailhive::Error) { Mailhive::Client.new(api_key: "mhp_abc") }
    assert_match(/publishable key \(mhp_…\)/, error.message)
  end

  def test_the_key_never_shows
    client = Mailhive::Client.new(api_key: "mhs_supersecret")
    [client.inspect, client.to_s, client.emails.inspect, "#{client}"].each do |text|
      refute_includes text, "mhs_supersecret"
    end
    require "pp"
    refute_includes client.pretty_inspect, "mhs_supersecret"
  end

  def test_sends_the_standard_headers
    transport = FakeTransport.json(200, ACCEPTED)
    result = client(transport).emails.send({ "from" => "a@acme.com", "to" => "b@example.com", "subject" => "Hi", "text" => "Hello" })
    assert_equal ACCEPTED, result
    headers = transport.requests[0][:headers]
    assert_match(%r{\Amailhive-ruby/0\.1\.0 ruby/\d+\.\d+}, headers["User-Agent"])
    assert_equal "application/json", headers["Content-Type"]
    assert_equal "application/json", headers["Accept"]
    assert_match(/\A\h{8}-\h{4}-4\h{3}-[89ab]\h{3}-\h{12}\z/, headers["Idempotency-Key"])
  end

  def test_get_escapes_the_id_and_sends_no_body_or_idempotency_key
    transport = FakeTransport.json(200, { id: "a b/c" })
    client(transport).emails.get("a b/c")
    request = transport.requests[0]
    assert_equal "https://api.mailhive.africa/v1/send/emails/a%20b%2Fc", request[:url]
    assert_nil request[:body]
    refute request[:headers].key?("Idempotency-Key")
    refute request[:headers].key?("Content-Type")
  end

  def test_encodes_attachments
    transport = FakeTransport.json(200, ACCEPTED)
    raw = "%PDF-1.4\x00\xFF".b
    client(transport).emails.send(
      from: "a@acme.com", to: "b@example.com", subject: "Invoice", text: "Attached",
      attachments: [
        { filename: "invoice.pdf", content: raw, content_type: "application/pdf" },
        { "filename" => "note.txt", "content_base64" => "aGk=" }
      ]
    )
    attachments = transport.last_body["attachments"]
    assert_equal({ "filename" => "invoice.pdf", "content" => [raw].pack("m0"), "content_type" => "application/pdf" }, attachments[0])
    assert_equal raw, attachments[0]["content"].unpack1("m0")
    assert_equal({ "filename" => "note.txt", "content" => "aGk=" }, attachments[1])
  end

  def test_keyword_fields_and_hash_merge
    transport = FakeTransport.json(200, ACCEPTED)
    client(transport).emails.send({ from: "a@acme.com", to: ["b@example.com"] }, subject: "Hi", html: "<p>Hi</p>",
                                                                                 tags: { type: "welcome" }, reply_to: nil)
    assert_equal({ "from" => "a@acme.com", "to" => ["b@example.com"], "subject" => "Hi", "html" => "<p>Hi</p>",
                   "tags" => { "type" => "welcome" } }, transport.last_body)
  end

  def test_send_batch_unwraps_data
    transport = FakeTransport.json(200, { data: [ACCEPTED, ACCEPTED.merge(id: "msg_2")] })
    result = client(transport).emails.send_batch([{ from: "a@acme.com", to: "b@example.com", text: "x" }] * 2,
                                                 idempotency_key: "batch-1")
    assert_equal %w[msg_1 msg_2], result.map { |e| e[:id] }
    assert_equal 2, transport.last_body["emails"].length
    assert_equal "batch-1", transport.requests[0][:headers]["Idempotency-Key"]
  end

  def test_a_retry_after_over_60_seconds_gives_up_after_one_request
    response = Mailhive::Response.new(429, { "retry-after" => "120", "x-request-id" => "req_9" },
                                      JSON.generate(error: { code: "rate_limited", message: "Slow down" }))
    transport = FakeTransport.new([response])
    sleeps = []
    error = assert_raises(Mailhive::RateLimitError) { client(transport, sleeps: sleeps).emails.send(to: "b@example.com") }
    assert_equal 1, transport.requests.length
    assert_empty sleeps
    assert_equal 120.0, error.retry_after
    assert_equal "req_9", error.request_id
    assert_equal "Slow down", error.message
  end

  def test_backoff_without_retry_after
    body = JSON.generate(error: { code: "internal_server_error", message: "Oops" })
    transport = FakeTransport.new([Mailhive::Response.new(500, {}, body)])
    sleeps = []
    assert_raises(Mailhive::APIError) { client(transport, sleeps: sleeps).emails.send(to: "b@example.com") }
    assert_equal 3, transport.requests.length
    assert_equal 1, transport.requests.map { |r| r[:headers]["Idempotency-Key"] }.uniq.length
    assert_equal 2, sleeps.length
    assert(sleeps[0].between?(0.25, 0.5), sleeps.inspect)
    assert(sleeps[1].between?(0.5, 1.0), sleeps.inspect)
  end

  def test_backoff_is_capped_at_eight_seconds
    100.times do
      assert Mailhive::Client.backoff(10).between?(4.0, 8.0)
    end
  end

  def test_a_timeout_becomes_a_connection_error_after_the_retries
    transport = FakeTransport.new([Net::ReadTimeout])
    sleeps = []
    error = assert_raises(Mailhive::ConnectionError) do
      client(transport, sleeps: sleeps, timeout: 5).emails.send(to: "b@example.com")
    end
    assert_equal "The Mailhive API didn't answer within 5 seconds.", error.message
    assert_equal 3, transport.requests.length
    assert_equal 2, sleeps.length
    assert_equal 1, transport.requests.map { |r| r[:headers]["Idempotency-Key"] }.uniq.length
    assert_kind_of Net::ReadTimeout, error.cause
  end

  def test_a_refused_connection_names_the_base_url
    transport = FakeTransport.new([Errno::ECONNREFUSED])
    error = assert_raises(Mailhive::ConnectionError) do
      client(transport, max_retries: 0, base_url: "http://127.0.0.1:1/v1").emails.send(to: "b@example.com")
    end
    assert_equal "Couldn't reach the Mailhive API at http://127.0.0.1:1/v1.", error.message
    assert_equal 1, transport.requests.length
  end

  def test_the_real_transport_reports_a_refused_connection
    server = TCPServer.new("127.0.0.1", 0)
    port = server.addr[1]
    server.close
    client = Mailhive::Client.new(api_key: "mhs_x", base_url: "http://127.0.0.1:#{port}/v1", max_retries: 0)
    assert_raises(Mailhive::ConnectionError) { client.emails.get("msg_1") }
  end

  def test_a_non_json_error_is_http_error
    transport = FakeTransport.new([Mailhive::Response.new(418, { "x-request-id" => "req_h" }, "<html>teapot</html>")])
    error = assert_raises(Mailhive::APIError) { client(transport).emails.get("x") }
    assert_equal Mailhive::APIError, error.class
    assert_equal "http_error", error.code
    assert_equal "HTTP 418", error.message
    assert_equal "req_h", error.request_id
    refute_includes error.inspect, "mhs_"
  end

  def test_400_is_a_validation_error_and_4xx_is_not_retried
    transport = FakeTransport.new([Mailhive::Response.new(400, {}, JSON.generate(error: { code: "bad_request", message: "Bad", details: [{ loc: ["to"] }] }))])
    error = assert_raises(Mailhive::ValidationError) { client(transport).emails.send(to: "x") }
    assert_equal [{ loc: ["to"] }], error.details
    assert_equal 1, transport.requests.length
  end
end
