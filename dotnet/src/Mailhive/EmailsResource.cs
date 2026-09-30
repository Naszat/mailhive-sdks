using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Mailhive
{
    /// <summary>Sends and looks up emails: <c>client.Emails</c>.</summary>
    public sealed class EmailsResource
    {
        private readonly MailhiveClient _client;

        internal EmailsResource(MailhiveClient client)
        {
            _client = client;
        }

        /// <summary>Sends one email.</summary>
        /// <exception cref="MailhiveApiException">The API refused it (see the subclasses).</exception>
        /// <exception cref="MailhiveConnectionException">The API couldn't be reached, even after the retries.</exception>
        public Task<AcceptedEmail> SendAsync(SendEmailRequest request, SendOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            return _client.RequestAsync<AcceptedEmail>(HttpMethod.Post, "/send/emails", request, options?.IdempotencyKey, cancellationToken);
        }

        /// <summary>Sends up to 100 independent emails in one request: all are accepted, or none.</summary>
        /// <returns>The accepted emails, in the order given.</returns>
        public async Task<IReadOnlyList<AcceptedEmail>> SendBatchAsync(
            IEnumerable<SendEmailRequest> emails, SendOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (emails is null)
            {
                throw new ArgumentNullException(nameof(emails));
            }
            var body = new BatchRequest { Emails = emails.ToList() };
            var response = await _client
                .RequestAsync<BatchResponse>(HttpMethod.Post, "/send/emails/batch", body, options?.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            return response.Data;
        }

        /// <summary>Looks up an email by the id <see cref="SendAsync"/> returned.</summary>
        /// <exception cref="NotFoundException">No such email.</exception>
        public Task<Email> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("An email id is required.", nameof(id));
            }
            return _client.RequestAsync<Email>(HttpMethod.Get, "/send/emails/" + Uri.EscapeDataString(id), null, null, cancellationToken);
        }

        private sealed class BatchRequest
        {
            [JsonPropertyName("emails")]
            public List<SendEmailRequest> Emails { get; set; } = new List<SendEmailRequest>();
        }

        private sealed class BatchResponse
        {
            [JsonPropertyName("data")]
            public List<AcceptedEmail> Data { get; set; } = new List<AcceptedEmail>();
        }
    }
}
