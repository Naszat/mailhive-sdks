# frozen_string_literal: true

require "test_helper"

class ContactsTest < Minitest::Test
  include TestSupport

  CONTACT = { id: "c1", email: "ada@example.com" }.freeze

  def client(transport)
    Mailhive::Client.new(api_key: "mhs_x", transport: transport)
  end

  def test_list_percent_encodes_the_query_and_leaves_out_unset_filters
    transport = FakeTransport.json(200, { items: [], page: 1, page_size: 20, total: 0 })
    client(transport).contacts.list("aud 1", email: "a+b@example.com", page: 2)
    request = transport.requests[0]
    assert_equal "GET", request[:method]
    assert_equal "https://api.mailhive.africa/v1/send/audiences/aud%201/contacts?email=a%2Bb%40example.com&page=2", request[:url]
    refute request[:headers].key?("Idempotency-Key")
    assert_nil request[:body]
  end

  def test_list_with_no_filters_has_no_query_string
    transport = FakeTransport.json(200, { items: [] })
    client(transport).contacts.list("aud_1")
    assert_equal "https://api.mailhive.africa/v1/send/audiences/aud_1/contacts", transport.requests[0][:url]
  end

  def test_upsert_is_a_put_with_string_keys_and_no_nils_and_no_idempotency_key
    transport = FakeTransport.json(200, CONTACT)
    client(transport).contacts.upsert("aud_1", email: "ada@example.com", first_name: nil, fields: { plan: "pro" },
                                               double_opt_in: { from: "Acme <news@acme.com>" })
    request = transport.requests[0]
    assert_equal "PUT", request[:method]
    assert_equal({ "email" => "ada@example.com", "fields" => { "plan" => "pro" },
                   "double_opt_in" => { "from" => "Acme <news@acme.com>" } }, transport.last_body)
    refute request[:headers].key?("Idempotency-Key")
  end

  def test_add_and_batch_are_posts_with_an_idempotency_key
    transport = FakeTransport.json(201, CONTACT)
    client(transport).contacts.add("aud_1", { email: "ada@example.com" }, idempotency_key: "signup-1")
    assert_equal "signup-1", transport.requests[0][:headers]["Idempotency-Key"]
    transport = FakeTransport.json(200, { created: 1, updated: 0, failed: 0, results: [] })
    client(transport).contacts.upsert_batch("aud_1", [{ email: "a@example.com" }, { "email" => "b@example.com" }])
    assert_equal "POST", transport.requests[0][:method]
    assert transport.requests[0][:url].end_with?("/contacts/batch")
    assert_equal [{ "email" => "a@example.com" }, { "email" => "b@example.com" }], transport.last_body["contacts"]
  end

  def test_update_can_clear_a_name_and_remove_all_fields
    transport = FakeTransport.json(200, CONTACT)
    client(transport).contacts.update("c1", first_name: "", fields: {})
    assert_equal({ "first_name" => "", "fields" => {} }, transport.last_body)
    assert_equal "PATCH", transport.requests[0][:method]
  end

  def test_unsubscribe_and_delete
    transport = FakeTransport.json(200, CONTACT)
    client(transport).contacts.unsubscribe("c1")
    assert_equal({ "unsubscribe" => true }, transport.last_body)
    transport = FakeTransport.new([Mailhive::Response.new(204, {}, "")])
    assert_nil client(transport).contacts.delete("c1")
    assert_equal "DELETE", transport.requests[0][:method]
    assert_equal "https://api.mailhive.africa/v1/send/contacts/c1", transport.requests[0][:url]
  end
end
