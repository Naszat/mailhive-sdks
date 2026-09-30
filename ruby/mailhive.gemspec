# frozen_string_literal: true

require_relative "lib/mailhive/version"

Gem::Specification.new do |spec|
  spec.name = "mailhive"
  spec.version = Mailhive::VERSION
  spec.authors = ["Naszat"]
  spec.summary = "The official Ruby SDK for Mailhive Send, with an ActionMailer delivery method."
  spec.description = "Send transactional email through the Mailhive Send API from Ruby and Rails: " \
                     "safe retries with idempotency keys, typed errors, webhook verification and an " \
                     "ActionMailer delivery method. No runtime dependencies."
  spec.homepage = "https://mailhive.africa/docs/sdks/ruby"
  spec.license = "MIT"
  spec.required_ruby_version = ">= 3.0"

  spec.metadata = {
    "homepage_uri" => spec.homepage,
    "source_code_uri" => "https://github.com/Naszat/mailhive-sdks/tree/main/ruby",
    "changelog_uri" => "https://github.com/Naszat/mailhive-sdks/blob/main/ruby/CHANGELOG.md",
    "bug_tracker_uri" => "https://github.com/Naszat/mailhive-sdks/issues",
    "rubygems_mfa_required" => "true"
  }

  spec.files = Dir["lib/**/*.rb"] + %w[README.md CHANGELOG.md LICENSE]
  spec.require_paths = ["lib"]
end
