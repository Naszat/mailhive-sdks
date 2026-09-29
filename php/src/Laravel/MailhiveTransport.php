<?php

declare(strict_types=1);

namespace Mailhive\Laravel;

use Mailhive\Mailhive;
use Psr\EventDispatcher\EventDispatcherInterface;
use Psr\Log\LoggerInterface;
use Symfony\Component\Mailer\Header\MetadataHeader;
use Symfony\Component\Mailer\Header\TagHeader;
use Symfony\Component\Mailer\SentMessage;
use Symfony\Component\Mailer\Transport\AbstractTransport;
use Symfony\Component\Mime\Address;
use Symfony\Component\Mime\Email;
use Symfony\Component\Mime\MessageConverter;

/**
 * A Symfony Mailer transport that sends through the Mailhive Send API.
 * Laravel uses it through MailhiveServiceProvider; plain Symfony apps can
 * construct it directly.
 *
 * Laravel's ->tag() and ->metadata() become Mailhive tags. Set an
 * X-Mailhive-Idempotency-Key header to make a send safe to repeat.
 */
final class MailhiveTransport extends AbstractTransport
{
    public const IDEMPOTENCY_HEADER = 'X-Mailhive-Idempotency-Key';

    // Headers the API sets itself, or takes as fields.
    private const OWN_HEADERS = [
        'from', 'to', 'cc', 'bcc', 'reply-to', 'subject', 'date', 'message-id', 'mime-version',
        'content-type', 'content-transfer-encoding', 'sender', 'return-path', 'x-tag',
        'x-mailhive-idempotency-key', 'x-mailhive-email-id',
    ];

    public function __construct(
        private readonly Mailhive $client,
        ?EventDispatcherInterface $dispatcher = null,
        ?LoggerInterface $logger = null,
    ) {
        parent::__construct($dispatcher, $logger);
    }

    protected function doSend(SentMessage $message): void
    {
        $email = MessageConverter::toEmail($message->getOriginalMessage());
        $idempotencyKey = $email->getHeaders()->get(self::IDEMPOTENCY_HEADER)?->getBodyAsString();
        $accepted = $this->client->emails->send(
            self::params($email, array_map(static fn (Address $a) => $a->getAddress(), $message->getEnvelope()->getRecipients())),
            $idempotencyKey ? ['idempotency_key' => $idempotencyKey] : [],
        );
        $message->setMessageId($accepted['id']);
        $email->getHeaders()->addTextHeader('X-Mailhive-Email-Id', $accepted['id']);
    }

    /**
     * The API request for a Symfony Email.
     *
     * @param list<string> $envelopeRecipients used when the email has no To
     * @return array<string, mixed>
     */
    public static function params(Email $email, array $envelopeRecipients = []): array
    {
        $list = static fn (array $addresses): array => array_map(static fn (Address $a) => $a->toString(), $addresses);
        $params = [
            'from' => $list($email->getFrom())[0] ?? null,
            'to' => $list($email->getTo()) ?: $envelopeRecipients,
            'subject' => (string) $email->getSubject(),
        ];
        foreach (['cc' => $email->getCc(), 'bcc' => $email->getBcc(), 'reply_to' => $email->getReplyTo()] as $field => $addresses) {
            if ($addresses) {
                $params[$field] = $list($addresses);
            }
        }
        foreach (['html' => $email->getHtmlBody(), 'text' => $email->getTextBody()] as $field => $body) {
            if ($body !== null) {
                $params[$field] = is_resource($body) ? (string) stream_get_contents($body) : (string) $body;
            }
        }
        $headers = [];
        $tags = [];
        foreach ($email->getHeaders()->all() as $header) {
            $name = $header->getName();
            if ($header instanceof TagHeader) {
                $tags[$header->getValue()] = 'true';
            } elseif ($header instanceof MetadataHeader) {
                $tags[$header->getKey()] = $header->getValue();
            } elseif (!in_array(strtolower($name), self::OWN_HEADERS, true)) {
                $headers[$name] = $header->getBodyAsString();
            }
        }
        if ($headers) {
            $params['headers'] = $headers;
        }
        if ($tags) {
            $params['tags'] = $tags;
        }
        foreach ($email->getAttachments() as $part) {
            $params['attachments'][] = [
                'filename' => $part->getFilename() ?? 'attachment',
                'content' => $part->getBody(),
                'content_type' => $part->getMediaType() . '/' . $part->getMediaSubtype(),
            ];
        }
        return $params;
    }

    public function __toString(): string
    {
        return 'mailhive';
    }
}
