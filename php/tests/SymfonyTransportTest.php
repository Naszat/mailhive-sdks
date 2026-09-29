<?php

declare(strict_types=1);

namespace Mailhive\Tests;

use Mailhive\Laravel\MailhiveTransport;
use Mailhive\Mailhive;
use PHPUnit\Framework\TestCase;
use Symfony\Component\Mailer\Header\MetadataHeader;
use Symfony\Component\Mailer\Header\TagHeader;
use Symfony\Component\Mime\Address;
use Symfony\Component\Mime\Email;

final class SymfonyTransportTest extends TestCase
{
    public function testSendsASymfonyEmailThroughTheApi(): void
    {
        $transport = FakeTransport::json(200, ['id' => 'm42', 'status' => 'queued', 'suppressed' => [], 'test' => false]);
        $mailer = new MailhiveTransport(new Mailhive('mhs_x', ['transport' => $transport]));

        $email = (new Email())
            ->from(new Address('hello@acme.com', 'Acme'))
            ->to('ada@example.com')
            ->cc('grace@example.com')
            ->bcc('audit@acme.com')
            ->replyTo('support@acme.com')
            ->subject('Your receipt')
            ->text('Thanks')
            ->html('<p>Thanks</p>')
            ->attach('Paid', 'receipt.txt', 'text/plain');
        $email->getHeaders()->addTextHeader('X-Order', '1042');
        $email->getHeaders()->addTextHeader(MailhiveTransport::IDEMPOTENCY_HEADER, 'order-1042');
        $email->getHeaders()->add(new TagHeader('receipt'));
        $email->getHeaders()->add(new MetadataHeader('order_id', '1042'));

        $sent = $mailer->send($email);

        self::assertSame('m42', $sent->getMessageId());
        self::assertSame('order-1042', $transport->requests[0]['headers']['Idempotency-Key']);
        self::assertSame([
            'from' => '"Acme" <hello@acme.com>',
            'to' => ['ada@example.com'],
            'subject' => 'Your receipt',
            'cc' => ['grace@example.com'],
            'bcc' => ['audit@acme.com'],
            'reply_to' => ['support@acme.com'],
            'html' => '<p>Thanks</p>',
            'text' => 'Thanks',
            'headers' => ['X-Order' => '1042'],
            'tags' => ['receipt' => 'true', 'order_id' => '1042'],
            'attachments' => [['filename' => 'receipt.txt', 'content' => base64_encode('Paid'), 'content_type' => 'text/plain']],
        ], $transport->lastBody());
        self::assertSame('mailhive', (string) $mailer);
    }
}
