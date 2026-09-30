# frozen_string_literal: true

require "test_helper"

class WebhookTest < Minitest::Test
  VECTORS = TestSupport.spec("webhook-vectors.json")

  VECTORS["cases"].each_with_index do |vector, index|
    define_method("test_vector_#{format("%02d", index)}_#{vector["name"].gsub(/\W+/, "_")}") do
      verify = lambda do
        Mailhive::Webhook.verify(vector["payload"], vector["header"], vector["secret"],
                                 tolerance: VECTORS["tolerance_seconds"], now: vector["now"])
      end
      if vector["valid"]
        assert_equal JSON.parse(vector["payload"], symbolize_names: true), verify.call
      else
        error = assert_raises(Mailhive::WebhookVerificationError) { verify.call }
        assert_equal vector["reason"], error.reason
        assert_kind_of Mailhive::Error, error
      end
    end
  end

  def test_the_event_has_symbol_keys
    vector = VECTORS["cases"].find { |v| v["valid"] }
    event = Mailhive::Webhook.verify(vector["payload"], vector["header"], vector["secret"], now: vector["now"])
    assert_equal "email.delivered", event[:type]
  end

  def test_a_nil_header_is_a_header_error
    error = assert_raises(Mailhive::WebhookVerificationError) { Mailhive::Webhook.verify("{}", nil, "whsec_x") }
    assert_equal "header", error.reason
  end

  def test_parsed_json_is_refused
    assert_raises(TypeError) { Mailhive::Webhook.verify({ "type" => "x" }, "t=1,v1=#{"a" * 64}", "whsec_x") }
  end

  def test_uses_the_current_time_by_default
    secret = "whsec_now"
    payload = '{"type":"email.sent"}'
    t = Time.now.to_i
    signature = OpenSSL::HMAC.hexdigest("SHA256", secret, "#{t}.#{payload}")
    assert_equal({ type: "email.sent" }, Mailhive::Webhook.verify(payload, "t=#{t},v1=#{signature}", secret))
  end
end
