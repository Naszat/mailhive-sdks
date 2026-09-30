# frozen_string_literal: true

require "json"
require "net/http"
require "openssl"
require "securerandom"
require "uri"

require_relative "mailhive/version"
require_relative "mailhive/errors"
require_relative "mailhive/transport"
require_relative "mailhive/emails"
require_relative "mailhive/client"
require_relative "mailhive/webhook"
require_relative "mailhive/action_mailer"
require_relative "mailhive/railtie" if defined?(Rails::Railtie)

# The official Ruby SDK for Mailhive Send: Mailhive::Client, Mailhive::Webhook
# and an ActionMailer delivery method.
module Mailhive
end
