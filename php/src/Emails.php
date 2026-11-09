<?php

declare(strict_types=1);

namespace Mailhive;

final class Emails
{
    public function __construct(private readonly Mailhive $client)
    {
    }

    /**
     * Sends one email. Keys match the API reference: from, to, cc, bcc,
     * subject, html, text, template_id, variables, reply_to, headers, tags,
     * attachments. An attachment's 'content' is the raw file (encoded for
     * you); use 'content_base64' for data that's already base64.
     *
     * @param array<string, mixed> $email
     * @param array{idempotency_key?: string} $options
     * @return array{id: string, status: string, suppressed: list<string>, rejected: array<string, string>, test: bool}
     *   'rejected' maps each address dropped before sending to why it can't work
     *   (invalid_syntax, domain_typo, domain_not_found, no_mail_server); it's [] when none were.
     */
    public function send(array $email, array $options = []): array
    {
        return $this->client->request('POST', '/send/emails', self::encode($email), $options['idempotency_key'] ?? null);
    }

    /**
     * Up to 100 independent emails; all are accepted, or none.
     *
     * @param list<array<string, mixed>> $emails
     * @param array{idempotency_key?: string} $options
     * @return list<array{id: string, status: string, suppressed: list<string>, rejected: array<string, string>, test: bool}>
     */
    public function sendBatch(array $emails, array $options = []): array
    {
        $response = $this->client->request(
            'POST',
            '/send/emails/batch',
            ['emails' => array_map([self::class, 'encode'], $emails)],
            $options['idempotency_key'] ?? null,
        );
        return $response['data'];
    }

    /** @return array<string, mixed> */
    public function get(string $id): array
    {
        return $this->client->request('GET', '/send/emails/' . rawurlencode($id));
    }

    /**
     * The same checks sending applies, for a form to run while the person
     * can still fix a typo: syntax, mistyped domains (gmial.com, with the
     * address probably meant in 'suggestion'), domains that don't exist or
     * have no mail server, and your suppression list. Nothing is sent or
     * stored. Up to 100 addresses, as typed; one verdict each, in order.
     *
     * @param list<string> $emails
     * @return list<array{email: string, normalized: ?string, valid: bool, reason: ?string, message: ?string, suggestion: ?string}>
     */
    public function validate(array $emails): array
    {
        $response = $this->client->request('POST', '/send/emails/validate', ['emails' => array_values($emails)]);
        return $response['data'];
    }

    /**
     * @param array<string, mixed> $email
     * @return array<string, mixed>
     */
    public static function encode(array $email): array
    {
        // Empty PHP arrays would be sent as JSON lists; these are objects.
        foreach (['headers', 'tags', 'variables'] as $map) {
            if (array_key_exists($map, $email)) {
                if ($email[$map] === [] || $email[$map] === null) {
                    unset($email[$map]);
                } else {
                    $email[$map] = (object) $email[$map];
                }
            }
        }
        if (!empty($email['attachments'])) {
            $email['attachments'] = array_map(static function (array $attachment): array {
                if (array_key_exists('content_base64', $attachment)) {
                    $attachment['content'] = $attachment['content_base64'];
                    unset($attachment['content_base64']);
                } else {
                    $attachment['content'] = base64_encode((string) ($attachment['content'] ?? ''));
                }
                return $attachment;
            }, $email['attachments']);
        }
        return $email;
    }
}
