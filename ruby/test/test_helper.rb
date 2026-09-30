# frozen_string_literal: true

$LOAD_PATH.unshift File.expand_path("../lib", __dir__)

require "minitest/autorun"
require "json"
require "net/http"
require "mailhive"

module TestSupport
  SPEC_DIR = File.expand_path("../../spec", __dir__)
  MOCK_SERVER = File.expand_path("../../mock-server/server.mjs", __dir__)

  def self.spec(name)
    JSON.parse(File.read(File.join(SPEC_DIR, name)))
  end

  # The shared mock API (mock-server/server.mjs) on a free port, started once.
  module MockServer
    def self.url
      @url ||= begin
        io = IO.popen(["node", MOCK_SERVER, "--port", "0"])
        @pid = io.pid
        line = io.gets.to_s.strip
        raise "The mock server didn't start (is node on PATH?)" if line.empty?

        Minitest.after_run do
          Process.kill("TERM", @pid)
          Process.wait(@pid)
        rescue StandardError
          nil
        end
        line.split(" ").last
      end
    end

    def self.reset
      Net::HTTP.post(URI("#{url}/__reset"), "")
    end

    def self.requests
      JSON.parse(Net::HTTP.get(URI("#{url}/__requests")))
    end
  end

  # Records requests and answers from a queue of responses or exceptions.
  class FakeTransport
    attr_reader :requests

    def self.json(status, body, headers = {})
      new([Mailhive::Response.new(status, { "content-type" => "application/json" }.merge(headers), JSON.generate(body))])
    end

    def initialize(responses)
      @responses = responses
      @requests = []
    end

    def call(method, url, headers, body, timeout)
      @requests << { method: method, url: url, headers: headers, body: body, timeout: timeout }
      answer = @responses.length > 1 ? @responses.shift : @responses.first
      raise answer if answer.is_a?(Exception) || (answer.is_a?(Class) && answer <= Exception)

      answer
    end

    def last_body
      JSON.parse(@requests.last[:body])
    end
  end

  def with_env(values)
    saved = values.keys.to_h { |key| [key, ENV.fetch(key, nil)] }
    values.each { |key, value| value.nil? ? ENV.delete(key) : ENV[key] = value }
    yield
  ensure
    saved.each { |key, value| value.nil? ? ENV.delete(key) : ENV[key] = value }
  end
end
