<?php

declare(strict_types=1);

namespace Mailhive\Tests;

use Mailhive\Exception\ConnectionException;
use Mailhive\Exception\MailhiveException;
use Mailhive\Exception\RateLimitException;
use Mailhive\Http\TransportException;
use Mailhive\Mailhive;
use PHPUnit\Framework\TestCase;

final class ClientTest extends TestCase
{
    private const ACCEPTED = ['id' => 'm1', 'status' => 'queued', 'suppressed' => [], 'test' => false];

    protected function tearDown(): void
    {
        putenv('MAILHIVE_API_KEY');
        putenv('MAILHIVE_BASE_URL');
    }

    public function testReadsTheKeyAndBaseUrlFromTheEnvironment(): void
    {
        putenv('MAILHIVE_API_KEY=mhs_fromenv');
        putenv('MAILHIVE_BASE_URL=https://api-beta.mailhive.africa/v1/');
        $transport = FakeTransport::json(200, self::ACCEPTED);
        (new Mailhive(null, ['transport' => $transport]))->emails->send(['from' => 'a@acme.com', 'to' => 'b@example.com', 'subject' => 's', 'text' => 't']);
        self::assertSame('https://api-beta.mailhive.africa/v1/send/emails', $transport->requests[0]['url']);
        self::assertSame('Bearer mhs_fromenv', $transport->requests[0]['headers']['Authorization']);
    }

    public function testNeedsAKeyAndRefusesAFormKey(): void
    {
        try {
            new Mailhive();
            self::fail('Expected an exception');
        } catch (MailhiveException $e) {
            self::assertStringContainsString('No API key', $e->getMessage());
        }
        $this->expectExceptionMessageMatches('/publishable key/');
        new Mailhive('mhp_form');
    }

    public function testNeverShowsTheKey(): void
    {
        self::assertStringNotContainsString('supersecret', print_r(new Mailhive('mhs_supersecret'), true));
    }

    public function testEncodesAttachmentsAndSendsEmptyMapsAsObjects(): void
    {
        $transport = FakeTransport::json(200, self::ACCEPTED);
        (new Mailhive('mhs_x', ['transport' => $transport]))->emails->send([
            'from' => 'a@acme.com', 'to' => 'b@example.com', 'subject' => 's', 'text' => 't', 'tags' => [], 'headers' => ['X-Order' => '1'],
            'attachments' => [['filename' => 'a.txt', 'content' => 'hello'], ['filename' => 'b.txt', 'content_base64' => 'aGk=']],
        ]);
        $body = $transport->lastBody();
        self::assertSame([base64_encode('hello'), 'aGk='], array_column($body['attachments'], 'content'));
        self::assertArrayNotHasKey('tags', $body);
        self::assertStringContainsString('"headers":{"X-Order":"1"}', (string) $transport->requests[0]['body']);
    }

    public function testGivesUpOnARetryAfterLongerThanAMinute(): void
    {
        $transport = FakeTransport::json(429, ['error' => ['code' => 'rate_limited', 'message' => 'slow']], ['retry-after' => '3600']);
        try {
            (new Mailhive('mhs_x', ['transport' => $transport]))->emails->get('m1');
            self::fail('Expected a rate limit');
        } catch (RateLimitException $e) {
            self::assertSame(3600.0, $e->retryAfter());
            self::assertCount(1, $transport->requests);
        }
    }

    public function testRetriesADroppedConnectionThenReportsIt(): void
    {
        $slept = [];
        $transport = new FakeTransport(static fn () => throw new TransportException('timed out', true));
        $client = new Mailhive('mhs_x', ['transport' => $transport, 'timeout' => 5, 'sleep' => function (float $s) use (&$slept) { $slept[] = $s; }]);
        try {
            $client->emails->get('m1');
            self::fail('Expected a connection error');
        } catch (ConnectionException $e) {
            self::assertStringContainsString("didn't answer within 5 seconds", $e->getMessage());
        }
        self::assertCount(3, $transport->requests);
        self::assertCount(2, $slept);
    }
}
