<?php

declare(strict_types=1);

namespace Mailhive;

/** Audiences: the lists your broadcasts go to. Needs an API key with Full access. */
final class Audiences
{
    public function __construct(private readonly Mailhive $client)
    {
    }

    /** @return list<array{id: string, name: string, contacts?: int, mailable?: int}> */
    public function list(): array
    {
        return $this->client->request('GET', '/send/audiences');
    }

    /**
     * @param array{idempotency_key?: string} $options
     * @return array<string, mixed>
     */
    public function create(string $name, array $options = []): array
    {
        return $this->client->request('POST', '/send/audiences', ['name' => $name], $options['idempotency_key'] ?? null);
    }

    /** @return array{id: string, name: string} */
    public function rename(string $id, string $name): array
    {
        return $this->client->request('PATCH', '/send/audiences/' . rawurlencode($id), ['name' => $name]);
    }
}
