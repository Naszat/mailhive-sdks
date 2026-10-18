<?php

declare(strict_types=1);

namespace Mailhive;

/**
 * The people in an audience. Needs an API key with Full access.
 *
 * Someone who unsubscribed, or reported your email as spam, can't be made
 * subscribed again by these calls (a ConflictException with code
 * contact_unsubscribed). Pass 'double_opt_in' => ['from' => ..., 'subject' => ...]
 * to email them a confirmation link instead: they're subscribed only if they
 * click it, and until then they're 'pending' and never mailed.
 *
 * Contact keys match the API reference: email, first_name, last_name, fields,
 * status, double_opt_in.
 */
final class Contacts
{
    public function __construct(private readonly Mailhive $client)
    {
    }

    /**
     * A page of contacts. Filters: email (one person, exact address), q,
     * status (subscribed, unsubscribed or pending), page and page_size.
     *
     * @param array{email?: string, q?: string, status?: string, page?: int, page_size?: int} $filters
     * @return array{items: list<array<string, mixed>>, page: int, page_size: int, total: int}
     */
    public function list(string $audienceId, array $filters = []): array
    {
        $query = [];
        foreach (['email', 'q', 'status', 'page', 'page_size'] as $name) {
            $query[$name] = $filters[$name] ?? null;
        }
        return $this->client->request('GET', self::audience($audienceId), null, null, $query);
    }

    /**
     * Adds one person. A 409 contact_exists if they're already there: use upsert().
     *
     * @param array<string, mixed> $contact
     * @param array{idempotency_key?: string} $options
     * @return array<string, mixed>
     */
    public function add(string $audienceId, array $contact, array $options = []): array
    {
        return $this->client->request('POST', self::audience($audienceId), self::encode($contact), $options['idempotency_key'] ?? null);
    }

    /**
     * Adds the person, or updates them if they're already there. Safe to repeat.
     *
     * @param array<string, mixed> $contact
     * @return array<string, mixed>
     */
    public function upsert(string $audienceId, array $contact): array
    {
        return $this->client->request('PUT', self::audience($audienceId), self::encode($contact));
    }

    /**
     * Adds or updates up to 100 people. A bad entry doesn't throw: check each result's 'ok'.
     *
     * @param list<array<string, mixed>> $contacts
     * @param array{idempotency_key?: string} $options
     * @return array{created: int, updated: int, failed: int, results: list<array<string, mixed>>}
     */
    public function upsertBatch(string $audienceId, array $contacts, array $options = []): array
    {
        return $this->client->request(
            'POST',
            self::audience($audienceId) . '/batch',
            ['contacts' => array_map([self::class, 'encode'], array_values($contacts))],
            $options['idempotency_key'] ?? null,
        );
    }

    /** A contact and the latest emails sent to them. @return array<string, mixed> */
    public function get(string $id): array
    {
        return $this->client->request('GET', self::contact($id));
    }

    /**
     * Edits names and fields (fields are replaced, not merged: use upsert() to
     * merge). Keys: first_name, last_name, fields, unsubscribe.
     *
     * @param array<string, mixed> $changes
     * @return array<string, mixed>
     */
    public function update(string $id, array $changes): array
    {
        if (array_key_exists('fields', $changes)) {
            // Replacing with nothing must be {}, not [].
            $changes['fields'] = (object) $changes['fields'];
        }
        return $this->client->request('PATCH', self::contact($id), $changes);
    }

    /** Unsubscribes them. Only they can undo it, by confirming a double opt-in email. @return array<string, mixed> */
    public function unsubscribe(string $id): array
    {
        return $this->update($id, ['unsubscribe' => true]);
    }

    /** Deleting someone who unsubscribed keeps their address suppressed. */
    public function delete(string $id): void
    {
        $this->client->request('DELETE', self::contact($id));
    }

    /**
     * @param array<string, mixed> $contact
     * @return array<string, mixed>
     */
    public static function encode(array $contact): array
    {
        // An empty PHP array would be sent as a JSON list; fields is an object.
        if (array_key_exists('fields', $contact)) {
            if ($contact['fields'] === [] || $contact['fields'] === null) {
                unset($contact['fields']);
            } else {
                $contact['fields'] = (object) $contact['fields'];
            }
        }
        return $contact;
    }

    private static function audience(string $id): string
    {
        return '/send/audiences/' . rawurlencode($id) . '/contacts';
    }

    private static function contact(string $id): string
    {
        return '/send/contacts/' . rawurlencode($id);
    }
}
