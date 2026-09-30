# frozen_string_literal: true

require "test_helper"
require "mail"

class ActionMailerDeliveryMethodTest < Minitest::Test
  include TestSupport

  ACCEPTED = { id: "msg_7", status: "queued", suppressed: [], test: true }.freeze
  PDF = "%PDF-1.4\x00\x01\xFF binary".b

  def delivery(transport)
    client = Mailhive::Client.new(api_key: "mhs_test_rails", transport: transport, sleeper: ->(_) {})
    Mailhive::ActionMailer::DeliveryMethod.new(client: client)
  end

  def receipt
    mail = Mail.new do
      from "Acme <hello@acme.com>"
      to "Ada Lovelace <ada@example.com>"
      cc ["bob@example.com", "Carol <carol@example.com>"]
      bcc "audit@acme.com"
      reply_to "support@acme.com"
      subject "Your receipt"
      text_part { body "Thanks for your order." }
      html_part do
        content_type "text/html; charset=UTF-8"
        body "<p>Thanks for your order.</p>"
      end
    end
    mail["X-Campaign"] = "autumn"
    mail["List-Unsubscribe"] = "<https://acme.com/unsubscribe>"
    mail["X-Mailhive-Idempotency-Key"] = "order-1042-receipt"
    mail["X-Mailhive-Tags"] = '{"type":"receipt","order":1042}'
    mail.attachments["receipt.pdf"] = { mime_type: "application/pdf", content: PDF }
    mail
  end

  def test_delivers_a_multipart_message
    transport = FakeTransport.json(200, ACCEPTED)
    mail = receipt
    result = delivery(transport).deliver!(mail)

    assert_equal ACCEPTED, result
    assert_equal 1, transport.requests.length
    request = transport.requests[0]
    assert_equal "Bearer mhs_test_rails", request[:headers]["Authorization"]
    assert_equal "order-1042-receipt", request[:headers]["Idempotency-Key"]

    body = transport.last_body
    assert_equal "Acme <hello@acme.com>", body["from"]
    assert_equal ["Ada Lovelace <ada@example.com>"], body["to"]
    assert_equal ["bob@example.com", "Carol <carol@example.com>"], body["cc"]
    assert_equal ["audit@acme.com"], body["bcc"]
    assert_equal ["support@acme.com"], body["reply_to"]
    assert_equal "Your receipt", body["subject"]
    assert_equal "Thanks for your order.", body["text"]
    assert_equal "<p>Thanks for your order.</p>", body["html"]
    assert_equal({ "X-Campaign" => "autumn", "List-Unsubscribe" => "<https://acme.com/unsubscribe>" }, body["headers"])
    assert_equal({ "type" => "receipt", "order" => "1042" }, body["tags"])

    assert_equal 1, body["attachments"].length
    attachment = body["attachments"][0]
    assert_equal "receipt.pdf", attachment["filename"]
    assert_equal "application/pdf", attachment["content_type"]
    assert_equal PDF, attachment["content"].unpack1("m0")

    assert_equal "msg_7", mail["X-Mailhive-Email-Id"].value
  end

  def test_single_part_bodies_go_by_content_type
    html = Mail.new do
      from "hello@acme.com"
      to "ada@example.com"
      subject "Hi"
      content_type "text/html; charset=UTF-8"
      body "<p>Héllo</p>"
    end
    transport = FakeTransport.json(200, ACCEPTED)
    delivery(transport).deliver!(html)
    body = transport.last_body
    assert_equal "<p>Héllo</p>", body["html"]
    refute body.key?("text")
    refute body.key?("headers")
    refute body.key?("tags")
    assert_match(/\A\h{8}-/, transport.requests[0][:headers]["Idempotency-Key"])

    text = Mail.new(from: "hello@acme.com", to: "ada@example.com", subject: "Hi", body: "Hello")
    transport = FakeTransport.json(200, ACCEPTED)
    delivery(transport).deliver!(text)
    assert_equal({ "from" => "hello@acme.com", "to" => ["ada@example.com"], "subject" => "Hi", "text" => "Hello" },
                 transport.last_body)
  end

  def test_invalid_tags_are_refused_before_sending
    mail = Mail.new(from: "hello@acme.com", to: "ada@example.com", subject: "Hi", body: "Hello")
    mail["X-Mailhive-Tags"] = "receipt"
    transport = FakeTransport.json(200, ACCEPTED)
    assert_raises(Mailhive::Error) { delivery(transport).deliver!(mail) }
    assert_empty transport.requests
  end

  def test_api_errors_propagate
    transport = FakeTransport.json(422, { error: { code: "validation_error", message: "Bad", details: [{}] } })
    mail = Mail.new(from: "hello@acme.com", to: "ada@example.com", subject: "Hi", body: "Hello")
    assert_raises(Mailhive::ValidationError) { delivery(transport).deliver!(mail) }
  end

  def test_settings_build_a_client_and_fall_back_to_the_environment
    with_env("MAILHIVE_API_KEY" => "mhs_env_key", "MAILHIVE_BASE_URL" => nil) do
      method = Mailhive::ActionMailer::DeliveryMethod.new("base_url" => "https://api-beta.mailhive.africa/v1")
      assert_equal "https://api-beta.mailhive.africa/v1", method.client.base_url
      refute_includes method.client.inspect, "mhs_env_key"
    end
  end

  def test_works_as_a_mail_gem_delivery_method
    transport = FakeTransport.json(200, ACCEPTED)
    mail = Mail.new(from: "hello@acme.com", to: "ada@example.com", subject: "Hi", body: "Hello")
    mail.delivery_method Mailhive::ActionMailer::DeliveryMethod, api_key: "mhs_mail_gem", transport: transport
    mail.deliver!
    assert_equal "Bearer mhs_mail_gem", transport.requests[0][:headers]["Authorization"]
    assert_equal "msg_7", mail["X-Mailhive-Email-Id"].value
  end
end

begin
  require "action_mailer"
rescue LoadError
  nil
end

if defined?(ActionMailer::Base)
  # A real ActionMailer mailer delivering through :mailhive.
  class ActionMailerIntegrationTest < Minitest::Test
    class ReceiptMailer < ActionMailer::Base
      default from: "Acme <hello@acme.com>"

      def receipt
        headers["X-Mailhive-Idempotency-Key"] = "order-1042-receipt"
        headers["X-Mailhive-Tags"] = JSON.generate(type: "receipt")
        attachments["receipt.txt"] = "Total: 10"
        mail(to: "ada@example.com", subject: "Your receipt") do |format|
          format.text { render plain: "Thanks for your order." }
          format.html { render html: "<p>Thanks for your order.</p>".html_safe }
        end
      end
    end

    def setup
      Mailhive::ActionMailer.install
      Mailhive::ActionMailer.install # twice is fine
      @transport = TestSupport::FakeTransport.json(200, { id: "msg_rails", status: "queued", suppressed: [], test: false })
      ActionMailer::Base.delivery_method = :mailhive
      ActionMailer::Base.mailhive_settings = { api_key: "mhs_rails", transport: @transport }
      ActionMailer::Base.perform_deliveries = true
      ActionMailer::Base.raise_delivery_errors = true
    end

    def test_a_mailer_delivers_through_mailhive
      message = ReceiptMailer.receipt.deliver_now
      assert_equal 1, @transport.requests.length
      request = @transport.requests[0]
      assert_equal "Bearer mhs_rails", request[:headers]["Authorization"]
      assert_equal "order-1042-receipt", request[:headers]["Idempotency-Key"]
      body = @transport.last_body
      assert_equal "Acme <hello@acme.com>", body["from"]
      assert_equal ["ada@example.com"], body["to"]
      assert_equal "Thanks for your order.", body["text"]
      assert_equal "<p>Thanks for your order.</p>", body["html"]
      assert_equal({ "type" => "receipt" }, body["tags"])
      assert_equal "Total: 10", body["attachments"][0]["content"].unpack1("m0")
      assert_equal "msg_rails", message["X-Mailhive-Email-Id"].value
    end
  end
end
