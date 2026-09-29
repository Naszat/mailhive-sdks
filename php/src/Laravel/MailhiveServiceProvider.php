<?php

declare(strict_types=1);

namespace Mailhive\Laravel;

use Illuminate\Support\Facades\Mail;
use Illuminate\Support\ServiceProvider;
use Mailhive\Mailhive;

/**
 * Registered automatically by Laravel's package discovery.
 *
 *     // config/services.php
 *     'mailhive' => ['key' => env('MAILHIVE_API_KEY')],
 *
 *     // config/mail.php, under 'mailers'
 *     'mailhive' => ['transport' => 'mailhive'],
 *
 *     // .env
 *     MAIL_MAILER=mailhive
 *
 * The client is also bound in the container: type-hint Mailhive\Mailhive.
 */
final class MailhiveServiceProvider extends ServiceProvider
{
    public function register(): void
    {
        $this->app->singleton(Mailhive::class, static function ($app): Mailhive {
            $config = $app['config']->get('services.mailhive', []);
            return new Mailhive($config['key'] ?? null, array_filter(['base_url' => $config['base_url'] ?? null]));
        });
    }

    public function boot(): void
    {
        Mail::extend('mailhive', function (array $config = []): MailhiveTransport {
            $client = isset($config['key'])
                ? new Mailhive($config['key'], array_filter(['base_url' => $config['base_url'] ?? null]))
                : $this->app->make(Mailhive::class);
            return new MailhiveTransport($client);
        });
    }
}
