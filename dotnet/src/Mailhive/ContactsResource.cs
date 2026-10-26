using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Mailhive
{
    /// <summary>Audiences, the lists your broadcasts go to: <c>client.Audiences</c>. Needs an API key with Full access.</summary>
    public sealed class AudiencesResource
    {
        private readonly MailhiveClient _client;

        internal AudiencesResource(MailhiveClient client)
        {
            _client = client;
        }

        /// <summary>The organization's audiences.</summary>
        public Task<IReadOnlyList<Audience>> ListAsync(CancellationToken cancellationToken = default) =>
            _client.RequestAsync<IReadOnlyList<Audience>>(HttpMethod.Get, "/send/audiences", null, null, cancellationToken);

        /// <summary>Makes an audience.</summary>
        public Task<Audience> CreateAsync(string name, SendOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("An audience name is required.", nameof(name));
            }
            return _client.RequestAsync<Audience>(HttpMethod.Post, "/send/audiences", new NameBody(name), options?.IdempotencyKey, cancellationToken);
        }

        /// <summary>Changes an audience's name.</summary>
        public Task<Audience> RenameAsync(string id, string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("An audience id is required.", nameof(id));
            }
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("An audience name is required.", nameof(name));
            }
            return _client.RequestAsync<Audience>(
                new HttpMethod("PATCH"), "/send/audiences/" + Uri.EscapeDataString(id), new NameBody(name), null, cancellationToken);
        }

        private sealed class NameBody
        {
            public NameBody(string name) => Name = name;

            [JsonPropertyName("name")]
            public string Name { get; }
        }
    }

    /// <summary>
    /// The people in an audience: <c>client.Contacts</c>. Needs an API key with Full access.
    /// <para>
    /// Someone who unsubscribed, or reported your email as spam, can't be made subscribed again by these calls: you get
    /// a <see cref="ConflictException"/> with code <c>contact_unsubscribed</c>. Set <see cref="ContactInput.DoubleOptIn"/>
    /// to email them a confirmation link instead.
    /// </para>
    /// </summary>
    public sealed class ContactsResource
    {
        private static readonly HttpMethod Patch = new HttpMethod("PATCH");

        private readonly MailhiveClient _client;

        internal ContactsResource(MailhiveClient client)
        {
            _client = client;
        }

        /// <summary>A page of contacts. Set <see cref="ListContactsOptions.Email"/> to find one person.</summary>
        public Task<ContactPage> ListAsync(string audienceId, ListContactsOptions? options = null, CancellationToken cancellationToken = default)
        {
            RequireId(audienceId, nameof(audienceId));
            return _client.RequestAsync<ContactPage>(
                HttpMethod.Get, AudiencePath(audienceId) + QueryString(options), null, null, cancellationToken);
        }

        /// <summary>Adds one person. A <see cref="ConflictException"/> <c>contact_exists</c> if they're already there: use <see cref="UpsertAsync"/>.</summary>
        public Task<Contact> AddAsync(string audienceId, ContactInput contact, SendOptions? options = null, CancellationToken cancellationToken = default)
        {
            RequireId(audienceId, nameof(audienceId));
            if (contact is null)
            {
                throw new ArgumentNullException(nameof(contact));
            }
            return _client.RequestAsync<Contact>(HttpMethod.Post, AudiencePath(audienceId), contact, options?.IdempotencyKey, cancellationToken);
        }

        /// <summary>Adds the person, or updates them if they're already there. Safe to repeat.</summary>
        public Task<Contact> UpsertAsync(string audienceId, ContactInput contact, CancellationToken cancellationToken = default)
        {
            RequireId(audienceId, nameof(audienceId));
            if (contact is null)
            {
                throw new ArgumentNullException(nameof(contact));
            }
            return _client.RequestAsync<Contact>(HttpMethod.Put, AudiencePath(audienceId), contact, null, cancellationToken);
        }

        /// <summary>Adds or updates up to 100 people. A bad entry doesn't throw: check each result's <see cref="ContactBatchEntry.Ok"/>.</summary>
        public Task<ContactBatchResult> UpsertBatchAsync(
            string audienceId, IEnumerable<ContactInput> contacts, SendOptions? options = null, CancellationToken cancellationToken = default)
        {
            RequireId(audienceId, nameof(audienceId));
            if (contacts is null)
            {
                throw new ArgumentNullException(nameof(contacts));
            }
            var body = new BatchBody { Contacts = contacts.ToList() };
            return _client.RequestAsync<ContactBatchResult>(
                HttpMethod.Post, AudiencePath(audienceId) + "/batch", body, options?.IdempotencyKey, cancellationToken);
        }

        /// <summary>A contact and the latest emails sent to them.</summary>
        public Task<ContactDetail> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            RequireId(id, nameof(id));
            return _client.RequestAsync<ContactDetail>(HttpMethod.Get, ContactPath(id), null, null, cancellationToken);
        }

        /// <summary>Edits names and fields (fields are replaced, not merged: use <see cref="UpsertAsync"/> to merge), or unsubscribes.</summary>
        public Task<Contact> UpdateAsync(string id, ContactUpdate changes, CancellationToken cancellationToken = default)
        {
            RequireId(id, nameof(id));
            if (changes is null)
            {
                throw new ArgumentNullException(nameof(changes));
            }
            return _client.RequestAsync<Contact>(Patch, ContactPath(id), changes, null, cancellationToken);
        }

        /// <summary>Unsubscribes them. Only they can undo it, by confirming a double opt-in email.</summary>
        public Task<Contact> UnsubscribeAsync(string id, CancellationToken cancellationToken = default) =>
            UpdateAsync(id, new ContactUpdate { Unsubscribe = true }, cancellationToken);

        /// <summary>Removes a contact. Deleting someone who unsubscribed keeps their address suppressed.</summary>
        public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            RequireId(id, nameof(id));
            await _client.RequestAsync<NoContent>(HttpMethod.Delete, ContactPath(id), null, null, cancellationToken).ConfigureAwait(false);
        }

        private static void RequireId(string id, string name)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("An id is required.", name);
            }
        }

        private static string AudiencePath(string audienceId) => "/send/audiences/" + Uri.EscapeDataString(audienceId) + "/contacts";

        private static string ContactPath(string id) => "/send/contacts/" + Uri.EscapeDataString(id);

        /// <summary>"?email=a%2Bb%40example.com&amp;page=2": percent-encoded, with unset filters left out.</summary>
        internal static string QueryString(ListContactsOptions? options)
        {
            if (options is null)
            {
                return string.Empty;
            }
            var pairs = new List<string>();
            void Add(string name, string? value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    pairs.Add(name + "=" + Uri.EscapeDataString(value!));
                }
            }
            Add("email", options.Email);
            Add("q", options.Q);
            Add("status", options.Status);
            Add("page", options.Page?.ToString(CultureInfo.InvariantCulture));
            Add("page_size", options.PageSize?.ToString(CultureInfo.InvariantCulture));
            return pairs.Count == 0 ? string.Empty : "?" + string.Join("&", pairs);
        }

        private sealed class BatchBody
        {
            [JsonPropertyName("contacts")]
            public List<ContactInput> Contacts { get; set; } = new List<ContactInput>();
        }
    }

    /// <summary>An empty response body (a 204).</summary>
    internal sealed class NoContent
    {
    }
}
