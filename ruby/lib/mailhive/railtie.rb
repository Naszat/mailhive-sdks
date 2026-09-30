# frozen_string_literal: true

module Mailhive
  # Registers the :mailhive ActionMailer delivery method. Settings come from
  # config.action_mailer.mailhive_settings = { api_key: …, base_url: … },
  # falling back to MAILHIVE_API_KEY and MAILHIVE_BASE_URL.
  class Railtie < ::Rails::Railtie
    # Before ActionMailer applies config.action_mailer.*, so that
    # mailhive_settings= exists by then.
    initializer "mailhive.action_mailer", before: "action_mailer.set_configs" do
      ActiveSupport.on_load(:action_mailer) { Mailhive::ActionMailer.install(self) }
    end
  end
end
