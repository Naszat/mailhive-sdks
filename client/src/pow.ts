// Solves Mailhive's proof-of-work challenge (ALTCHA format): find the number
// n in 0..maxnumber where sha256(salt + n) is the challenge. Each digest is
// awaited, so the page stays responsive while it works (about a second).

export interface Challenge {
  algorithm: string;
  challenge: string;
  maxnumber: number;
  salt: string;
  signature: string;
}

export interface Solution {
  algorithm: string;
  challenge: string;
  salt: string;
  signature: string;
  number: number;
}

const encoder = new TextEncoder();

function hex(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let out = "";
  for (let i = 0; i < bytes.length; i++) out += bytes[i]!.toString(16).padStart(2, "0");
  return out;
}

export async function solve(challenge: Challenge, signal?: AbortSignal): Promise<Solution> {
  if (challenge.algorithm !== "SHA-256") throw new Error(`Unsupported challenge algorithm ${challenge.algorithm}.`);
  const target = challenge.challenge.toLowerCase();
  for (let number = 0; number <= challenge.maxnumber; number++) {
    if (number % 1000 === 0 && signal?.aborted) throw signal.reason;
    const digest = await crypto.subtle.digest("SHA-256", encoder.encode(challenge.salt + number));
    if (hex(digest) === target) {
      const { algorithm, salt, signature } = challenge;
      return { algorithm, challenge: challenge.challenge, salt, signature, number };
    }
  }
  throw new Error("The challenge has no solution.");
}
