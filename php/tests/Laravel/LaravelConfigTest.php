<?php

declare(strict_types=1);

namespace Mailhive\Tests\Laravel;

use Mailhive\Tests\FakeTransport;

use Illuminate\Support\Facades\Mail;
use Mailhive\Laravel\MailhiveServiceProvider;
use Mailhive\Mailhive;
use Orchestra\Testbench\TestCase;

/** The provider's own client, built from config/services.php. */
final class LaravelConfigTest extends TestCase
{
    protected function getPackageProviders($app): array
    {
        return [MailhiveServiceProvider::class];
    }

    protected function defineEnvironment($app): void
    {
        $app['config']->set('services.mailhive', ['key' => 'mhs_config', 'base_url' => 'https://api-beta.mailhive.africa/v1']);
    }

    public function testTheClientComesFromServicesConfig(): void
    {
        $client = $this->app->make(Mailhive::class);
        self::assertSame('https://api-beta.mailhive.africa/v1', $client->baseUrl);
        self::assertSame($client, $this->app->make(Mailhive::class));
        $this->app['config']->set('mail.mailers.mailhive', ['transport' => 'mailhive']);
        self::assertSame('mailhive', (string) Mail::mailer('mailhive')->getSymfonyTransport());
    }

}
