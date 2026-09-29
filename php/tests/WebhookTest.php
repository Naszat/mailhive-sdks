<?php

declare(strict_types=1);

namespace Mailhive\Tests;

use Mailhive\Exception\WebhookVerificationException;
use Mailhive\Mailhive;
use Mailhive\Webhook;
use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\TestCase;

final class WebhookTest extends TestCase
{
    public static function vectors(): iterable
    {
        foreach (Spec::load('webhook-vectors.json')['cases'] as $vector) {
            yield $vector['name'] => [$vector];
        }
    }

    #[DataProvider('vectors')]
    public function testVectorsSignedByTheBackend(array $v): void
    {
        if ($v['valid']) {
            self::assertSame('email.delivered', Webhook::verify($v['payload'], $v['header'], $v['secret'], 300, $v['now'])['type']);
            return;
        }
        try {
            Webhook::verify($v['payload'], $v['header'], $v['secret'], 300, $v['now']);
            self::fail('Expected the signature to be refused.');
        } catch (WebhookVerificationException $e) {
            self::assertSame($v['reason'], $e->reason());
        }
    }

    public function testAlsoOnTheClient(): void
    {
        $v = Spec::load('webhook-vectors.json')['cases'][0];
        self::assertNotEmpty(Mailhive::verifyWebhook($v['payload'], $v['header'], $v['secret'], 300, $v['now']));
    }
}
