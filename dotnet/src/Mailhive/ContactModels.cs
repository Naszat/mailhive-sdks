using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Mailhive.Internal;

namespace Mailhive
{
    /// <summary>A list of contacts that broadcasts go to.</summary>
    public sealed class Audience
    {
        /// <summary>The audience's id.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>The audience's name.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>When it was made (not returned by a rename).</summary>
        [JsonPropertyName("created_at")]
        [JsonConverter(typeof(UtcDateTimeOffsetConverter))]
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>Everyone in the audience.</summary>
        [JsonPropertyName("contacts")]
        public int Contacts { get; set; }

        /// <summary>Of those, how many a broadcast would reach.</summary>
        [JsonPropertyName("mailable")]
        public int Mailable { get; set; }
    }

    /// <summary>A person in an audience.</summary>
    public class Contact
    {
        /// <summary>The contact's id.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>The audience they're in.</summary>
        [JsonPropertyName("audience_id")]
        public string AudienceId { get; set; } = string.Empty;

        /// <summary>Their address, in lower case.</summary>
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        /// <summary>First name, or <c>null</c>.</summary>
        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        /// <summary>Last name, or <c>null</c>.</summary>
        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        /// <summary>Custom fields.</summary>
        [JsonPropertyName("fields")]
        public IReadOnlyDictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();

        /// <summary><c>subscribed</c>, <c>unsubscribed</c> or <c>pending</c> (waiting to confirm a double opt-in).</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        /// <summary>On the suppression list (bounce, spam complaint or unsubscribe): never mailed.</summary>
        [JsonPropertyName("suppressed")]
        public bool Suppressed { get; set; }

        /// <summary>Subscribed and not suppressed: a broadcast would reach them.</summary>
        [JsonPropertyName("mailable")]
        public bool Mailable { get; set; }

        /// <summary><c>manual</c>, <c>import</c>, <c>api</c> or <c>form</c>.</summary>
        [JsonPropertyName("source")]
        public string? Source { get; set; }

        /// <summary>When they were added.</summary>
        [JsonPropertyName("created_at")]
        [JsonConverter(typeof(UtcDateTimeOffsetConverter))]
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>When they unsubscribed, or <c>null</c>.</summary>
        [JsonPropertyName("unsubscribed_at")]
        [JsonConverter(typeof(UtcDateTimeOffsetConverter))]
        public DateTimeOffset? UnsubscribedAt { get; set; }

        /// <summary>True when a test key made the call: nothing was saved or sent.</summary>
        [JsonPropertyName("test")]
        public bool Test { get; set; }
    }

    /// <summary>A contact with the latest emails sent to them.</summary>
    public sealed class ContactDetail : Contact
    {
        /// <summary>The latest emails sent to them.</summary>
        [JsonPropertyName("history")]
        public IReadOnlyList<Email> History { get; set; } = Array.Empty<Email>();
    }

    /// <summary>One page of contacts.</summary>
    public sealed class ContactPage
    {
        /// <summary>The contacts on this page.</summary>
        [JsonPropertyName("items")]
        public IReadOnlyList<Contact> Items { get; set; } = Array.Empty<Contact>();

        /// <summary>The page number, from 1.</summary>
        [JsonPropertyName("page")]
        public int Page { get; set; }

        /// <summary>How many fit on a page.</summary>
        [JsonPropertyName("page_size")]
        public int PageSize { get; set; }

        /// <summary>Everyone who matches, across all pages.</summary>
        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    /// <summary>
    /// Emails the person a confirmation link first: they're subscribed only if they click it. Until then they're
    /// <c>pending</c> and never mailed.
    /// </summary>
    public sealed class DoubleOptIn
    {
        /// <summary>Required. On a verified sending domain, e.g. <c>Acme &lt;news@example.com&gt;</c>.</summary>
        [JsonPropertyName("from")]
        public string From { get; set; } = string.Empty;

        /// <summary>The subject. Defaults to "Please confirm your subscription".</summary>
        [JsonPropertyName("subject")]
        public string? Subject { get; set; }
    }

    /// <summary>
    /// A person to add or update. The JSON names match the <see href="https://mailhive.africa/docs/api/contacts">API
    /// reference</see>; properties left <c>null</c> aren't sent, so an update keeps them.
    /// </summary>
    public sealed class ContactInput
    {
        /// <summary>Required.</summary>
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        /// <summary>Leave <c>null</c> to keep it; <c>""</c> clears it.</summary>
        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        /// <summary>Leave <c>null</c> to keep it; <c>""</c> clears it.</summary>
        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        /// <summary>Custom fields, merged into the contact's; an empty value removes one.</summary>
        [JsonPropertyName("fields")]
        public IDictionary<string, string>? Fields { get; set; }

        /// <summary>
        /// <c>subscribed</c> or <c>unsubscribed</c>. New contacts default to subscribed. Someone who unsubscribed or
        /// reported spam can't be set to subscribed: use <see cref="DoubleOptIn"/>.
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        /// <summary>Email them a confirmation link instead of subscribing them straight away.</summary>
        [JsonPropertyName("double_opt_in")]
        public DoubleOptIn? DoubleOptIn { get; set; }
    }

    /// <summary>Changes to an existing contact. Anything left <c>null</c> stays as it is.</summary>
    public sealed class ContactUpdate
    {
        /// <summary>Leave <c>null</c> to keep it; <c>""</c> clears it.</summary>
        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        /// <summary>Leave <c>null</c> to keep it; <c>""</c> clears it.</summary>
        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        /// <summary>
        /// Replaces the contact's custom fields: <c>null</c> leaves them, and an empty dictionary removes them all.
        /// Use <see cref="ContactsResource.UpsertAsync"/> to merge instead.
        /// </summary>
        [JsonPropertyName("fields")]
        public IDictionary<string, string>? Fields { get; set; }

        /// <summary>
        /// <c>true</c> unsubscribes the contact. Only they can undo it, by confirming a double opt-in email.
        /// </summary>
        [JsonPropertyName("unsubscribe")]
        public bool? Unsubscribe { get; set; }
    }

    /// <summary>Filters for <see cref="ContactsResource.ListAsync"/>. Anything left unset is left out.</summary>
    public sealed class ListContactsOptions
    {
        /// <summary>One person, by exact address (case-insensitive).</summary>
        public string? Email { get; set; }

        /// <summary>Searches names and addresses.</summary>
        public string? Q { get; set; }

        /// <summary><c>subscribed</c>, <c>unsubscribed</c> or <c>pending</c>.</summary>
        public string? Status { get; set; }

        /// <summary>The page number, from 1.</summary>
        public int? Page { get; set; }

        /// <summary>How many per page: up to 100 (default 20).</summary>
        public int? PageSize { get; set; }
    }

    /// <summary>The result for one contact of a batch.</summary>
    public sealed class ContactBatchEntry
    {
        /// <summary>Its position in the list you sent.</summary>
        [JsonPropertyName("index")]
        public int Index { get; set; }

        /// <summary>The address you sent.</summary>
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        /// <summary>Whether it was saved.</summary>
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        /// <summary>Whether this call made the contact (rather than updating one).</summary>
        [JsonPropertyName("created")]
        public bool Created { get; set; }

        /// <summary>The saved contact, when <see cref="Ok"/>.</summary>
        [JsonPropertyName("contact")]
        public Contact? Contact { get; set; }

        /// <summary>Why it failed, or <c>null</c>.</summary>
        [JsonPropertyName("error")]
        public ContactBatchError? Error { get; set; }
    }

    /// <summary>Why one entry of a batch failed.</summary>
    public sealed class ContactBatchError
    {
        /// <summary>A stable code such as <c>invalid_email</c>.</summary>
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        /// <summary>A human-readable message.</summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>The outcome of <see cref="ContactsResource.UpsertBatchAsync"/>: one entry per contact sent, in order.</summary>
    public sealed class ContactBatchResult
    {
        /// <summary>How many were added.</summary>
        [JsonPropertyName("created")]
        public int Created { get; set; }

        /// <summary>How many were updated.</summary>
        [JsonPropertyName("updated")]
        public int Updated { get; set; }

        /// <summary>How many failed.</summary>
        [JsonPropertyName("failed")]
        public int Failed { get; set; }

        /// <summary>A failure here doesn't throw: check <see cref="ContactBatchEntry.Ok"/>.</summary>
        [JsonPropertyName("results")]
        public IReadOnlyList<ContactBatchEntry> Results { get; set; } = Array.Empty<ContactBatchEntry>();
    }
}
