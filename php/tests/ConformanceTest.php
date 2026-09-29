<?php

declare(strict_types=1);

namespace Mailhive\Tests;

use Mailhive\Exception;
use Mailhive\Mailhive;
use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\TestCase;

/** The shared conformance suite (spec/conformance.json). */
final class ConformanceTest extends TestCase
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
        foreach (Spec::load('conformance.json')['cases'] as $case) {
            yield $case['name'] => [$case];
        }
    }

    #[DataProvider('cases')]
    public function testCase(array $case): void
    {
        MockServer::reset();
        $spec = Spec::load('conformance.json');
        $client = new Mailhive($case['key'], ['base_url' => MockServer::url() . '/v1', 'max_retries' => 2]);
        $options = isset($case['idempotencyKey']) ? ['idempotency_key' => $case['idempotencyKey']] : [];
        $want = $case['expect'];

        $started = microtime(true);
        $result = null;
        $error = null;
        try {
            $result = match ($case['call']) {
                'emails.send' => $client->emails->send($spec['email'], $options),
                'emails.sendBatch' => $client->emails->sendBatch([$spec['email'], $spec['email']], $options),
                'emails.get' => $client->emails->get($case['id']),
            };
        } catch (Exception\MailhiveException $caught) {
            $error = $caught;
        }
        $elapsedMs = (microtime(true) - $started) * 1000;
        $requests = MockServer::requests();

        self::assertCount($want['attempts'], $requests);
        foreach (['result', 'resultFields'] as $field) {
            if (isset($want[$field])) {
                $expected = $want[$field];
                if (array_is_list($expected)) {
                    foreach ($expected as $i => $item) {
                        self::assertSame($item, array_intersect_key($result[$i], $item));
                    }
                } else {
                    self::assertSame($expected, array_intersect_key($result, $expected));
                }
            }
        }
        if (isset($want['minElapsedMs'])) {
            self::assertGreaterThanOrEqual($want['minElapsedMs'], $elapsedMs);
        }
        if (!empty($want['sameIdempotencyKey'])) {
            $keys = array_unique(array_map(static fn ($r) => $r['headers']['idempotency-key'] ?? null, $requests));
            self::assertCount(1, $keys);
            self::assertNotNull($keys[0]);
        }
        if (isset($want['error'])) {
            $expected = $want['error'];
            self::assertInstanceOf(self::KINDS[$expected['kind']], $error);
            self::assertSame($expected['status'], $error->status());
            self::assertSame($expected['code'], $error->errorCode());
            if (isset($expected['requestId'])) {
                self::assertSame($expected['requestId'], $error->requestId());
            }
            if (!empty($expected['hasDetails'])) {
                self::assertNotEmpty($error->details());
            }
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
                'userAgentPattern' => self::assertMatchesRegularExpression("~{$value}~", $headers['user-agent']),
                'idempotencyKeyPattern' => self::assertMatchesRegularExpression("~{$value}~", $headers['idempotency-key']),
                'idempotencyKey' => self::assertSame($value, $headers['idempotency-key']),
                'noIdempotencyKey' => self::assertArrayNotHasKey('idempotency-key', $headers),
                'body' => self::assertSame($value, $first['body']),
                'bodyEmailCount' => self::assertCount($value, $first['body']['emails']),
            };
        }
    }
}
