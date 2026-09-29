<?php

declare(strict_types=1);

namespace Mailhive\Http;

/** Sends one HTTP request. Swap it in tests, or to use your own client. */
interface Transport
{
    /**
     * @param array<string, string> $headers
     * @throws TransportException when there's no response at all
     */
    public function send(string $method, string $url, array $headers, ?string $body, float $timeout): Response;
}
