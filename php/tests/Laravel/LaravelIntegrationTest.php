<?php

declare(strict_types=1);

namespace Mailhive\Tests\Laravel;

use Mailhive\Tests\FakeTransport;

use Illuminate\Mail\Mailable;
use Illuminate\Mail\Mailables\Content;
use Illuminate\Mail\Mailables\Envelope;
use Illuminate\Mail\Mailables\Headers;
use Illuminate\Support\Facades\Mail;
use Mailhive\Laravel\MailhiveServiceProvider;
use Mailhive\Mailhive;
use Orchestra\Testbench\TestCase;

final class ReceiptMail extends Mailable
{
    public function envelope(): Envelope
    {
        return new Envelope(subject: 'Your receipt', tags: ['receipt'], metadata: ['order_id' => '1042']);
    }

    public function content(): Content
    {
        return new Content(htmlString: '<p>Thanks for your order.</p>');
    }

    public function headers(): Headers
    {
        return new Headers(text: ['X-Mailhive-Idempotency-Key' => 'order-1042-receipt']);
    }
}

/** A real Laravel app (Testbench) sending through MAIL_MAILER=mailhive. */
final class LaravelIntegrationTest extends TestCase
{
    private FakeTransport $api;

    protected function getPackageProviders($app): array
    {
        return [MailhiveServiceProvider::class];
    }

    protected function defineEnvironment($app): void
    {
        $app['config']->set('mail.default', 'mailhive');
        $app['config']->set('mail.mailers.mailhive', ['transport' => 'mailhive']);
        $app['config']->set('mail.from', ['address' => 'hello@acme.com', 'name' => 'Acme']);
        $app['config']->set('services.mailhive', ['key' => 'mhs_laravel']);
        $this->api = FakeTransport::json(200, ['id' => 'm7', 'status' => 'queued', 'suppressed' => [], 'test' => false]);
        // The container's client talks to the fake API.
        $app->singleton(Mailhive::class, fn () => new Mailhive('mhs_laravel', ['transport' => $this->api]));
    }

    public function testAMailableGoesThroughMailhive(): void
    {
        Mail::to('ada@example.com')->send(new ReceiptMail());

        self::assertCount(1, $this->api->requests);
        $request = $this->api->requests[0];
        self::assertSame('Bearer mhs_laravel', $request['headers']['Authorization']);
        self::assertSame('order-1042-receipt', $request['headers']['Idempotency-Key']);
        $body = $this->api->lastBody();
        self::assertSame('"Acme" <hello@acme.com>', $body['from']);
        self::assertSame(['ada@example.com'], $body['to']);
        self::assertSame('Your receipt', $body['subject']);
        self::assertStringContainsString('Thanks for your order.', $body['html']);
        self::assertSame(['receipt' => 'true', 'order_id' => '1042'], $body['tags']);
    }

    public function testTheClientIsInTheContainer(): void
    {
        self::assertInstanceOf(Mailhive::class, $this->app->make(Mailhive::class));
    }
}
