// sarvaghna/core/gemini-client.mjs
// Shared Gemini JSON-mode caller. Extracted from rules/semantic-reviewer.mjs
// (Tier 4) so every crew agent that wants real reasoning instead of
// regex/templates calls the model the same verified way, with the same
// graceful degradation when GEMINI_API_KEY is absent.
//
// DISC-14: this key's own request-count quota (e.g. a free-tier key's 20/day —
// confirmed the hard way via a real HTTP 429 RESOURCE_EXHAUSTED during D5-T8's
// .NET eval harness) is shared with Sarvagna's own .NET callers (API, CLI eval).
// Both sides check and record against the identical file-based ledger at
// sarvaghna/ledger/gemini-requests.jsonl — a dollar-cost budget guard alone
// would never see this coming, since a free-tier call costs $0.

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const DEFAULT_LEDGER_PATH = path.resolve(__dirname, '..', 'ledger', 'gemini-requests.jsonl');
const DEFAULT_DAILY_REQUEST_QUOTA = 20;

// SARVAGHNA_QUOTA_LEDGER_PATH lets tests redirect to an isolated temp file — the real, shared
// ledger at sarvaghna/ledger/gemini-requests.jsonl must never gain test noise, and a test must
// never see another test's (or a real run's) request count.
function ledgerPath() {
  return process.env.SARVAGHNA_QUOTA_LEDGER_PATH || DEFAULT_LEDGER_PATH;
}

function countTodayRequests() {
  const p = ledgerPath();
  if (!fs.existsSync(p)) return 0;
  const todayUtc = new Date().toISOString().slice(0, 10); // YYYY-MM-DD
  const lines = fs.readFileSync(p, 'utf8').split('\n').filter(Boolean);
  let count = 0;
  for (const line of lines) {
    try {
      const entry = JSON.parse(line);
      if (typeof entry.timestampUtc === 'string' && entry.timestampUtc.slice(0, 10) === todayUtc) count++;
    } catch {
      // A malformed line (e.g. truncated by a crash mid-write) is skipped, not fatal —
      // undercounting here is the safe direction, same reasoning the .NET
      // FileAiRequestQuotaLedger's own doc comment gives.
    }
  }
  return count;
}

function recordRequest(capability) {
  const p = ledgerPath();
  fs.mkdirSync(path.dirname(p), { recursive: true });
  const entry = { timestampUtc: new Date().toISOString(), source: 'sarvaghna-node', capability };
  fs.appendFileSync(p, JSON.stringify(entry) + '\n');
}

/**
 * Calls Gemini with a prompt and expects a JSON object back
 * (responseMimeType: 'application/json'). Never throws — callers always
 * get a result object and decide what to do when ok is false.
 *
 * @param {Object} params
 * @param {string} params.prompt
 * @param {string} [params.model]
 * @param {number} [params.temperature]
 * @param {number} [params.maxOutputTokens]
 * @param {number} [params.dailyRequestQuota] - shared quota ceiling (default 20, this project's observed free-tier limit)
 * @param {string} [params.capability] - recorded in the shared ledger, e.g. 'semantic-review', 'crew-analyst'
 * @returns {Promise<{ok: true, data: any} | {ok: false, reason: string}>}
 */
export async function callGeminiJson({
  prompt, model = 'gemini-3.8-flash', temperature = 0.2, maxOutputTokens = 4096,
  dailyRequestQuota = DEFAULT_DAILY_REQUEST_QUOTA, capability = 'sarvaghna'
}) {
  const apiKey = process.env.GEMINI_API_KEY;
  if (!apiKey) {
    return { ok: false, reason: 'GEMINI_API_KEY not configured.' };
  }

  const requestsToday = countTodayRequests();
  if (requestsToday >= dailyRequestQuota) {
    return {
      ok: false,
      reason: `AI request quota reached: ${requestsToday} of ${dailyRequestQuota} requests already made today ` +
        '(shared with Sarvagna\'s own .NET AI generation feature via sarvaghna/ledger/gemini-requests.jsonl). Refused before calling.'
    };
  }

  const endpoint = `https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent?key=${apiKey}`;

  try {
    const response = await fetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        contents: [{ role: 'user', parts: [{ text: prompt }] }],
        generationConfig: {
          temperature,
          maxOutputTokens,
          responseMimeType: 'application/json'
        }
      })
    });

    // A response was received — the request reached Gemini and counts against quota regardless
    // of status code. A thrown network/DNS error below never reached Gemini, so it does not.
    recordRequest(capability);

    if (!response.ok) {
      const errText = await response.text();
      return { ok: false, reason: `Gemini API returned error ${response.status}: ${errText.substring(0, 200)}` };
    }

    const data = await response.json();
    const candidate = data.candidates?.[0];
    const candidateText = candidate?.content?.parts?.[0]?.text;
    if (!candidateText) {
      return { ok: false, reason: `Empty response received from AI model (finishReason: ${candidate?.finishReason || 'unknown'}).` };
    }

    try {
      return { ok: true, data: JSON.parse(candidateText) };
    } catch (parseErr) {
      return { ok: false, reason: `Model output was not valid JSON (finishReason: ${candidate?.finishReason || 'unknown'}): ${parseErr.message}` };
    }
  } catch (err) {
    return { ok: false, reason: `Gemini call failed: ${err.message}` };
  }
}
