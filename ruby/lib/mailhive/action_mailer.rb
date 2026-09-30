# frozen_string_literal: true

module Mailhive
  # Sending ActionMailer (and plain Mail gem) messages through Mailhive Send.
  #
  #   # config/environments/production.rb
  #   config.action_mailer.delivery_method = :mailhive
  #   config.action_mailer.mailhive_settings = { api_key: ENV["MAILHIVE_API_KEY"] }
  #
  # The Railtie registers the :mailhive delivery method. Outside Rails, call
  # Mailhive::ActionMailer.install, or use the class directly with the Mail
  # gem: +mail.delivery_method Mailhive::ActionMailer::DeliveryMethod, api_key: "mhs_…"+.
  #
  # This file never loads the mail gem: it works on whatever Mail::Message
  # it's handed.
  module ActionMailer
    IDEMPOTENCY_HEADER = "X-Mailhive-Idempotency-Key"
    TAGS_HEADER = "X-Mailhive-Tags"
    EMAIL_ID_HEADER = "X-Mailhive-Email-Id"

    # Non-X- headers the API accepts.
    PASSED_HEADERS = %w[in-reply-to references list-unsubscribe list-unsubscribe-post].freeze

    # Registers :mailhive on ActionMailer::Base. Safe to call twice.
    def self.install(base = ::ActionMailer::Base)
      base.add_delivery_method(:mailhive, DeliveryMethod) unless base.delivery_methods.key?(:mailhive)
    end

    # The API request for one Mail::Message.
    def self.params(mail)
      params = {
        "from" => addresses(mail, :from).first,
        "to" => recipients(mail),
        "subject" => mail.subject.to_s
      }
      { "cc" => :cc, "bcc" => :bcc, "reply_to" => :reply_to }.each do |key, field|
        list = addresses(mail, field)
        params[key] = list unless list.empty?
      end
      params.merge!(bodies(mail))

      headers = {}
      mail.header.fields.each do |field|
        name = field.name.to_s
        lower = name.downcase
        next if lower.start_with?("x-mailhive-")
        next unless lower.start_with?("x-") || PASSED_HEADERS.include?(lower)

        headers[name] = field.decoded.to_s
      end
      params["headers"] = headers unless headers.empty?

      tags = tags(mail)
      params["tags"] = tags if tags

      attachments = mail.attachments.map do |part|
        {
          "filename" => part.filename || "attachment",
          "content" => part.decoded,
          "content_type" => part.mime_type || "application/octet-stream"
        }
      end
      params["attachments"] = attachments unless attachments.empty?
      params
    end

    def self.header_value(mail, name)
      field = mail.header[name]
      field = field.last if field.is_a?(Array)
      value = field&.decoded.to_s.strip
      value.nil? || value.empty? ? nil : value
    end

    def self.addresses(mail, name)
      field = mail[name]
      return [] if field.nil?

      Array(field.respond_to?(:formatted) ? field.formatted : field.decoded).compact.map(&:to_s)
    rescue StandardError
      Array(mail.public_send(name)).compact.map(&:to_s)
    end

    # To, or the envelope's recipients (less cc and bcc) when there's no To.
    def self.recipients(mail)
      to = addresses(mail, :to)
      return to unless to.empty?

      envelope = Array(mail.smtp_envelope_to)
      rest = envelope - Array(mail.cc) - Array(mail.bcc)
      rest.empty? ? envelope : rest
    end

    def self.bodies(mail)
      if mail.multipart?
        result = {}
        result["html"] = mail.html_part.decoded if mail.html_part
        result["text"] = mail.text_part.decoded if mail.text_part
        result
      elsif mail.body.to_s.empty?
        {}
      elsif mail.mime_type == "text/html"
        { "html" => mail.decoded }
      else
        { "text" => mail.decoded }
      end
    end

    def self.tags(mail)
      raw = header_value(mail, TAGS_HEADER)
      return nil if raw.nil?

      tags = begin
        JSON.parse(raw)
      rescue JSON::ParserError
        nil
      end
      raise Error, "#{TAGS_HEADER} must be a JSON object, like {\"type\":\"receipt\"}." unless tags.is_a?(Hash)

      tags.to_h { |key, value| [key.to_s, value.to_s] }
    end
    private_class_method :addresses, :recipients, :bodies, :tags

    # An ActionMailer / Mail delivery method. ActionMailer builds one per
    # message, from config.action_mailer.mailhive_settings: api_key,
    # base_url, timeout, max_retries (or client: a Mailhive::Client).
    # Without an api_key, MAILHIVE_API_KEY is used.
    class DeliveryMethod
      attr_accessor :settings

      def initialize(settings = {})
        @settings = (settings || {}).to_h.transform_keys(&:to_sym)
      end

      # Sends the message, sets its X-Mailhive-Email-Id header to the
      # email's id, and returns the accepted email.
      def deliver!(mail)
        accepted = client.emails.send(
          ActionMailer.params(mail),
          idempotency_key: ActionMailer.header_value(mail, IDEMPOTENCY_HEADER)
        )
        mail[EMAIL_ID_HEADER] = accepted[:id]
        accepted
      end

      def client
        @client ||= settings[:client] || Client.new(
          **settings.slice(:api_key, :base_url, :timeout, :max_retries, :transport, :sleeper)
        )
      end

      def inspect
        "#<#{self.class.name}>"
      end
    end
  end
end
