<?php

declare(strict_types=1);

namespace Mailhive\Tests;

use Mailhive\Exception;
use Mailhive\Mailhive;
use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\TestCase;

/** spec/conformance.json "contactCases": audiences and contacts. */
final class ContactsConformanceTest extends TestCase
{
    private const KINDS = [
        'api' => Exception\ApiException::class,
        'authentication' => Exception\AuthenticationException::class,
        'billing' => Exception\BillingException::class,
        'permission' => Exception\PermissionException::class,
        'not_found' => Exception\NotFoundException::class,
        'conflict' => Exception\ConflictException::class,
        'validation' => Exception\ValidationException::class,
        'rate_limit' => Exception\RateLimitException::class,
    ];

    public static function cases(): iterable
    {
        foreach (Spec::load('conformance.json')['contactCases']['cases'] as $case) {
            yield $case['name'] => [$case];
        }
    }

    /** A string naming a fixture stands for it; anything else is itself. */
    private static function fixture(mixed $value, array $spec): mixed
    {
        return is_string($value) && array_key_exists($value, $spec) ? $spec[$value] : $value;
    }

    private static function at(mixed $value, string $path): mixed
    {
        foreach (explode('.', $path) as $part) {
            $value = $value[ctype_digit($part) ? (int) $part : $part];
        }
        return $value;
    }

    private static function perform(Mailhive $client, array $case, array $spec): mixed
    {
        $a = $case['args'] ?? [];
        $audience = $spec['audienceId'];
        return match ($case['call']) {
            'audiences.list' => $client->audiences->list(),
            'audiences.create' => $client->audiences->create($a['name']),
            'audiences.rename' => $client->audiences->rename($a['id'], $a['name']),
            'contacts.add' => $client->contacts->add($audience, self::fixture($a['contact'], $spec)),
            'contacts.upsert' => $client->contacts->upsert(
                $audience,
                self::fixture($a['contact'], $spec) + (isset($a['doubleOptIn']) ? ['double_opt_in' => self::fixture($a['doubleOptIn'], $spec)] : []),
            ),
            'contacts.upsertBatch' => $client->contacts->upsertBatch($audience, array_map(static fn ($c) => self::fixture($c, $spec), $a['contacts'])),
            'contacts.list' => $client->contacts->list($audience, array_filter([
                'email' => $a['email'] ?? null,
                'status' => $a['status'] ?? null,
                'page' => $a['page'] ?? null,
                'page_size' => $a['pageSize'] ?? null,
            ], static fn ($v) => $v !== null)),
            'contacts.get' => $client->contacts->get($a['id']),
            'contacts.update' => $client->contacts->update($a['id'], $a['changes']),
            'contacts.unsubscribe' => $client->contacts->unsubscribe($a['id']),
            'contacts.delete' => $client->contacts->delete($a['id']),
        };
    }

    #[DataProvider('cases')]
    public function testCase(array $case): void
    {
        MockServer::reset();
        $spec = Spec::load('conformance.json')['contactCases'];
        $client = new Mailhive($case['key'], ['base_url' => MockServer::url() . '/v1', 'max_retries' => 2]);
        $want = $case['expect'];

        $started = microtime(true);
        $result = null;
        $error = null;
        try {
            $result = self::perform($client, $case, $spec);
        } catch (Exception\MailhiveException $caught) {
            $error = $caught;
        }
        $elapsedMs = (microtime(true) - $started) * 1000;
        $requests = MockServer::requests();

        self::assertCount($want['attempts'], $requests);
        foreach ($want['resultFields'] ?? [] as $path => $expected) {
            self::assertSame($expected, self::at($result, $path), $path);
        }
        if (isset($want['resultCount'])) {
            self::assertCount($want['resultCount'], $result);
        }
        if (!empty($want['resultIsEmpty'])) {
            self::assertNull($result);
        }
        if (isset($want['minElapsedMs'])) {
            self::assertGreaterThanOrEqual($want['minElapsedMs'], $elapsedMs);
        }
        if (isset($want['error'])) {
            $expected = $want['error'];
            self::assertInstanceOf(self::KINDS[$expected['kind']], $error);
            self::assertSame($expected['status'], $error->status());
            self::assertSame($expected['code'], $error->errorCode());
        } else {
            self::assertNull($error, $error?->getMessage() ?? '');
        }

        $first = $requests[0];
        $headers = $first['headers'];
        foreach ($want['request'] ?? [] as $key => $value) {
            match ($key) {
                'method' => self::assertSame($value, $first['method']),
                'path' => self::assertSame($value, $first['path']),
                'authorization' => self::assertSame($value, $headers['authorization']),
                'contentType' => self::assertSame($value, $headers['content-type']),
                'noIdempotencyKey' => self::assertArrayNotHasKey('idempotency-key', $headers),
                'query' => self::assertEquals($value, $first['query']),
                'body' => self::assertEquals(self::fixture($value, $spec), $first['body']),
                'bodyIncludes' => self::assertEquals($value, array_intersect_key($first['body'], $value)),
                'bodyContactCount' => self::assertCount($value, $first['body']['contacts']),
            };
        }
    }
}
